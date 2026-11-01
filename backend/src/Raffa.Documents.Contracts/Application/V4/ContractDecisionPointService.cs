using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Documents.Contracts.Application.V4;

/// <summary>The user's choice on a decision point.</summary>
public static class PointDecisions
{
    public const string Negotiate = "negotiate";
    public const string Accept = "accept";
    public const string Clarify = "clarify";

    public static bool IsKnown(string? value) => value is Negotiate or Accept or Clarify;
}

/// <summary>
/// One "Cosa richiede una decisione" card (AskRaffa V4.dc.html, Contratto). <see cref="Tag"/> is
/// <c>discuss | risk | sla</c> (*Da discutere / Rischio / SLA*); <see cref="Says"/>,
/// <see cref="Title"/> and <see cref="Proposal"/> are the contract's own text; <see cref="Why"/> is
/// the contract's risk text or a <see cref="ContractMessage"/>.
/// </summary>
public sealed record DecisionPointView(
    string Key,
    string Tag,
    ContractMessage Kind,
    string Title,
    string Says,
    object Why,
    string Proposal,
    ContractMessage Source,
    ContractMessage? PolicyNote,
    string? Decision,
    string? DecidedBy,
    DateTimeOffset? DecidedAt);

/// <summary>
/// The ≤3 decision points of a contract and the user's recorded choices
/// (<c>contract_point_decision</c>). Points come from the contract facts exactly as the
/// prototype derives them: every "to discuss" clause (<c>DEEP.push</c>), then the first liability /
/// service-credit clause to raise, capped at three.
/// </summary>
public sealed class ContractDecisionPointService(
    DocumentsContractsDbContext dbContext, ITenantContext tenantContext, IContractFactsReader factsReader, IClock clock)
{
    public const int MaxPoints = 3;

    private static readonly Regex RaiseFilter = new("liab|credit", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The points of <paramref name="contractId"/>, or null when it has no facts.</summary>
    public async Task<IReadOnlyList<DecisionPointView>?> ListAsync(
        TenantId tenantId, EntityId contractId, CancellationToken cancellationToken = default)
    {
        var facts = await factsReader.GetAsync(tenantId, contractId, cancellationToken).ConfigureAwait(false);
        if (facts is null)
        {
            return null;
        }

        var decisions = await LoadDecisionsAsync(tenantId, contractId, cancellationToken).ConfigureAwait(false);
        return Build(facts, decisions);
    }

    /// <summary>Records (or clears, when <paramref name="decision"/> is null) the choice on one point.
    /// Null when the contract has no facts; a failure when the point or the decision is unknown.</summary>
    public async Task<Result<IReadOnlyList<DecisionPointView>>?> SetAsync(
        TenantId tenantId,
        EntityId contractId,
        string pointKey,
        string? decision,
        string decidedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decidedBy);
        if (decision is not null && !PointDecisions.IsKnown(decision))
        {
            return Result<IReadOnlyList<DecisionPointView>>.Failure("decision must be negotiate, accept, clarify or null.");
        }

        var facts = await factsReader.GetAsync(tenantId, contractId, cancellationToken).ConfigureAwait(false);
        if (facts is null)
        {
            return null;
        }

        if (!Build(facts, new Dictionary<string, ContractPointDecision>()).Any(p => p.Key == pointKey))
        {
            return Result<IReadOnlyList<DecisionPointView>>.Failure($"Unknown decision point '{pointKey}'.");
        }

        using (tenantContext.BeginScope(tenantId))
        {
            var row = await dbContext.ContractPointDecisions
                .SingleOrDefaultAsync(
                    d => d.TenantId == tenantId && d.ContractId == contractId && d.PointKey == pointKey, cancellationToken)
                .ConfigureAwait(false);
            if (decision is null)
            {
                if (row is not null)
                {
                    dbContext.ContractPointDecisions.Remove(row);
                }
            }
            else if (row is null)
            {
                dbContext.ContractPointDecisions.Add(new ContractPointDecision
                {
                    TenantId = tenantId,
                    ContractId = contractId,
                    PointKey = pointKey,
                    Decision = decision,
                    DecidedBy = decidedBy,
                    DecidedAt = clock.UtcNow,
                });
            }
            else
            {
                row.Decision = decision;
                row.DecidedBy = decidedBy;
                row.DecidedAt = clock.UtcNow;
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var decisions = await LoadDecisionsAsync(tenantId, contractId, cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<DecisionPointView>>.Success(Build(facts, decisions));
    }

    /// <summary>The prototype's derivation (<c>decs</c> in <c>renderVals</c>), pure.</summary>
    public static IReadOnlyList<DecisionPointView> Build(
        ContractFacts facts, IReadOnlyDictionary<string, ContractPointDecision> decisions)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(decisions);

        var push = facts.Deep.Push.Select(p => View(facts, p, "discuss", decisions, policy: null));
        var raise = facts.Deep.Raise
            .Where(p => RaiseFilter.IsMatch(p.Topic))
            .Take(1)
            .Select(p =>
            {
                var sla = p.Topic.Contains("credit", StringComparison.OrdinalIgnoreCase);
                var policy = p.Topic.Contains("liab", StringComparison.OrdinalIgnoreCase)
                    ? ContractMessage.Of("Policy: ≥ 24 months (binding)")
                    : null;
                return View(facts, p, sla ? "sla" : "risk", decisions, policy);
            });
        return push.Concat(raise).Take(MaxPoints).ToList();
    }

    /// <summary>"Price increase" → "price-increase".</summary>
    public static string KeyOf(string topic)
    {
        var builder = new StringBuilder(topic.Length);
        foreach (var ch in topic.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().TrimEnd('-');
    }

    private static DecisionPointView View(
        ContractFacts facts,
        ContractClausePoint point,
        string tag,
        IReadOnlyDictionary<string, ContractPointDecision> decisions,
        ContractMessage? policy)
    {
        var key = KeyOf(point.Topic);
        decisions.TryGetValue(key, out var decision);
        var kind = tag switch
        {
            "discuss" => ContractMessage.Of("To discuss"),
            "sla" => ContractMessage.Of("SLA"),
            _ => ContractMessage.Of("Risk"),
        };
        return new DecisionPointView(
            key,
            tag,
            kind,
            point.Topic,
            point.Says,
            WhyItMatters(facts, point.Topic),
            point.Proposal,
            ContractMessage.Of("Contract · {source}", ("source", point.Source)),
            policy,
            decision?.Decision,
            decision?.DecidedBy,
            decision?.DecidedAt);
    }

    /// <summary>The contract's own risk whose title or detail mentions the topic's first word, else
    /// the generic "costs money or freedom" line (prototype <c>risk(t)</c>).</summary>
    private static object WhyItMatters(ContractFacts facts, string topic)
    {
        var first = topic.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? topic;
        var pattern = new Regex(Regex.Escape(first), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var risk = facts.Deep.Risks.FirstOrDefault(r => pattern.IsMatch(r.Title + " " + r.Detail));
        return risk is not null
            ? risk.Detail
            : ContractMessage.Of("Costs money or freedom at renewal if left as is.");
    }

    private async Task<Dictionary<string, ContractPointDecision>> LoadDecisionsAsync(
        TenantId tenantId, EntityId contractId, CancellationToken cancellationToken)
    {
        using var _ = tenantContext.BeginScope(tenantId);
        var rows = await dbContext.ContractPointDecisions.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.ContractId == contractId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(r => r.PointKey, StringComparer.Ordinal);
    }
}
