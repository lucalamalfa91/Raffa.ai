using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Gate;
using Raffa.Chat.Application.Interview;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Application.WebResearch;
using Raffa.Chat.Domain;
using Raffa.Documents.Contracts.Application;
using Raffa.SharedKernel;

namespace Raffa.Api;

/// <summary>
/// The web-mode half of <see cref="AskCopilotService"/> (ADR-032): the composer's web-search
/// toggle. Switching it on is the user's consent for every question sent with it, so there is no
/// per-question consent dialog, and the procurement-only filters are lifted — the domain gate's
/// off-domain/legal/unknown-supplier redirects, the interview, and the four research purposes of
/// <see cref="WebResearchTopicLexicon"/>. One filter stays: a plainly personal or leisure question
/// (<see cref="WebModeLexicon"/>, then the open persona's own <c>offTopic</c>) is pointed at a search
/// engine or an AI search assistant. The operational gates stay too — the kill switch, the
/// workspace Admin's opt-in and the daily budget — as does the isolation: the web is searched with
/// the user's own words through <see cref="WebQuerySanitizer"/>, never with a pack.
///
/// <para>
/// A turn has two halves that never share a pack: the normal contracts-only pipeline
/// (<see cref="BuildInDomainReplyAsync"/>, interview suppressed, without the "search the web"
/// phrase) and one open-mode research call (<see cref="WebResearchComposer"/>). Each is grounded and
/// guarded on its own; <see cref="WebModeReplyBuilder.Combine"/> only places them side by side.
/// </para>
/// </summary>
internal sealed partial class AskCopilotService
{
    private const string WebModeAuditTag = "mode=toggle";

    /// <summary>A greeting or small talk this short ("ciao", "come stai?") is still answered as one
    /// with the toggle on; anything longer is a search.</summary>
    private const int WebModeSmallTalkMaxWords = 4;

    /// <summary>Whether this turn is web mode's: the toggle is on and the question is a search — not
    /// the feature tour, not a capability gap (an operation, not a question), not a bare greeting.
    /// An off-context question is always web mode's, so it gets the search-engine pointer.</summary>
    private static bool IsWebModeTurn(AskTurnHints hints, DomainGateResult gate, string question)
    {
        if (!hints.WebMode)
        {
            return false;
        }

        if (WebModeLexicon.IsOffContext(question))
        {
            return true;
        }

        return gate.Label switch
        {
            GateLabel.Capability or GateLabel.CapabilityGap => false,
            GateLabel.Greeting or GateLabel.OffDomain =>
                question.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > WebModeSmallTalkMaxWords,
            _ => true,
        };
    }

