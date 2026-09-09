using System.Text.Json;
using System.Text.RegularExpressions;
using Contigo.AiEval.TenantFixtures;
using Contigo.Chat.Application.Capabilities;

namespace Contigo.AiEval;

/// <summary>How one golden case came out.</summary>
internal enum GoldenVerdict
{
    /// <summary>Everything the requirement asked for held.</summary>
    Pass,

    /// <summary>The reply did not match the requirement, but did match exactly the deviation the
    /// case itself declares in <see cref="GoldenCase.KnownGap"/> — a reported, already-named engine
    /// gap, not a regression. Counted separately from <see cref="Pass"/> in the report.</summary>
    KnownGap,

    /// <summary>Neither the requirement nor the declared gap. A real failure.</summary>
    Fail,
}

/// <summary>The scored result of one case, including everything the report prints.</summary>
internal sealed record GoldenCaseOutcome(
    GoldenCase Case,
    GoldenVerdict Verdict,
    string ObservedKind,
    string AnswerMarkdown,
    IReadOnlyList<string> ObservedCorpora,
    IReadOnlyList<string> ObservedActionHrefs,
    int CitationCount,
    int GatewayCallCount,
    bool GuardIntervened,
    string AuditAction,
    IReadOnlyList<string> Failures,
    IReadOnlyList<string> GapNotes);

/// <summary>
/// Scores one Ask turn against one golden case (task E13/F06/US01/T02, ask-golden-set; R-EVD-03).
/// Pure over the HTTP body plus the two observability channels <see cref="AskTurnResult"/> carries
/// — no I/O, so the same turn always scores the same way.
///
/// <para>
/// <b>Always-applied checks</b> (every case, regardless of what its JSON declares), because they
/// are requirements on the engine as a whole rather than on one question:
/// <list type="number">
/// <item>HTTP 200 and a well-formed ADR-024 §6 body.</item>
/// <item>Exactly one audit row for the turn, whose action matches the reply kind (R-ASK-09).</item>
/// <item><c>abstainGuardIntervened=False</c> — R-EVD-03's headline: "hallucination (numeric guard
/// interventions) must be 0 on the golden set". Never relaxed, not even for a known-gap case.</item>
/// <item>No engineer chrome in <c>answerMarkdown</c>: no GUID, no <c>Document:</c>, no "Structured
/// query", no "not wired", no raw calculator trace (R-ASK-08). <c>Document:</c> / "Structured
/// query" are additionally checked against the whole response body, matching the ask-engine task's
/// own Definition of Done.</item>
/// <item>Every action href is a real capability-catalog route with no unresolved placeholder
/// (R-SYS-02: "hrefs are never model-authored").</item>
/// </list>
/// </para>
/// </summary>
internal static class GoldenCaseEvaluator
{
    /// <summary>Engineer chrome that must never reach a rendered reply (R-ASK-08). The last two
    /// are the literal developer-trace strings <c>DeterministicQueryResult.Explanation</c> and
    /// <c>DeterministicQueryPlanner</c> produce — the ones the ask-engine task had to stop leaking
    /// into <c>answerMarkdown</c>, kept here so a regression is caught by the golden set too.</summary>
    private static readonly string[] ForbiddenInMarkdown =
    [
        "Document:", "Structured query", "not wired", "Appendix C rule", "deterministic filter on",
        "no deterministic handler", "citationKey", "PackItem", "SupplierId",
    ];

    /// <summary>Checked against the entire response body, not just the rendered markdown — the
    /// ask-engine task's own Definition of Done phrases these two that way.</summary>
    private static readonly string[] ForbiddenInBody = ["Structured query", "not wired"];

    private static readonly Regex GuidPattern = new(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
        RegexOptions.Compiled);

    private const string GuardInterventionMarker = "abstainGuardIntervened=True";

