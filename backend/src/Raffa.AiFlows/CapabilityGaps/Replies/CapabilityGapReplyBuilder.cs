using Raffa.AiFlows.CapabilityGaps.Agents;
using Raffa.AiFlows.CapabilityGaps.Drafting;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Gaps;
using Raffa.Chat.Application.Pack;
using Raffa.Chat.Application.Reply;

namespace Raffa.AiFlows.CapabilityGaps.Replies;

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
                Gap: new GapInfo(gap.Key, gap.Title(language), language, gap.Discovery),
                FeedbackOffer: CapabilityGapCopy.FeedbackOfferFor(gap, language)));
    }

    /// <summary>How many validated suppliers the "which contract?" reply offers as follow-ups.</summary>
    public const int MaxSupplierFollowUps = 5;

    /// <summary>
    /// The draft alternative with no contract to draft for: "which contract?", one follow-up chip per
    /// supplier on file (each re-enters this same gap with the name resolved), Portfolio as the
    /// action - or the Documents upload (<see cref="CapabilityIntent.UnknownSupplier"/>) when nothing
    /// is on file. The one component behind both the in-turn reply
    /// (<c>Raffa.Api.AskCopilotService</c>) and the investigator's follow-up message
    /// (<c>Raffa.Api.CapabilityCheckRunner</c>), which differ only in what they hand it.
    /// </summary>
    /// <param name="opening">Optional lead-in sentence put before the question (the follow-up
    /// message's "I checked what Raffa.ai can do..."); <see langword="null"/> for the in-turn reply.</param>
    /// <param name="unknownSupplier">A typed name that resolved to no contract of this tenant, named
    /// back in the question; <see langword="null"/> when there is none.</param>
    /// <param name="supplierNames">Candidate supplier names, in any order: blank ones are dropped,
    /// the rest de-duplicated (case-insensitively), sorted by name and capped at
    /// <see cref="MaxSupplierFollowUps"/>.</param>
    public static CopilotReply WhichContract(
        CapabilityGap gap,
        string language,
        string? opening,
        string? unknownSupplier,
        bool portfolioIsEmpty,
        IEnumerable<string?> supplierNames,
        CapabilityRouting routing,
        RoutingContext context)
    {
        ArgumentNullException.ThrowIfNull(gap);
        ArgumentNullException.ThrowIfNull(supplierNames);
        ArgumentNullException.ThrowIfNull(routing);
        ArgumentNullException.ThrowIfNull(context);

        var question = CapabilityGapCopy.AskWhichContract(gap, language, unknownSupplier, portfolioIsEmpty);
        var markdown = string.IsNullOrEmpty(opening) ? question : opening + " " + question;

        var actions = portfolioIsEmpty
            ? routing.ResolveActions([CapabilityIntent.UnknownSupplier], context)
            : routing.ResolveActions([CapabilityIntent.HowTo(CapabilityCatalog.PortfolioKey)], context);

        var followUps = supplierNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSupplierFollowUps)
            .Select(name => CapabilityGapCopy.DraftFollowUp(language, name))
            .ToList();

        return Redirect(gap, language, markdown, actions, followUps);
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
                Gap: new GapInfo(gap.Key, gap.Title(language), language, gap.Discovery),
                Draft: new EmailDraft(draft.Subject, draft.Body),
                FeedbackOffer: CapabilityGapCopy.FeedbackOfferFor(gap, language)));
    }
}