    private async Task<(CopilotReply Reply, bool GuardIntervened, bool FallbackUsed)> BuildWebModeReplyAsync(
        TenantId tenantId,
        string question,
        DomainGateResult gate,
        PortfolioPage portfolio,
        IReadOnlyDictionary<EntityId, string> supplierNames,
        IReadOnlyList<(string Role, string Markdown)> recentTurns,
        EntityId? scopeContractId,
        PortfolioListItem? scopedContractItem,
        string actor,
        CancellationToken cancellationToken)
    {
        // The one content filter left: zero retrieval, zero model calls, two outbound links.
        if (WebModeLexicon.IsOffContext(question))
        {
            await WriteWebResearchAuditAsync(tenantId, AuditWebResearchRefusedAction, actor, $"gate=OffContext stage=offer {WebModeAuditTag}", cancellationToken)
                .ConfigureAwait(false);
            return (WebModeReplyBuilder.OffContext(question), false, false);
        }

        var routingContext = new RoutingContext(
            portfolio.TotalCount,
            CapabilityCallerRole.Standard,
            scopedContractItem is null ? null : new EntityId(scopedContractItem.ContractId));

        var (webGate, query) = await CheckWebModeGatesAsync(tenantId, question, cancellationToken).ConfigureAwait(false);
        if (webGate != WebGate.Open || query is null)
        {
            await WriteWebResearchAuditAsync(tenantId, AuditWebResearchRefusedAction, actor, $"gate={webGate} stage=offer {WebModeAuditTag}", cancellationToken)
                .ConfigureAwait(false);
            return (BuildWebGateClosedReply(webGate, question, portfolio, routingContext), false, false);
        }

        // Raffa's own store: the normal pipeline, contracts-only by construction. Skipped for a
        // question the gate found off-domain — nothing in the store speaks to it. An unknown
        // supplier's name is not carried in: there is no contract of it to resolve.
        (CopilotReply Reply, bool GuardIntervened, bool FallbackUsed)? internalHalf = null;
        if (gate.Label != GateLabel.OffDomain)
        {
            internalHalf = await BuildInDomainReplyAsync(
                    tenantId,
                    WebConsentInterview.DeclinedRewrite(question),
                    gate.Label == GateLabel.NeedsDocument ? null : gate.NamedSupplier,
                    portfolio,
                    supplierNames,
                    recentTurns,
                    scopeContractId,
                    scopedContractItem,
                    actor,
                    AskTurnHints.None with { SuppressInterview = true },
                    previousRaffaTurnWasInterview: false,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var outcome = await RunWebModeResearchAsync(tenantId, question, query, actor, cancellationToken).ConfigureAwait(false);
        var italian = WebModeReplyBuilder.IsItalian(question);

        // Only an answer from the store is kept: its abstain, redirect or refusal would only repeat
        // the filters this mode lifts, and the web half speaks for the turn instead.
        if (internalHalf is { Reply.Kind: ReplyKind.Answer } both)
        {
            if (outcome is null)
            {
                return both;
            }

            var webActions = outcome.Kind == WebResearchOutcomeKind.Answered
                ? capabilityRouting.ResolveActions([CapabilityIntent.Benchmark], routingContext)
                : [];

            return (
                WebModeReplyBuilder.Combine(both.Reply, outcome, webActions, italian),
                both.GuardIntervened || outcome.GuardIntervened,
                both.FallbackUsed);
        }

        if (outcome is null)
        {
            return (BuildWebGateClosedReply(WebGate.Budget, question, portfolio, routingContext), false, false);
        }

        // The open persona found nothing work-related to research: the same pointer as the lexicon.
        if (outcome.Kind == WebResearchOutcomeKind.Refused)
        {
            return (WebModeReplyBuilder.OffContext(question), false, false);
        }

        var actions = outcome.Kind == WebResearchOutcomeKind.Answered
            ? capabilityRouting.ResolveActions([CapabilityIntent.Benchmark], routingContext)
            : ResolveAbstainRecoveryActions(portfolio, routingContext);

        return (
            new CopilotReply(outcome.AsReplyKind, outcome.Markdown, outcome.Citations, actions, outcome.Provenance, []),
            outcome.GuardIntervened,
            false);
    }

    /// <summary>The ADR-030 gates in the order a toggle turn meets them; the topic lexicon is not
    /// one of them. <see cref="WebGate.TooShort"/> when fewer than two searchable words survive the
    /// sanitiser.</summary>
    private async Task<(WebGate Gate, string? Query)> CheckWebModeGatesAsync(
        TenantId tenantId, string question, CancellationToken cancellationToken)
    {
        if (!webResearchOptions.Enabled)
        {
            return (WebGate.KillSwitch, null);
        }

        if (webResearchOptions.RequireWorkspaceOptIn
            && !await workspaceWebResearchPolicy.IsEnabledAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return (WebGate.WorkspaceOptIn, null);
        }

        if (!await webResearchBudget.HasRemainingAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return (WebGate.Budget, null);
        }

        var query = WebQuerySanitizer.Sanitize(question, webResearchOptions.MaxQueryChars);
        return query is null ? (WebGate.TooShort, null) : (WebGate.Open, query);
    }

    /// <summary>One budgeted research call with the open persona, audited like a consented one
    /// (hashes and counts only, ADR-011). <see langword="null"/> when the day's budget ran out
    /// between the gate check and the spend.</summary>
    private async Task<WebResearchOutcome?> RunWebModeResearchAsync(
        TenantId tenantId, string question, string query, string actor, CancellationToken cancellationToken)
    {
        var queryHash = ComputeHash(query);

        await WriteWebResearchAuditAsync(
                tenantId, AuditWebResearchAuthorizedAction, actor,
                $"queryHash={queryHash} purpose={WebModeLexicon.Purpose} queryLength={query.Length} {WebModeAuditTag}", cancellationToken)
            .ConfigureAwait(false);

        if (!await webResearchBudget.TryConsumeAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            await WriteWebResearchAuditAsync(tenantId, AuditWebResearchRefusedAction, actor, $"gate={WebGate.Budget} stage=run queryHash={queryHash} {WebModeAuditTag}", cancellationToken)
                .ConfigureAwait(false);
            return null;
        }

        var outcome = await webResearchComposer
            .ComposeAsync(query, WebModeLexicon.Purpose, WebModeReplyBuilder.IsItalian(question) ? "it" : "en", cancellationToken)
            .ConfigureAwait(false);

        await WriteWebResearchAuditAsync(
                tenantId, AuditWebResearchedAction, actor,
                $"queryHash={queryHash} outcome={outcome.Kind} sourceCount={outcome.SourceCount} " +
                $"guardIntervened={outcome.GuardIntervened} promptVersion={outcome.Provenance.PromptVersion} {WebModeAuditTag}",
                cancellationToken)
            .ConfigureAwait(false);

        return outcome;
    }
}
