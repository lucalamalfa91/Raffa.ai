using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Interview;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Application.WebResearch;
using Raffa.Documents.Contracts.Application;
using Raffa.SharedKernel;

namespace Raffa.Api;

/// <summary>
/// The web-research half of <see cref="AskCopilotService"/> (ADR-030). Three gates before any
/// offer — the kill switch (<see cref="WebResearchOptions.Enabled"/>), the workspace Admin's
/// opt-in (<see cref="IWorkspaceWebResearchPolicy"/>) and the daily budget
/// (<see cref="IWebResearchBudget"/>) — plus the topic lexicon, so only a procurement question is
/// ever offered the web. A consent is asked per question (<see cref="WebConsentInterview"/>),
/// never remembered, and re-checked against the gates when it is spent. The search itself is
/// <see cref="WebResearchComposer"/>'s: this file hands it three strings and never a pack.
/// </summary>
internal sealed partial class AskCopilotService
{
    private const string AuditWebResearchAuthorizedAction = "chat.web_research_authorized";
    private const string AuditWebResearchDeclinedAction = "chat.web_research_declined";
    private const string AuditWebResearchedAction = "chat.web_researched";
    private const string AuditWebResearchRefusedAction = "chat.web_research_refused";
    private const string WebResearchAuditResourceId = "web-research";

    private enum WebGate
    {
        Open,
        KillSwitch,
        WorkspaceOptIn,
        Budget,
        NoTopic,

        /// <summary>ADR-032: a web-mode question with fewer than two searchable words.</summary>
        TooShort,
    }

    /// <summary>The offer the interpretation menu may carry, or <see langword="null"/> when any
    /// gate is closed or the question is not a procurement topic. Never audited by itself: the
    /// interview turn that carries it is.</summary>
    private async Task<WebResearchRequest?> BuildWebOfferAsync(
        TenantId tenantId, string question, CancellationToken cancellationToken)
    {
        var (gate, offer) = await CheckWebGatesAsync(tenantId, question, cancellationToken).ConfigureAwait(false);
        return gate == WebGate.Open ? offer : null;
    }

    private async Task<(WebGate Gate, WebResearchRequest? Offer)> CheckWebGatesAsync(
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
    private async Task<CopilotReply> BuildWebResearchEntryReplyAsync(
        TenantId tenantId,
        string question,
        PortfolioPage portfolio,
        RoutingContext routingContext,
        string actor,
        CancellationToken cancellationToken)
    {
        var (gate, offer) = await CheckWebGatesAsync(tenantId, question, cancellationToken).ConfigureAwait(false);
        if (gate == WebGate.Open && offer is not null)
        {
            return InterviewReplyBuilder.Interview(WebConsentInterview.Build(question, offer));
        }

        await WriteWebResearchAuditAsync(tenantId, AuditWebResearchRefusedAction, actor, $"gate={gate} stage=offer", cancellationToken)
            .ConfigureAwait(false);

        return BuildWebGateClosedReply(gate, question, portfolio, routingContext);
    }

    /// <summary>A consumed consent: re-check the gates, spend one budget call, run the research
    /// role, and wrap the guarded outcome as a reply whose actions come from the catalog.</summary>
    private async Task<(CopilotReply Reply, bool GuardIntervened)> RunWebResearchAsync(
        TenantId tenantId,
        string question,
        WebResearchRequest authorized,
        PortfolioPage portfolio,
        RoutingContext routingContext,
        string actor,
        CancellationToken cancellationToken)
    {
        var queryHash = ComputeHash(authorized.Query);

        // A consent authorises one question; the gates still decide at execution time.
        if (!webResearchOptions.Enabled)
        {
            await WriteWebResearchAuditAsync(tenantId, AuditWebResearchRefusedAction, actor, $"gate={WebGate.KillSwitch} stage=run queryHash={queryHash}", cancellationToken)
                .ConfigureAwait(false);
            return (BuildWebGateClosedReply(WebGate.KillSwitch, question, portfolio, routingContext), false);
        }

        if (webResearchOptions.RequireWorkspaceOptIn
            && !await workspaceWebResearchPolicy.IsEnabledAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            await WriteWebResearchAuditAsync(tenantId, AuditWebResearchRefusedAction, actor, $"gate={WebGate.WorkspaceOptIn} stage=run queryHash={queryHash}", cancellationToken)
                .ConfigureAwait(false);
            return (BuildWebGateClosedReply(WebGate.WorkspaceOptIn, question, portfolio, routingContext), false);
        }

        await WriteWebResearchAuditAsync(
                tenantId, AuditWebResearchAuthorizedAction, actor,
                $"queryHash={queryHash} purpose={authorized.Purpose} queryLength={authorized.Query.Length}", cancellationToken)
            .ConfigureAwait(false);

        if (!await webResearchBudget.TryConsumeAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            await WriteWebResearchAuditAsync(tenantId, AuditWebResearchRefusedAction, actor, $"gate={WebGate.Budget} stage=run queryHash={queryHash}", cancellationToken)
                .ConfigureAwait(false);
            return (BuildWebGateClosedReply(WebGate.Budget, question, portfolio, routingContext), false);
        }

        var language = LanguageHint.IsItalian(question) ? "it" : "en";
        var outcome = await webResearchComposer
            .ComposeAsync(authorized.Query, authorized.Purpose, language, cancellationToken)
            .ConfigureAwait(false);

        await WriteWebResearchAuditAsync(
                tenantId, AuditWebResearchedAction, actor,
                $"queryHash={queryHash} outcome={outcome.Kind} sourceCount={outcome.SourceCount} " +
                $"guardIntervened={outcome.GuardIntervened} promptVersion={outcome.Provenance.PromptVersion}",
                cancellationToken)
            .ConfigureAwait(false);

        // Actions only from the catalog (R-SYS-02): Quote check for a market question, plus the
        // contract's own 360 when one is in scope; a non-answer keeps the usual recovery step.
        var actions = outcome.Kind == WebResearchOutcomeKind.Answered
            ? capabilityRouting.ResolveActions([CapabilityIntent.Benchmark], routingContext)
            : ResolveAbstainRecoveryActions(portfolio, routingContext);

        return (
            new CopilotReply(outcome.AsReplyKind, outcome.Markdown, outcome.Citations, actions, outcome.Provenance, []),
            outcome.GuardIntervened);
    }

    private CopilotReply BuildWebGateClosedReply(
        WebGate gate, string question, PortfolioPage portfolio, RoutingContext routingContext)
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
            ResolveAbstainRecoveryActions(portfolio, routingContext),
            ReplyProvenance.NoModelCall([]),
            []);
    }

    /// <summary>The ADR-030 audit rows beside the per-turn one: hashes and counts, never the
    /// query text or a source URL (ADR-011).</summary>
    private Task WriteWebResearchAuditAsync(
        TenantId tenantId, string action, string actor, string detail, CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(
            new AuditEntry(tenantId, actor, action, AuditResourceType, WebResearchAuditResourceId, clock.UtcNow, detail),
            cancellationToken);
}
