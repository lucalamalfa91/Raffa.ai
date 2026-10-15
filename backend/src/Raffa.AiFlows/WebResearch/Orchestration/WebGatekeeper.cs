using Microsoft.Extensions.Options;
using Raffa.AiFlows.WebResearch.Configuration;
using Raffa.AiFlows.WebResearch.Lexicons;
using Raffa.AiFlows.WebResearch.Ports;
using Raffa.AiFlows.WebResearch.Query;
using Raffa.Chat.Application.Interview;
using Raffa.Chat.Application.WebResearch;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.WebResearch.Orchestration;

/// <summary>
/// F3-T05 — the one place the web gates are decided. Both entries keep their own shape (the per-question
/// consent of ADR-030 and the composer toggle of ADR-032) and their own order, which differ on purpose:
/// the consented entry judges the topic before the workspace opt-in and the budget, the toggle entry
/// judges the opt-in and the budget before the query is long enough. What was written twice, in
/// <see cref="WebResearchFlow"/> and <see cref="WebModeFlow"/>, is here once: the kill switch, the
/// workspace policy and the budget are read from the same three collaborators, in one class. The options
/// are read through the monitor on every call, so the kill switch applies without a restart (F3-T10).
/// </summary>
public sealed class WebGatekeeper(
    IOptionsMonitor<WebResearchOptions> optionsMonitor,
    IWorkspaceWebResearchPolicy workspacePolicy,
    IWebResearchBudget budget)
{
    /// <summary>The consented entry (ADR-030): kill switch, topic and query, workspace opt-in, budget.</summary>
    public async Task<(WebGate Gate, WebResearchRequest? Offer)> CheckConsentedAsync(
        TenantId tenantId, string question, CancellationToken cancellationToken)
    {
        var options = optionsMonitor.CurrentValue;
        if (!options.Enabled)
        {
            return (WebGate.KillSwitch, null);
        }

        var purpose = WebResearchTopicLexicon.Classify(question);
        var query = WebQuerySanitizer.Sanitize(question, options.MaxQueryChars, options.RedactSensitiveQueryPatterns);
        if (purpose is null || query is null)
        {
            return (WebGate.NoTopic, null);
        }

        if (!await WorkspaceAllowsAsync(options, tenantId, cancellationToken).ConfigureAwait(false))
        {
            return (WebGate.WorkspaceOptIn, null);
        }

        if (!await budget.HasRemainingAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return (WebGate.Budget, null);
        }

        return (WebGate.Open, new WebResearchRequest(query, purpose));
    }

    /// <summary>The toggle entry (ADR-032): kill switch, workspace opt-in, budget, then the query
    /// (<see cref="WebGate.TooShort"/> when fewer than two searchable words survive the sanitiser).</summary>
    public async Task<(WebGate Gate, string? Query)> CheckToggleAsync(
        TenantId tenantId, string question, CancellationToken cancellationToken)
    {
        var options = optionsMonitor.CurrentValue;
        if (!options.Enabled)
        {
            return (WebGate.KillSwitch, null);
        }

        if (!await WorkspaceAllowsAsync(options, tenantId, cancellationToken).ConfigureAwait(false))
        {
            return (WebGate.WorkspaceOptIn, null);
        }

        if (!await budget.HasRemainingAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return (WebGate.Budget, null);
        }

        var query = WebQuerySanitizer.Sanitize(question, options.MaxQueryChars, options.RedactSensitiveQueryPatterns);
        return query is null ? (WebGate.TooShort, null) : (WebGate.Open, query);
    }

    /// <summary>A consent authorises one question; the gates that can change between the offer and the
    /// spend are re-checked when it is spent: the kill switch, then the workspace opt-in. The budget is
    /// not here: spending it is the reservation itself.</summary>
    public async Task<WebGate> RecheckBeforeRunAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        var options = optionsMonitor.CurrentValue;
        if (!options.Enabled)
        {
            return WebGate.KillSwitch;
        }

        return await WorkspaceAllowsAsync(options, tenantId, cancellationToken).ConfigureAwait(false)
            ? WebGate.Open
            : WebGate.WorkspaceOptIn;
    }

    private async Task<bool> WorkspaceAllowsAsync(WebResearchOptions options, TenantId tenantId, CancellationToken cancellationToken) =>
        !options.RequireWorkspaceOptIn
        || await workspacePolicy.IsEnabledAsync(tenantId, cancellationToken).ConfigureAwait(false);
}
