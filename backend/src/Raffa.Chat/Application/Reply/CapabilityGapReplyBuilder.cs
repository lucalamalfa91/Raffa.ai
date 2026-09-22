using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Drafting;
using Raffa.Chat.Application.Gaps;
using Raffa.Chat.Application.Pack;

namespace Raffa.Chat.Application.Reply;

/// <summary>
/// Assembles the two reply shapes a <see cref="Domain.GateLabel.CapabilityGap"/> turn can take
/// (ADR-030 D1/D2): a <see cref="ReplyKind.Redirect"/> carrying the honest preface, the nearest
/// alternative's action and the feedback offer; or a <see cref="ReplyKind.Draft"/> carrying the
/// preface, the drafted email, the pack items it cites and the same offer. Like
/// <see cref="RedirectReplyBuilder"/>, this never resolves an action or retrieves anything itself —
/// the composition root hands it everything already resolved.
/// </summary>
public static class CapabilityGapReplyBuilder
{
    public static CopilotReply Redirect(
        CapabilityGap gap,
        string language,
        string markdown,
        IReadOnlyList<CopilotAction> actions,
        IReadOnlyList<string> followUps)
    {
        ArgumentNullException.ThrowIfNull(gap);
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(followUps);

        return new CopilotReply(
            ReplyKind.Redirect,
            markdown,
            [],
            actions,
            ReplyProvenance.NoModelCall([]),
            followUps,
            new ReplyPayload(
                Gap: new GapInfo(gap.Key, gap.Title(language), language),
                FeedbackOffer: CapabilityGapCopy.FeedbackOfferFor(gap, language)));
    }

    /// <summary>
    /// The draft turn. Citations are the pack items the email was written from
    /// (<see cref="DraftOutcome.UsedCitationKeys"/>, already proven by <c>Drafting.DraftGuard</c>
    /// to exist in <paramref name="pack"/>), numbered in that order; provenance carries the
    /// writer's model metadata when a model wrote the email, or the template's own version tag
    /// with no model id when the deterministic fallback did — an honest "no model call", the same
    /// convention <see cref="ReplyProvenance.NoModelCall"/> uses.
    /// </summary>
    public static CopilotReply Draft(
        CapabilityGap gap,
        string language,
        string markdown,
        DraftOutcome draft,
        IReadOnlyList<PackItem> pack,
        IReadOnlyList<CopilotAction> actions,
        IReadOnlyList<string> followUps)
    {
        ArgumentNullException.ThrowIfNull(gap);
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(followUps);

        var citations = CopilotReplyBuilder.BuildCitations(draft.UsedCitationKeys, pack);
        var sources = citations.Select(c => c.Corpus).Distinct(StringComparer.Ordinal).ToList();

        var provenance = draft.Source == DraftSource.Model && draft.Metadata is { } metadata
            ? new ReplyProvenance(sources, metadata.ModelId, metadata.PromptVersion, metadata.InputHash)
            : new ReplyProvenance(sources, null, DraftingAgents.TemplateVersion, null);

        return new CopilotReply(
            ReplyKind.Draft,
            markdown,
            citations,
            actions,
            provenance,
            followUps,
            new ReplyPayload(
                Gap: new GapInfo(gap.Key, gap.Title(language), language),
                Draft: new EmailDraft(draft.Subject, draft.Body),
                FeedbackOffer: CapabilityGapCopy.FeedbackOfferFor(gap, language)));
    }
}
