using Raffa.AiGateway.Telemetry;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Interview;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Application.WebResearch;
using Raffa.Documents.Contracts.Application;
using Raffa.SharedKernel;

namespace Raffa.Api;

/// <summary>Why a web-research turn was (or was not) let through: the gates in the order they are
/// met. <see cref="Open"/> is the only value that ever reaches the search.</summary>
internal enum WebGate
{
    Open,
    KillSwitch,
    WorkspaceOptIn,
    Budget,
    NoTopic,

    /// <summary>ADR-032: a web-mode question with fewer than two searchable words.</summary>
    TooShort,
}

/// <summary>Resolves the recovery actions of a non-answer for the current portfolio and routing
/// context — the orchestrator's own <c>AskCopilotService.ResolveAbstainRecoveryActions</c>, handed to
/// the web flows as a delegate so they never reference the orchestrator.</summary>
internal delegate IReadOnlyList<CopilotAction> AbstainRecoveryResolver(
    PortfolioPage portfolio, RoutingContext routingContext);

/// <summary>
/// The web-research half of Ask (ADR-030). Three gates before any
/// offer — the kill switch (<see cref="WebResearchOptions.Enabled"/>), the workspace Admin's
/// opt-in (<see cref="IWorkspaceWebResearchPolicy"/>) and the daily budget
/// (<see cref="IWebResearchBudget"/>) — plus the topic lexicon, so only a procurement question is
/// ever offered the web. A consent is asked per question (<see cref="WebConsentInterview"/>),
/// never remembered, and re-checked against the gates when it is spent. The search itself is
/// <see cref="WebResearchComposer"/>'s: this class hands it three strings and never a pack.
/// Rewritten from the former <c>AskCopilotService.WebResearch.cs</c> partial, bodies unchanged: the
/// order of the gates, the budget reservation and release, and every audit row are as they were.
/// </summary>
internal sealed class WebResearchFlow(
    WebResearchOptions webResearchOptions,
    WebResearchComposer webResearchComposer,
    IWebResearchBudget webResearchBudget,
    IWorkspaceWebResearchPolicy workspaceWebResearchPolicy,
    IAuditWriter auditWriter,
    IClock clock,
    CapabilityRouting capabilityRouting)
{
    internal const string AuditAuthorizedAction = "chat.web_research_authorized";
    internal const string AuditDeclinedAction = "chat.web_research_declined";
    internal const string AuditResearchedAction = "chat.web_researched";
    internal const string AuditRefusedAction = "chat.web_research_refused";
    private const string WebResearchAuditResourceId = "web-research";

    /// <summary>The offer the interpretation menu may carry, or <see langword="null"/> when any
    /// gate is closed or the question is not a procurement topic. Never audited by itself: the
    /// interview turn that carries it is.</summary>
    public async Task<WebResearchRequest?> BuildOfferAsync(
        TenantId tenantId, string question, CancellationToken cancellationToken)
    {
        var (gate, offer) = await CheckGatesAsync(tenantId, question, cancellationToken).ConfigureAwait(false);
        return gate == WebGate.Open ? offer : null;
    }

    private async Task<(WebGate Gate, WebResearchRequest? Offer)> CheckGatesAsync(
        TenantId tenantId, string question, CancellationToken cancellationToken)
    {
        if (!webResearchOptions.Enabled)
        {
            return (WebGate.KillSwitch, null);
        }

        var purpose = WebResearchTopicLexicon.Classify(question);
        var query = WebQuerySanitizer.Sanitize(question, webResearchOptions.MaxQueryChars);
        if (purpose is null || query is null)
        {
            return (WebGate.NoTopic, null);
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

        return (WebGate.Open, new WebResearchRequest(query, purpose));
    }

    /// <summary>An explicit or forced <see cref="Raffa.Chat.Domain.AskIntent.WebResearch"/> turn:
    /// the consent question when every gate is open, otherwise a redirect that names the closed
    /// gate (never a silent fall-through, never a search).</summary>
    public async Task<CopilotReply> BuildEntryReplyAsync(
        TenantId tenantId,
        string question,
        PortfolioPage portfolio,
        RoutingContext routingContext,
        string actor,
        AbstainRecoveryResolver resolveAbstainRecoveryActions,
        CancellationToken cancellationToken)
    {
        var (gate, offer) = await CheckGatesAsync(tenantId, question, cancellationToken).ConfigureAwait(false);
        if (gate == WebGate.Open && offer is not null)
        {
            return InterviewReplyBuilder.Interview(WebConsentInterview.Build(question, offer));
        }

        await WriteAuditAsync(tenantId, AuditRefusedAction, actor, $"gate={gate} stage=offer", cancellationToken)
            .ConfigureAwait(false);

        return BuildGateClosedReply(gate, question, portfolio, routingContext, resolveAbstainRecoveryActions);
    }

    /// <summary>A consumed consent: re-check the gates, spend one budget call, run the research
    /// role, and wrap the guarded outcome as a reply whose actions come from the catalog.</summary>
    public async Task<(CopilotReply Reply, bool GuardIntervened)> RunAsync(
        TenantId tenantId,
        string question,
        WebResearchRequest authorized,
        PortfolioPage portfolio,
        RoutingContext routingContext,
        string actor,
        AbstainRecoveryResolver resolveAbstainRecoveryActions,
        CancellationToken cancellationToken)
    {
        var queryHash = QueryHasher.ComputeHash(authorized.Query);

        // A consent authorises one question; the gates still decide at execution time.
        if (!webResearchOptions.Enabled)
        {
            await WriteAuditAsync(tenantId, AuditRefusedAction, actor, $"gate={WebGate.KillSwitch} stage=run queryHash={queryHash}", cancellationToken)
                .ConfigureAwait(false);
            return (BuildGateClosedReply(WebGate.KillSwitch, question, portfolio, routingContext, resolveAbstainRecoveryActions), false);
        }

        if (webResearchOptions.RequireWorkspaceOptIn
            && !await workspaceWebResearchPolicy.IsEnabledAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            await WriteAuditAsync(tenantId, AuditRefusedAction, actor, $"gate={WebGate.WorkspaceOptIn} stage=run queryHash={queryHash}", cancellationToken)
                .ConfigureAwait(false);
            return (BuildGateClosedReply(WebGate.WorkspaceOptIn, question, portfolio, routingContext, resolveAbstainRecoveryActions), false);
        }

        await WriteAuditAsync(
                tenantId, AuditAuthorizedAction, actor,
                $"queryHash={queryHash} purpose={authorized.Purpose} queryLength={authorized.Query.Length}", cancellationToken)
            .ConfigureAwait(false);

        var reservation = await webResearchBudget.TryReserveAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (reservation is not { } reserved)
        {
            await WriteAuditAsync(tenantId, AuditRefusedAction, actor, $"gate={WebGate.Budget} stage=run queryHash={queryHash}", cancellationToken)
                .ConfigureAwait(false);
            return (BuildGateClosedReply(WebGate.Budget, question, portfolio, routingContext, resolveAbstainRecoveryActions), false);
        }

        var language = LanguageHint.IsItalian(question) ? "it" : "en";
        var (outcome, released) = await ComposeReservedAsync(reserved, authorized.Query, authorized.Purpose, language, cancellationToken)
            .ConfigureAwait(false);

        await WriteAuditAsync(
                tenantId, AuditResearchedAction, actor,
                $"queryHash={queryHash} outcome={outcome.Kind} sourceCount={outcome.SourceCount} " +
                $"guardIntervened={outcome.GuardIntervened} promptVersion={outcome.Provenance.PromptVersion} " +
                $"budgetReleased={released}",
                cancellationToken)
            .ConfigureAwait(false);

        // Actions only from the catalog (R-SYS-02): Quote check for a market question, plus the
        // contract's own 360 when one is in scope; a non-answer keeps the usual recovery step.
        var actions = outcome.Kind == WebResearchOutcomeKind.Answered
            ? capabilityRouting.ResolveActions([CapabilityIntent.Benchmark], routingContext)
            : resolveAbstainRecoveryActions(portfolio, routingContext);

        return (
            new CopilotReply(outcome.AsReplyKind, outcome.Markdown, outcome.Citations, actions, outcome.Provenance, []),
            outcome.GuardIntervened);
    }

    /// <summary>
    /// F3-T03: the research call under a budget reservation taken before it. When the call fails for
    /// transport or configuration reasons (<see cref="WebResearchOutcome.ReleaseBudget"/> — never a
    /// content-filter verdict, never unusable output), or throws, the reserved unit is handed back, so
    /// an outage never costs the tenant a call. A cancelled caller keeps the unit spent: the provider
    /// may well have run the search. Shared by the consented and the toggle entry. The release is
    /// best effort (never cancelled with the request, never fatal to the reply).
    /// </summary>
    internal async Task<(WebResearchOutcome Outcome, bool Released)> ComposeReservedAsync(
        WebResearchReservation reservation,
        string query,
        string purpose,
        string language,
        CancellationToken cancellationToken)
    {
        WebResearchOutcome outcome;
        try
        {
            outcome = await webResearchComposer
                .ComposeAsync(query, purpose, language, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await TryReleaseBudgetAsync(reservation).ConfigureAwait(false);
            throw;
        }

        var released = outcome.ReleaseBudget && await TryReleaseBudgetAsync(reservation).ConfigureAwait(false);
        return (outcome, released);
    }

    private async Task<bool> TryReleaseBudgetAsync(WebResearchReservation reservation)
    {
        try
        {
            await webResearchBudget.ReleaseAsync(reservation, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            // The reply matters more than the refund; a failed release leaves the unit spent (the
            // conservative side of the budget) and the audit row says budgetReleased=False.
            return false;
        }
    }

    internal CopilotReply BuildGateClosedReply(
        WebGate gate,
        string question,
        PortfolioPage portfolio,
        RoutingContext routingContext,
        AbstainRecoveryResolver resolveAbstainRecoveryActions)
    {
        var italian = LanguageHint.IsItalian(question);

        var markdown = gate switch
        {
            WebGate.Budget => italian
                ? "Questo workspace ha esaurito le ricerche web di oggi. Riprova domani, oppure chiedimi dei tuoi contratti."
                : "This workspace has used today's web research allowance. Try again tomorrow, or ask me about your contracts.",
            WebGate.TooShort => italian
                ? "Per cercare sul web mi servono un paio di parole in più, ad esempio «aumenti di prezzo Salesforce» oppure «nuove regole UE per i fornitori cloud»."
                : "To search the web I need a few more words, for example “Salesforce price increases” or “new EU rules for cloud suppliers”.",
            WebGate.NoTopic => italian
                ? "L'agente di ricerca web copre solo temi procurement: pratiche di mercato, notizie sui fornitori, range pubblici, tattiche di negoziazione. Chiedimi uno di questi, oppure dei tuoi contratti."
                : "The web research agent only covers procurement topics: market practice, supplier news, public benchmark ranges, negotiation tactics. Ask about one of those, or about your contracts.",
            _ => italian
                ? "La ricerca sul web è disattivata per questo workspace. Chiedi al tuo Admin di attivarla in Workspace & members; intanto posso rispondere con i tuoi contratti."
                : "Web research is switched off for this workspace. Ask your Admin to enable it under Workspace & members; meanwhile I can answer from your contracts.",
        };

        return new CopilotReply(
            ReplyKind.Redirect,
            markdown,
            [],
            resolveAbstainRecoveryActions(portfolio, routingContext),
            ReplyProvenance.NoModelCall([]),
            []);
    }

    /// <summary>The audit row of a declined consent, written beside the turn it became (the
    /// contracts-only answer), so "asked, said no" is visible without the query ever being logged.</summary>
    public Task WriteDeclinedAuditAsync(TenantId tenantId, string actor, CancellationToken cancellationToken) =>
        WriteAuditAsync(tenantId, AuditDeclinedAction, actor, "outcome=declined", cancellationToken);

    /// <summary>The ADR-030 audit rows beside the per-turn one: hashes and counts, never the
    /// query text or a source URL (ADR-011).</summary>
    internal Task WriteAuditAsync(
        TenantId tenantId, string action, string actor, string detail, CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(
            new AuditEntry(
                tenantId, actor, action, AskAuditResourceType.Value, WebResearchAuditResourceId, clock.UtcNow,
                $"{detail} turnId={RunContext.Current?.TurnId ?? "none"}"),
            cancellationToken);
}
