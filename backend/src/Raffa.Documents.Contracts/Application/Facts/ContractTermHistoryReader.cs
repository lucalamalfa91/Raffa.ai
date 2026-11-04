using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Documents.Contracts.Application.Facts;

/// <summary>
/// <see cref="IContractTermHistoryReader"/> over what the documents state: the contract's own term
/// (the validated facts as stored, so a fixture's curated dates and spend are the ones every screen
/// shows; the contract row fills what they lack), then every renewal recorded on it
/// (<c>contract_term_version</c>, current or superseded). Nothing is inferred: a contract without
/// both dates has no documented term, a term without a stated value keeps a null value.
/// </summary>
public sealed class ContractTermHistoryReader(DocumentsContractsDbContext dbContext, ITenantContext tenantContext)
    : IContractTermHistoryReader
{
    public async Task<IReadOnlyDictionary<Guid, ContractTermHistory>> ListAsync(
        TenantId tenantId, IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contractIds);
        if (contractIds.Count == 0)
        {
            return new Dictionary<Guid, ContractTermHistory>();
        }

        using var _ = tenantContext.BeginScope(tenantId);
        var ids = contractIds.Distinct().Select(id => new EntityId(id)).ToList();
        var contracts = await dbContext.Contracts.AsNoTracking()
            .Where(c => c.TenantId == tenantId && ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Currency, c.StartDate, c.EffectiveDate, c.EndDate, c.AnnualSpend, c.TotalContractValue, c.CreatedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var stored = await dbContext.ContractFacts.AsNoTracking()
            .Where(f => f.TenantId == tenantId && ids.Contains(f.ContractId))
            .Select(f => new { f.ContractId, f.Facts, f.ValidatedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var versions = await dbContext.ContractTermVersions.AsNoTracking()
            .Where(v => v.TenantId == tenantId && ids.Contains(v.ContractId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var lines = await dbContext.ContractLineItems.AsNoTracking()
            .Where(l => l.TenantId == tenantId && ids.Contains(l.ContractId))
            .Select(l => new { l.ContractId, l.BillingPeriod })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = new Dictionary<Guid, ContractTermHistory>();
        foreach (var c in contracts)
        {
            var row = stored.FirstOrDefault(f => f.ContractId == c.Id);
            var facts = row is null ? null : JsonSerializer.Deserialize<ContractFacts>(row.Facts, ContractFactsReader.Json);
            var currency = Code(c.Currency);
            var terms = new List<DocumentedContractTerm>();

            var from = facts?.StartDate ?? ContractTermCalculator.ParseDate(facts?.Start) ?? c.StartDate ?? c.EffectiveDate;
            var to = facts?.EndDate ?? ContractTermCalculator.ParseDate(facts?.End) ?? c.EndDate;
            if (from is { } f && to is { } t && t >= f)
            {
                var annual = facts is { Spend: > 0 } ? facts.Spend : c.AnnualSpend;
                terms.Add(new DocumentedContractTerm(
                    f, t, annual is > 0 ? annual : null, c.TotalContractValue is > 0 ? c.TotalContractValue : null,
                    currency, DocumentedTermSource.Contract, row?.ValidatedAt ?? c.CreatedAt));
            }

            terms.AddRange(versions
                .Where(v => v.ContractId == c.Id && v.ValidTo >= v.ValidFrom)
                .OrderBy(v => v.ValidFrom)
                .ThenBy(v => v.CreatedAt)
                .Select(v => new DocumentedContractTerm(
                    v.ValidFrom, v.ValidTo, v.AnnualValue is > 0 ? v.AnnualValue : null, null,
                    string.IsNullOrWhiteSpace(v.Currency) ? currency : Code(v.Currency), DocumentedTermSource.Renewal, v.CreatedAt)));

            result[c.Id.Value] = new ContractTermHistory(
                c.Id.Value,
                terms.OrderBy(x => x.From).ThenBy(x => x.RecordedAt).ToList(),
                BillingPeriods.Common(lines.Where(l => l.ContractId == c.Id).Select(l => l.BillingPeriod)));
        }

        return result;
    }

    private static string Code(string currency) => currency.Trim().ToUpperInvariant();
}
