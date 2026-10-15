using Raffa.AiFlows.Shared.ContractContext;
using static Raffa.AiFlows.Ask.Interview.InterviewInputsFactory;
using static Raffa.AiFlows.Ask.Packs.ContractResolution;
using static Raffa.AiFlows.Ask.Packs.PackItemFactory;
using Raffa.AiFlows.Shared.Audit;
using Raffa.AiFlows.CapabilityGaps.Investigation;
using Raffa.AiFlows.Negotiation.DataCheck;
using Raffa.AiFlows.WebResearch.Orchestration;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Raffa.AiFlows.Ask.Answering;
using Raffa.AiFlows.Ask.Gate;
using Raffa.AiFlows.Ask.Interview;
using Raffa.AiFlows.Ask.Planning;
using Raffa.AiFlows.Ask.Reply;
using Raffa.AiFlows.Ask.Routing;
using Raffa.AiFlows.CapabilityGaps.Drafting;
using Raffa.AiFlows.Negotiation.Orchestration;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Planning;
using Raffa.AiFlows.Shared.Reply;
using Raffa.AiFlows.Shared.Routing;
using Raffa.AiGateway.Telemetry;
using Raffa.Benchmark;
using Raffa.Benchmark.Contracts;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Interview;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Application.WebResearch;
using Raffa.Chat.Domain;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.Insights.Application;
using Raffa.Insights.Contracts;
using Raffa.Insights.Criticality;
using Raffa.Insights.Strategy;
using Raffa.Market.Contracts;
using Raffa.Market.Retrieval;
using Raffa.Renewals.Application;
using Raffa.Savings.Application;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;
using Raffa.Suppliers.Products.Application;
using Raffa.AiFlows.Ask.Packs;
using Raffa.AiFlows.Ask.Orchestration;

namespace Raffa.AiFlows.Ask.Packs;

