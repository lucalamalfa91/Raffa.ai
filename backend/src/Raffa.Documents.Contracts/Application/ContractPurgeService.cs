using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// Removes tenant-owned contracts and the intra-module rows that would otherwise Restrict or orphan
/// the delete (parent pointers, negotiation ticks, polymorphic correction history). Facts that
/// cascade from <c>Contract</c> (clauses, obligations, risks, line items, versions, evidence) are
/// left to EF. Callers must have already removed documents that still Restrict on
/// <c>Document.ContractId</c> — <see cref="DocumentDeleteService"/> does that for a single file;
/// bulk document purge does it for the tenant before calling here.
/// </summary>
public sealed class ContractPurgeService(DocumentsContractsDbContext dbContext, ITenantContext tenantContext)
{
    public async Task<int> PurgeAsync(
        TenantId tenantId,
        IReadOnlyCollection<EntityId> contractIds,
        CancellationToken cancellationToken = default)
    {
        if (contractIds.Count == 0)
        {
            return 0;
        }

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var ids = contractIds.ToHashSet();
        var nullableIds = ids.Select(id => (EntityId?)id).ToList();
        var extra = await dbContext.Contracts
            .Where(c => c.TenantId == tenantId && c.ParentContractId != null && nullableIds.Contains(c.ParentContractId))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        while (extra.Count > 0)
        {
            var added = 0;
            foreach (var id in extra)
            {
                if (ids.Add(id))
                {
                    added++;
                }
            }

            if (added == 0)
            {
                break;
            }

            nullableIds = ids.Select(id => (EntityId?)id).ToList();
            extra = await dbContext.Contracts
                .Where(c => c.TenantId == tenantId && c.ParentContractId != null && nullableIds.Contains(c.ParentContractId))
                .Select(c => c.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var idList = ids.ToList();
        var contracts = await dbContext.Contracts
            .Where(c => c.TenantId == tenantId && idList.Contains(c.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (contracts.Count == 0)
        {
            return 0;
        }

        var clauseIds = await dbContext.Clauses
            .Where(c => c.TenantId == tenantId && idList.Contains(c.ContractId))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var obligationIds = await dbContext.Obligations
            .Where(o => o.TenantId == tenantId && idList.Contains(o.ContractId))
            .Select(o => o.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var riskIds = await dbContext.Risks
            .Where(r => r.TenantId == tenantId && idList.Contains(r.ContractId))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var lineItemIds = await dbContext.ContractLineItems
            .Where(l => l.TenantId == tenantId && idList.Contains(l.ContractId))
            .Select(l => l.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var historyTargets = idList
            .Concat(clauseIds)
            .Concat(obligationIds)
            .Concat(riskIds)
            .Concat(lineItemIds)
            .ToList();

        var history = await dbContext.CorrectionHistories
            .Where(h => h.TenantId == tenantId && historyTargets.Contains(h.TargetEntityId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var steps = await dbContext.ContractNegotiationSteps
            .Where(s => s.TenantId == tenantId && idList.Contains(s.ContractId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var contract in contracts)
        {
            contract.ParentContractId = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        dbContext.CorrectionHistories.RemoveRange(history);
        dbContext.ContractNegotiationSteps.RemoveRange(steps);
        dbContext.Contracts.RemoveRange(contracts);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return contracts.Count;
    }
}
