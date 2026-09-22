using System.Text.RegularExpressions;
using Raffa.AiGateway.Contracts;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Pack;

namespace Raffa.Chat.Application.Reply;

/// <summary>
/// Assembles a <see cref="CopilotReply"/> from a guarded `answer`-role result (task
/// E13/F06/US01/T01, ask-engine; ADR-024 §6). The one place <see cref="ReplyCitation"/> rows are
/// ever constructed from a <see cref="PackItem"/> — every other builder in this namespace
/// (<see cref="RedirectReplyBuilder"/>) either cites nothing or cites a caller-supplied,
/// already-real citation built the same way.
/// </summary>
public static class CopilotReplyBuilder
{
    /// <summary>The abstain copy when there is no reason fit to show. Persona v2.4: never a bare
    /// "nothing supports an answer" — a way forward. (<c>Raffa.Api.AskCopilotService</c> answers
    /// every decline with <c>Answering.HelpfulFallbackAnswer</c>'s proposal for the question before
    /// it gets here; this is the last line for any other caller.)</summary>
    public const string DefaultAbstainReason =
        "Happy to help: name the supplier or the contract and tell me the goal (savings, a renewal, a " +
        "market comparison or a clause), and I'll build the answer from your validated contracts.";

    // An abstain reason is shown to the user as is (the web prints it verbatim), so one that talks
    // about the machinery — the pack, its keys, the guards, a requirement id — is replaced by
    // DefaultAbstainReason rather than shown (R-ASK-08 "never engineer chrome").
    private static readonly Regex EngineerChrome = new(
        @"\b(context\s+pack|the\s+pack|pack\s+items?|citation\s?keys?|action\s?keys?|canDetermine|guards?|R-[A-Z]{2,5}-\d+|Appendix\s+C)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Turns a guarded <see cref="AiAnswerResult"/> (already validated by
    /// <c>Guards.GroundingGuard</c>/<c>Guards.NumericGuard</c>, and downgraded by
    /// <c>Guards.RegenerateOnce</c> when it failed twice) into a <see cref="CopilotReply"/>.
    /// <see cref="ReplyKind.Answer"/> when <see cref="AiAnswerResult.CanDetermine"/> is
    /// <see langword="true"/>, <see cref="ReplyKind.Abstain"/> otherwise (R-ASK-07).
    /// </summary>
    /// <param name="guarded">The final, guard-approved (or guard-downgraded) result.</param>
    /// <param name="pack">The same pack the gateway was given — resolves every
    /// <see cref="AiAnswerResult.CitationKeys"/> entry back to its full citation card.</param>
    /// <param name="actions">Already-resolved actions for this turn's
    /// <see cref="AiAnswerResult.ActionKeys"/> (via <c>CapabilityRouting.ResolveActions</c> in the
    /// composition root — this builder never resolves an action key itself). Used only for
    /// <see cref="ReplyKind.Answer"/> — see <paramref name="recoveryActions"/> for the abstain
    /// reply's own actions.</param>
    /// <param name="recoveryActions">A non-empty, already-resolved recovery action for
    /// <see cref="ReplyKind.Abstain"/> (task E25/F05/US01/T01; ADR-024 "every abstain has a
    /// clickable next step"; parent story's council decision: "the server selects the action from
    /// the catalog that unblocks this failure"). Resolved by the composition root via
    /// <c>CapabilityRouting.ResolveActions</c> against deterministic routing facts — never from
    /// <see cref="AiAnswerResult.ActionKeys"/>, because an abstaining model has nothing grounded to
    /// suggest and AC-2 forbids a model-authored action regardless. Ignored when
    /// <see cref="AiAnswerResult.CanDetermine"/> is <see langword="true"/>.</param>
    /// <param name="abstainFollowUps">Next-step questions for an abstain whose result carries none of
    /// its own (<c>Answering.GroundedFallbackAnswer.SuggestedQuestions</c>, chosen by the composition
    /// root from the turn's intent) — so a user who hit a gap still has somewhere to go.
    /// <see langword="null"/> or empty for none; ignored for <see cref="ReplyKind.Answer"/>.</param>
    /// <exception cref="ArgumentNullException">Any required argument is <see langword="null"/>.</exception>
    public static CopilotReply FromGuardedResult(
        AiAnswerResult guarded,
        IReadOnlyList<PackItem> pack,
        IReadOnlyList<CopilotAction> actions,
        IReadOnlyList<CopilotAction> recoveryActions,
        IReadOnlyList<string>? abstainFollowUps = null)
    {
        ArgumentNullException.ThrowIfNull(guarded);
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(recoveryActions);

        if (!guarded.CanDetermine)
        {
            return new CopilotReply(
                ReplyKind.Abstain,
                UserFacingAbstainReason(guarded.AbstainReason),
                [],
                recoveryActions,
                new ReplyProvenance([], guarded.Metadata.ModelId, guarded.Metadata.PromptVersion, guarded.Metadata.InputHash),
                guarded.FollowUps is { Count: > 0 } modelFollowUps ? modelFollowUps : abstainFollowUps ?? []);
        }

        // A citation key the model wrote into its prose becomes an [n] marker (or goes), so the
        // reader never sees a lookup token -- see InlineCitationNormalizer's own doc comment.
        var (answerMarkdown, citationKeys) = InlineCitationNormalizer.Normalize(guarded.AnswerMarkdown, guarded.CitationKeys ?? [], pack);
        var citations = BuildCitations(citationKeys, pack);
        var sources = citations.Select(c => c.Corpus).Distinct(StringComparer.Ordinal).ToList();

        return new CopilotReply(
            ReplyKind.Answer,
            answerMarkdown,
            citations,
            actions,
            new ReplyProvenance(sources, guarded.Metadata.ModelId, guarded.Metadata.PromptVersion, guarded.Metadata.InputHash),
            guarded.FollowUps ?? []);
    }

    /// <summary>The model's (or the downgrade's) abstain reason when it reads as plain language for
    /// the user; <see cref="DefaultAbstainReason"/> when it is blank or talks about the machinery.</summary>
    public static string UserFacingAbstainReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) || EngineerChrome.IsMatch(reason) ? DefaultAbstainReason : reason.Trim();