/// <summary>
/// The notice pack's two typed items (the scoped fact and the optional matching clause). Moved
/// verbatim from <see cref="Raffa.AiFlows.Ask.Orchestration.AskCopilotService"/> (F1 Ask split, PR-2).
/// </summary>
internal static class NoticeFacts
{
    // The matching-clause evidence item's own vocabulary (task E30/F01/US01/T01): a notice question
    // is supported by whichever of this contract's own extracted clauses actually discusses when or
    // how notice must be given -- termination, cancellation, notice and auto-renewal clauses all
    // qualify; a generic "payment terms" or "liability" clause never does.
    private static readonly Regex NoticeClauseTypePattern = new(
        @"notice|cancellat|terminat|auto.?renew|renewal",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Task E30/F01/US01/T01: the notice pack's first item — the scoped fact itself
    /// (<c>endDate</c>/<c>cancellationDeadline</c>/<c>autoRenewal</c>/<c>renewalTermMonths</c>, task
    /// text verbatim), read straight off <paramref name="renewal"/> (<see cref="Contract360Renewal"/>,
    /// already tenant+contract scoped by <see cref="Contract360QueryService.GetByIdAsync"/>).
    /// <c>autoRenewal</c> itself carries no <see cref="PackValue"/> (no <see cref="PackValueKind"/>
    /// fits a boolean) — it decides which of the three honest snippets below applies instead, the
    /// same role it already plays in <see cref="BuildContractFactItem"/>'s own snippet. Every date is
    /// a <see cref="PackValueKind.Date"/> value (AC-2); <c>renewalTermMonths</c> is a bare
    /// <see cref="PackValueKind.Number"/>, never fabricated when the contract has none on file.
    /// </summary>
    internal static PackItem BuildNoticeFactItem(PortfolioListItem item, Contract360Renewal renewal, string displayName)
    {
        var values = new List<PackValue>();
        if (renewal.EndDate is { } endDate)
        {
            values.Add(new PackValue("endDate", endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PackValueKind.Date));
        }

        if (renewal.CancellationDeadline is { } deadline)
        {
            values.Add(new PackValue("cancellationDeadline", deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PackValueKind.Date));
        }

        if (renewal.RenewalTermMonths is { } termMonths)
        {
            values.Add(new PackValue("renewalTermMonths", termMonths.ToString(CultureInfo.InvariantCulture), PackValueKind.Number));
        }

        string snippet;
        if (!renewal.AutoRenewal)
        {
            // AC-3: autoRenewal=false -> no notice window, the contract just ends on endDate.
            snippet = renewal.EndDate is { } end
                ? $"{displayName} does not auto-renew: no notice window applies, the contract ends on {end:yyyy-MM-dd}."
                : $"{displayName} does not auto-renew, and Raffa has no end date on file yet (Appendix C rule 10).";
        }
        else if (renewal.CancellationDeadline is { } deadlineDate)
        {
            // AC-3's fixture shape: the date, then "if missed" only when a real renewalTermMonths is
            // on file -- never a fabricated "renews automatically" with no term to name.
            snippet = renewal.RenewalTermMonths is { } months
                ? $"{displayName}'s notice deadline is {deadlineDate:yyyy-MM-dd}. If missed, the " +
                  $"contract renews for {months} month(s)."
                : $"{displayName}'s notice deadline is {deadlineDate:yyyy-MM-dd}.";
        }
        else
        {
            snippet = $"{displayName} auto-renews, but Raffa has no validated notice deadline on " +
                "file yet (Appendix C rule 10).";
        }

        return new PackItem(
            $"fact:{item.ContractId}:notice",
            PackCorpus.Tenant,
            $"{displayName} · notice",
            null,
            null,
            null,
            snippet,
            $"/contracts/{item.ContractId}",
            null,
            null,
            "validated contract",
            values,
            item.ContractId.ToString());
    }

    /// <summary>
    /// Task E30/F01/US01/T01: the notice pack's optional third item — supporting evidence for the
    /// <c>cancellationDeadline</c> fact above, the first of this contract's own extracted
    /// <see cref="Contract360Clause"/> rows whose <see cref="Contract360Clause.ClauseType"/> or
    /// <see cref="Contract360Clause.RawText"/> names notice/cancellation/termination/auto-renewal
    /// (<see cref="NoticeClauseTypePattern"/>). <see langword="null"/> when none matches — "else
    /// abstain honestly" (task text): the fact and explanation items already ground the reply, so a
    /// missing evidence item narrows the citation, it never becomes a fabricated one. Reuses
    /// <see cref="ClausePackBuilder.ResolveTenantClauseLinks"/>'s own tier-1 viewer link, the identical resolution
    /// <see cref="ClausePackBuilder.BuildClausePackAsync"/> already gives a resolved <see cref="Contract360Clause"/>.
    /// </summary>
    internal static PackItem? BuildMatchingClauseItem(Contract360Result contract360, Guid namedContractId)
    {
        var clause = contract360.Clauses.FirstOrDefault(c =>
            NoticeClauseTypePattern.IsMatch(c.ClauseType) || NoticeClauseTypePattern.IsMatch(c.RawText));

        if (clause is null)
        {
            return null;
        }

        var (href, previewUrl) = ClausePackBuilder.ResolveTenantClauseLinks(clause, clause.ClauseId, namedContractId);
        var subtitle = clause.SourcePage is { } page
            ? $"p.{page}" + (clause.SourceSpan is { } span ? $" §{span}" : string.Empty)
            : null;

        return new PackItem(
            $"fact:{clause.ClauseId}:notice-clause",
            PackCorpus.Tenant,
            $"{clause.ClauseType} clause",
            subtitle,
            clause.SourcePage,
            clause.SourceSpan,
            clause.RawText,
            href,
            previewUrl,
            null,
            "validated contract",
            [],
            namedContractId.ToString(),
            clause.SourceDocumentId?.Value.ToString());
    }
}
