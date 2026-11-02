using Microsoft.EntityFrameworkCore;
using Raffa.Chat.Application.Conversations;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Renewals.Application;
using Raffa.Renewals.Application.CaseWork;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Api;

/// <summary>
/// Host orchestrator for <c>DELETE /api/contracts/{id}</c>: removes one contract with its source
/// documents, its renewal case and trackers, and the Ask chats scoped to it. The single-contract
/// counterpart of <see cref="DocumentPurgeAllService"/>; it lives here for the same reason (ADR-002:
/// Documents, Chat and Renewals do not reference each other).
/// </summary>
internal sealed class ContractDeleteService(
    DocumentsContractsDbContext dbContext,
    DocumentDeleteService documentDeleteService,
    ContractPurgeService contractPurgeService,
    ConversationService conversationService,
    RenewalActionService renewalActionService,
    RenewalAlertService renewalAlertService,
    RenewalCaseService renewalCaseService,
    ITenantContext tenantContext,
    IAuditWriter auditWriter,
    IClock clock)
{
    public const string DeletedAuditAction = "contract.deleted";

    /// <summary>False when the contract does not exist for this tenant; a failed document deletion
    /// (storage unavailable) is reported as <see cref="ContractDeleteOutcome.Failed"/> and nothing else is touched.</summary>
    public async Task<ContractDeleteOutcome> DeleteAsync(TenantId tenantId, EntityId contractId, string actor, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        using var scope = tenantContext.BeginScope(tenantId);

        if (!await dbContext.Contracts.AnyAsync(c => c.TenantId == tenantId && c.Id == contractId, ct).ConfigureAwait(false))
        {
            return ContractDeleteOutcome.NotFound;
        }

        var documentIds = await dbContext.Documents
            .Where(d => d.TenantId == tenantId && d.ContractId == contractId)
            .Select(d => d.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var documentId in documentIds)
        {
            var result = await documentDeleteService.DeleteAsync(tenantId, documentId, actor, ct).ConfigureAwait(false);
            if (result is { IsFailure: true })
            {
                return ContractDeleteOutcome.Failed;
            }
        }

        var ids = new List<EntityId> { contractId };
        var guids = new List<Guid> { contractId.Value };
        var conversations = await conversationService.DeleteByScopeContractsAsync(tenantId, actor, ids, ct).ConfigureAwait(false);
        var actions = await renewalActionService.DeleteForContractsAsync(tenantId, ids, ct).ConfigureAwait(false);
        var alerts = await renewalAlertService.DeleteForContractsAsync(tenantId, ids, ct).ConfigureAwait(false);
        var cases = await renewalCaseService.DeleteForContractsAsync(tenantId, guids, ct).ConfigureAwait(false);
        var purged = await contractPurgeService.PurgeAsync(tenantId, ids, ct).ConfigureAwait(false);

        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                actor,
                DeletedAuditAction,
                "contract",
                contractId.Value.ToString(),
                clock.UtcNow,
                $"documentsDeleted={documentIds.Count}; contractsPurged={purged}; conversationsDeleted={conversations}; renewalActionsDeleted={actions}; renewalAlertsDeleted={alerts}; casesDeleted={cases}"),
            ct).ConfigureAwait(false);
        return ContractDeleteOutcome.Deleted;
    }
}

internal enum ContractDeleteOutcome
{
    Deleted,
    NotFound,
    Failed,
}
