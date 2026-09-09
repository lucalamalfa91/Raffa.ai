using Contigo.AiEval.TenantFixtures;

namespace Contigo.AiEval;

/// <summary>
/// The AI evaluation set (task E13/F06/US01/T02, ask-golden-set; `inputs/requirements.md` R-EVD-03,
/// spec §15.3; parent story us-01-ask-engine AC-9). Every case in <c>golden/*.json</c> is one
/// xunit test case, so a regression names the exact question that broke rather than "the golden set
/// failed".
///
/// <para>
/// <b>What this proves.</b> Each case runs the real Ask V2 pipeline end to end over HTTP against
/// one of three seeded tenant workspaces (<see cref="TenantFixtureCatalog"/>) with the deterministic
/// fixture gateway — no Foundry endpoint, no network, a pinned clock. The assertions are the ones
/// R-EVD-03 names: the reply <c>kind</c>, the citation corpora, the calculator/pack numbers
/// verbatim, plus (always, for every case) zero guard interventions, no engineer chrome, and every
/// action href resolving to a real capability-catalog route.
/// </para>
///
/// <para>
/// <b>Manual Foundry run</b>: <c>AiEval__UseFoundry=true dotnet test backend/tests/Contigo.AiEval</c>
/// leaves the host's own <c>IAiGateway</c> registration in place, so a configured
/// <c>AiGateway:Endpoint</c> reaches a real deployment (R-AI-01). Verbatim-number checks are
/// advisory in that mode; the invariants are not. See <c>backend/README.md</c> § "Ask V2 AI
/// evaluation set".
/// </para>
/// </summary>
[Trait("Category", "AiEval")]
[Collection(GoldenSetCollection.Name)]
public sealed class GoldenSetTests(GoldenSetRunner runner)
{
    /// <summary>Every case id, as xunit theory data. Strings, not the internal
    /// <c>GoldenCase</c> record: xunit needs the parameter type to be public, and the id is also
    /// the display name a failing run prints.</summary>
    public static TheoryData<string> CaseIds
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var goldenCase in GoldenSet.Cases)
            {
                data.Add(goldenCase.Id);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void Golden_case_behaves_as_the_requirement_specifies(string caseId)
    {
        var outcome = runner.Outcome(caseId);

        Assert.True(
            outcome.Verdict != GoldenVerdict.Fail,
            $"""
             Golden case '{caseId}' failed.
               question : {outcome.Case.Question}
               fixture  : {outcome.Case.TenantFixture}
               intent   : {outcome.Case.Intent}
               expected : {outcome.Case.ExpectedKind}
               observed : {outcome.ObservedKind}
               reasons  : {string.Join("\n              ", outcome.Failures)}
               reply    : {outcome.AnswerMarkdown}
             """);
    }

    /// <summary>
    /// R-EVD-03, verbatim: "a golden set of >= 40 questions x 3 tenants' fixtures". Asserted on the
    /// loaded set rather than on the run, so shrinking the set below the requirement fails even if
    /// every remaining case passes.
    /// </summary>
    [Fact]
    public void Set_covers_at_least_forty_questions_across_the_three_tenant_fixtures()
    {
        Assert.True(
            GoldenSet.Cases.Count >= 40,
            $"R-EVD-03 requires at least 40 golden questions; the set has {GoldenSet.Cases.Count}.");

        foreach (var fixtureKey in TenantFixtureKeys.All)
        {
            Assert.True(
                GoldenSet.Cases.Any(c => c.TenantFixture == fixtureKey),
                $"No golden case runs against the '{fixtureKey}' tenant fixture.");
        }

        // OQ-askv2-006: the set carries both languages, because the answer must follow the
        // question's own language.
        Assert.Contains(GoldenSet.Cases, c => c.Language == "it");
        Assert.Contains(GoldenSet.Cases, c => c.Language == "en");
    }

    /// <summary>
    /// R-EVD-03's headline: "hallucination (numeric guard interventions) must be 0 on the golden
    /// set". Also asserted per case (never relaxed by a known gap), and repeated here as one
    /// set-wide statement so the failure message names every offending question at once.
    /// </summary>
    [Fact]
    public void No_guard_intervened_anywhere_in_the_set()
    {
        var intervened = GoldenSet.Cases
            .Select(c => runner.Outcome(c.Id))
            .Where(outcome => outcome.GuardIntervened)
            .Select(outcome => $"{outcome.Case.Id} ({outcome.Case.Question})")
            .ToList();

        Assert.True(
            intervened.Count == 0,
            "R-EVD-03 requires zero grounding/numeric guard interventions across the golden set; " +
            $"{intervened.Count} turn(s) intervened:\n  " + string.Join("\n  ", intervened));
    }

    /// <summary>
    /// The intents every case declares come from the fixed R-ASK-02/R-ASK-03 vocabulary, and every
    /// intent named by the task is actually covered. Without this, a typo in a JSON file would
    /// silently create a phantom intent that looks like coverage in the report but tests nothing
    /// the requirement names.
    /// </summary>
    [Fact]
    public void Every_case_names_a_real_intent_and_a_real_kind_and_the_named_intents_are_covered()
    {
        foreach (var goldenCase in GoldenSet.Cases)
        {
            Assert.True(
                GoldenSet.KnownIntents.Contains(goldenCase.Intent),
                $"Case '{goldenCase.Id}' names intent '{goldenCase.Intent}', which is not one of the " +
                $"fixed R-ASK-02/R-ASK-03 labels: {string.Join(", ", GoldenSet.KnownIntents.Order(StringComparer.Ordinal))}.");

            Assert.True(
                GoldenSet.KnownKinds.Contains(goldenCase.ExpectedKind),
                $"Case '{goldenCase.Id}' expects kind '{goldenCase.ExpectedKind}', which is not one of " +
                "the four R-ASK-07 kinds.");

            Assert.True(
                TenantFixtureKeys.All.Contains(goldenCase.TenantFixture),
                $"Case '{goldenCase.Id}' names tenant fixture '{goldenCase.TenantFixture}', which does not exist.");
        }

        // The four reply shapes ADR-024 §6 defines must each be exercised somewhere in the set.
        foreach (var kind in GoldenSet.KnownKinds)
        {
            Assert.True(
                GoldenSet.Cases.Any(c => c.ExpectedKind == kind),
                $"No golden case expects the '{kind}' reply shape (R-ASK-07 defines four).");
        }

        var uncovered = GoldenSet.KnownIntents
            .Except(GoldenSet.Cases.Select(c => c.Intent), StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            uncovered.Count == 0,
            "The golden set must cover every gate label and planner intent; missing: " +
            string.Join(", ", uncovered));
    }

    /// <summary>
    /// A case may only tolerate a deviation whose id is documented in
    /// <see cref="GoldenSetKnownGaps"/> — otherwise a future edit could silently forgive any
    /// behaviour by inventing a gap id inside a JSON file.
    /// </summary>
    [Fact]
    public void Every_known_gap_is_a_documented_gap()
    {
        foreach (var goldenCase in GoldenSet.Cases.Where(c => c.KnownGap is not null))
        {
            var gap = goldenCase.KnownGap!;

            Assert.True(
                GoldenSetKnownGaps.All.Contains(gap.Id),
                $"Case '{goldenCase.Id}' declares the undocumented gap id '{gap.Id}'. Add it to " +
                "GoldenSetKnownGaps (with the requirement it misses) or remove the declaration.");

            Assert.True(
                GoldenSet.KnownKinds.Contains(gap.ObservedKind),
                $"Case '{goldenCase.Id}' records the observed kind '{gap.ObservedKind}', which is not " +
                "one of the four R-ASK-07 kinds.");
        }
    }

    /// <summary>
    /// The task's own deliverable: "emit a markdown report (`backend/tests/Contigo.AiEval/reports/
    /// last-run.md`, git-ignored) with per-case verdicts to help HITL on `demo`".
    /// </summary>
    [Fact]
    public void Run_emits_the_per_case_markdown_report()
    {
        Assert.True(File.Exists(runner.ReportPath), $"No report was written at '{runner.ReportPath}'.");

        var report = File.ReadAllText(runner.ReportPath);

        Assert.Contains("# Ask V2 — AI evaluation set", report, StringComparison.Ordinal);
        Assert.Contains("## Per-case verdicts", report, StringComparison.Ordinal);
        Assert.Contains("Guard interventions (R-EVD-03 requires 0)", report, StringComparison.Ordinal);

        foreach (var goldenCase in GoldenSet.Cases)
        {
            Assert.Contains(goldenCase.Id, report, StringComparison.Ordinal);
        }
    }
}