    /// <summary>
    /// Resolves <paramref name="citationKeys"/> (already proven, by
    /// <c>Guards.GroundingGuard</c>, to exist in <paramref name="pack"/>) into
    /// <see cref="ReplyCitation"/> rows, numbered in the order the model returned them — the same
    /// order its inline <c>[n]</c> markers reference.
    /// </summary>
    /// <remarks>
    /// Task E28/F03/US01/T01 (NW-83; ADR-024 w19 cl. 17 "no citation without a pack source"):
    /// <see cref="ReplyCitation.DocumentId"/>/<see cref="ReplyCitation.ContractId"/> echo
    /// <see cref="PackItem.DocumentId"/>/<see cref="PackItem.ContractId"/> verbatim — the
    /// composition root (<c>Raffa.Api.AskCopilotService</c>) is the one place that resolves a real
    /// id, never this builder. Previously this stamped <see cref="PackItem.CitationKey"/> itself
    /// onto <c>DocumentId</c> for every tenant item (an internal lookup token, not a document id)
    /// and left <c>ContractId</c> always <see langword="null"/> — both stubs are gone.
    /// </remarks>
    public static IReadOnlyList<ReplyCitation> BuildCitations(
        IReadOnlyList<string> citationKeys, IReadOnlyList<PackItem> pack)
    {
        ArgumentNullException.ThrowIfNull(citationKeys);
        ArgumentNullException.ThrowIfNull(pack);

        var byKey = pack.ToDictionary(item => item.CitationKey, StringComparer.Ordinal);

        var citations = new List<ReplyCitation>();
        for (var i = 0; i < citationKeys.Count; i++)
        {
            // Defensive only: a caller that skipped GroundingGuard could hand this an unresolved
            // key. Silently dropped rather than throwing — one bad key must not break every other
            // (already-grounded) citation in the same reply.
            if (!byKey.TryGetValue(citationKeys[i], out var item))
            {
                continue;
            }

            citations.Add(new ReplyCitation(
                N: i + 1,
                Corpus: item.Corpus,
                Title: item.Title,
                Subtitle: item.Subtitle,
                Snippet: item.Snippet,
                DocumentId: item.DocumentId,
                ContractId: item.ContractId,
                Page: item.Page,
                Section: item.Section,
                PreviewUrl: item.PreviewUrl,
                Href: item.Href,
                RecordId: item.RecordId));
        }

        return citations;
    }
}