    public static GoldenCaseOutcome Evaluate(GoldenCase goldenCase, AskTurnResult turn)
    {
        ArgumentNullException.ThrowIfNull(goldenCase);
        ArgumentNullException.ThrowIfNull(turn);

        var always = new List<string>();
        var expectation = new List<string>();

        if (turn.StatusCode != 200)
        {
            return new GoldenCaseOutcome(
                goldenCase, GoldenVerdict.Fail, "<none>", string.Empty, [], [], 0,
                turn.GatewayCalls?.Count ?? 0, GuardIntervened: false, "<none>",
                [$"HTTP {turn.StatusCode} instead of 200; body: {Truncate(turn.Body)}"], []);
        }

        using var document = JsonDocument.Parse(turn.Body);
        var root = document.RootElement;

        var kind = root.GetProperty("kind").GetString() ?? string.Empty;
        var markdown = root.GetProperty("answerMarkdown").GetString() ?? string.Empty;

        var citations = root.GetProperty("citations").EnumerateArray().ToList();
        var corpora = citations
            .Select(c => c.GetProperty("corpus").GetString() ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        var actionHrefs = root.GetProperty("actions").EnumerateArray()
            .Select(a => a.GetProperty("href").GetString() ?? string.Empty)
            .ToList();

        var guardIntervened = turn.AuditEntries.Any(
            entry => entry.Detail.Contains(GuardInterventionMarker, StringComparison.Ordinal));
        var auditAction = turn.AuditEntries.Count == 1 ? turn.AuditEntries[0].Action : "<none>";

        AppendAlwaysChecks(turn, kind, markdown, actionHrefs, guardIntervened, always);
        AppendExpectationChecks(goldenCase, turn, kind, markdown, corpora, actionHrefs, citations.Count, expectation);

        var (verdict, gapNotes) = Score(goldenCase, kind, markdown, always, expectation);

        return new GoldenCaseOutcome(
            goldenCase, verdict, kind, markdown, corpora, actionHrefs, citations.Count,
            turn.GatewayCalls?.Count ?? 0, guardIntervened, auditAction,
            verdict == GoldenVerdict.Fail ? [.. always, .. expectation] : always, gapNotes);
    }

    /// <summary>
    /// A case with no declared gap passes only when every check held. A case that declares one
    /// passes when either (a) the requirement held after all — the gap has been fixed in the
    /// engine, and the report simply stops listing it, no JSON edit needed — or (b) the reply is
    /// exactly the recorded deviation. Any third behaviour still fails, so a declared gap can never
    /// hide a regression. The always-applied checks (guards, chrome, hrefs, audit) are never
    /// forgiven by a gap declaration.
    /// </summary>
    private static (GoldenVerdict Verdict, IReadOnlyList<string> GapNotes) Score(
        GoldenCase goldenCase,
        string kind,
        string markdown,
        List<string> always,
        List<string> expectation)
    {
        if (always.Count > 0)
        {
            return (GoldenVerdict.Fail, []);
        }

        if (expectation.Count == 0)
        {
            return (GoldenVerdict.Pass, []);
        }

        if (goldenCase.KnownGap is not { } gap)
        {
            return (GoldenVerdict.Fail, []);
        }

        if (!string.Equals(kind, gap.ObservedKind, StringComparison.Ordinal))
        {
            return (GoldenVerdict.Fail, []);
        }

        var unmatched = gap.ObservedContains
            .Where(fragment => !markdown.Contains(fragment, StringComparison.Ordinal))
            .ToList();

        if (unmatched.Count > 0)
        {
            expectation.Add(
                $"declared gap '{gap.Id}' expected the reply to still contain " +
                $"[{string.Join(", ", unmatched.Select(Quote))}], and it does not — the engine's " +
                "behaviour changed, so this case needs re-recording.");
            return (GoldenVerdict.Fail, []);
        }

        return (GoldenVerdict.KnownGap,
        [
            $"{gap.Id}: {gap.Note ?? "no note"} (expected kind '{goldenCase.ExpectedKind}', " +
            $"observed '{kind}')",
        ]);
    }

    private static void AppendAlwaysChecks(
        AskTurnResult turn,
        string kind,
        string markdown,
        IReadOnlyList<string> actionHrefs,
        bool guardIntervened,
        List<string> failures)
    {
        // R-EVD-03's headline assertion. Deliberately checked from the engine's own audit row
        // rather than inferred from the reply kind: an empty-pack abstain and a guard-downgraded
        // abstain are the same kind, and only the audit row tells them apart.
        if (guardIntervened)
        {
            failures.Add(
                "a guard intervened on this turn (audit detail carries " +
                $"'{GuardInterventionMarker}') — R-EVD-03 requires zero interventions across the set.");
        }

        if (turn.AuditEntries.Count != 1)
        {
            failures.Add($"expected exactly one audit row for the turn (R-ASK-09), saw {turn.AuditEntries.Count}.");
        }
        else
        {
            var expectedAction = kind switch
            {
                "answer" => "chat.answered",
                "abstain" => "chat.abstained",
                "redirect" => "chat.redirected",
                "refusal" => "chat.refused",
                _ => null,
            };

            if (expectedAction is not null && !string.Equals(turn.AuditEntries[0].Action, expectedAction, StringComparison.Ordinal))
            {
                failures.Add(
                    $"audit action '{turn.AuditEntries[0].Action}' does not match reply kind " +
                    $"'{kind}' (expected '{expectedAction}', R-ASK-09).");
            }
        }

        foreach (var forbidden in ForbiddenInMarkdown)
        {
            if (markdown.Contains(forbidden, StringComparison.Ordinal))
            {
                failures.Add($"answerMarkdown contains engineer chrome {Quote(forbidden)} (R-ASK-08).");
            }
        }

        if (GuidPattern.Match(markdown) is { Success: true } guid)
        {
            failures.Add($"answerMarkdown renders a raw guid ('{guid.Value}') — never shown to a user (R-ASK-08/R-SUP-04).");
        }

        foreach (var forbidden in ForbiddenInBody)
        {
            if (turn.Body.Contains(forbidden, StringComparison.Ordinal))
            {
                failures.Add($"response body contains {Quote(forbidden)} (R-ASK-08).");
            }
        }

        foreach (var href in actionHrefs)
        {
            if (!IsCatalogRoute(href))
            {
                failures.Add(
                    $"action href '{href}' does not resolve to a capability-catalog route " +
                    "(R-SYS-02: hrefs are never model-authored).");
            }
        }
    }

    private static void AppendExpectationChecks(
        GoldenCase goldenCase,
        AskTurnResult turn,
        string kind,
        string markdown,
        IReadOnlyList<string> corpora,
        IReadOnlyList<string> actionHrefs,
        int citationCount,
        List<string> failures)
    {
        if (!string.Equals(kind, goldenCase.ExpectedKind, StringComparison.Ordinal))
        {
            failures.Add($"kind '{kind}' != expected '{goldenCase.ExpectedKind}'.");
        }

        foreach (var corpus in goldenCase.Corpora)
        {
            if (!corpora.Contains(corpus, StringComparer.Ordinal))
            {
                failures.Add(
                    $"expected a '{corpus}' citation; the reply cited " +
                    $"[{(corpora.Count == 0 ? "nothing" : string.Join(", ", corpora))}].");
            }
        }

        if (goldenCase.ExpectNoCitations && citationCount != 0)
        {
            failures.Add($"expected no citations, saw {citationCount}.");
        }

        // A live model does not restate a number the same way twice, so verbatim-number checking is
        // advisory in the manual Foundry mode (AiEvalOptions.DescribeMode names this) — the
        // guard/chrome/href invariants above stay in force there.
        if (!AiEvalOptions.UseFoundry)
        {
            foreach (var number in goldenCase.Numbers)
            {
                if (!markdown.Contains(number, StringComparison.Ordinal))
                {
                    failures.Add($"expected the calculator/pack value {Quote(number)} verbatim in answerMarkdown.");
                }
            }
        }

        foreach (var fragment in goldenCase.MarkdownContains)
        {
            if (!markdown.Contains(fragment, StringComparison.Ordinal))
            {
                failures.Add($"expected answerMarkdown to contain {Quote(fragment)}.");
            }
        }

        foreach (var fragment in goldenCase.BodyContains)
        {
            if (!turn.Body.Contains(fragment, StringComparison.Ordinal))
            {
                failures.Add(
                    $"expected the reply body (citation cards included) to contain {Quote(fragment)}.");
            }
        }

        foreach (var forbidden in goldenCase.Forbidden)
        {
            if (markdown.Contains(forbidden, StringComparison.Ordinal))
            {
                failures.Add($"answerMarkdown contains forbidden text {Quote(forbidden)}.");
            }
        }

        foreach (var href in goldenCase.ActionHrefs)
        {
            if (!actionHrefs.Contains(href, StringComparer.Ordinal))
            {
                failures.Add(
                    $"expected the action href '{href}'; the reply offered " +
                    $"[{(actionHrefs.Count == 0 ? "nothing" : string.Join(", ", actionHrefs))}].");
            }
        }

        if (goldenCase.ExpectAtLeastOneAction && actionHrefs.Count == 0)
        {
            failures.Add("expected at least one action (R-SYS-02), the reply offered none.");
        }

        // Skipped in the manual Foundry mode: the host's own gateway registration is left in place
        // there, so there is no recording decorator to count calls on.
        if (goldenCase.ExpectNoGatewayCall && turn.GatewayCalls is { Count: > 0 } calls)
        {
            failures.Add(
                $"expected zero AI Gateway calls (R-ASK-01/R-ASK-02: off-domain never retrieves), " +
                $"saw [{string.Join(", ", calls)}].");
        }
    }

    /// <summary>
    /// Whether <paramref name="href"/> is one of <see cref="CapabilityCatalog"/>'s own route
    /// patterns, either verbatim or with a placeholder/id suffix substituted by a real GUID — the
    /// four href shapes <c>CapabilityRouting.BuildHref</c> can produce
    /// (<c>/contracts/{id}</c>, <c>/quotes/{id}</c>, <c>/documents?review={id}</c>,
    /// <c>/renewals?select={id}</c>). An unresolved <c>{</c> is rejected outright.
    /// </summary>
    private static bool IsCatalogRoute(string href)
    {
        if (string.IsNullOrWhiteSpace(href) || href.Contains('{', StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var capability in CapabilityCatalog.All)
        {
            var pattern = capability.RoutePattern;

            var placeholderIndex = pattern.IndexOf('{', StringComparison.Ordinal);
            if (placeholderIndex >= 0)
            {
                var prefix = pattern[..placeholderIndex];
                if (href.StartsWith(prefix, StringComparison.Ordinal) &&
                    Guid.TryParse(href[prefix.Length..], out _))
                {
                    return true;
                }

                continue;
            }

            if (string.Equals(href, pattern, StringComparison.Ordinal))
            {
                return true;
            }

            foreach (var separator in new[] { "?select=", "/" })
            {
                var scoped = pattern + separator;
                if (href.StartsWith(scoped, StringComparison.Ordinal) &&
                    Guid.TryParse(href[scoped.Length..], out _))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string Quote(string value) => $"'{value}'";

    private static string Truncate(string value) =>
        value.Length <= 400 ? value : value[..400] + "…";
}
