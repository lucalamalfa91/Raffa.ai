using Raffa.Chat.Application.Conversations;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Renewals.Application;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Api;

/// <summary>
/// Host orchestrator for bulk <c>DELETE /api/documents</c>: removes every tenant document, then
/// cascade-deletes the contracts those files built (portfolio), their renewal trackers, and Ask
/// chats scoped to those contracts. Lives here because ADR-002 forbids Documents/Chat/Renewals from
/// referencing each other — only this composition root sees all three. <see langword="internal"/>
/// so <c>Host_must_not_contain_domain_types</c> stays green.
/// </summary>
internal sealed class DocumentPurgeAllService(
    DocumentsContractsDbContext dbContext,
    DocumentDeleteService documentDeleteService,
    ContractPurgeService contractPurgeService,
    ConversationService conversationService,
    RenewalActionService renewalActionService,
    RenewalAlertService renewalAlertService,
    ITenantContext tenantContext,
    IAuditWriter auditWriter,
    IClock clock)
{
    public const string PurgedAuditAction = "document.purged_all";

    public async Task PurgeAsync(TenantId tenantId, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var documentIds = await dbContext.Documents
            .Where(d => d.TenantId == tenantId)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var contractIds = await dbContext.Contracts
            .Where(c => c.TenantId == tenantId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var documentsDeleted = 0;
        foreach (var documentId in documentIds)
        {
            var result = await documentDeleteService
                .DeleteAsync(tenantId, documentId, actor, cancellationToken)
                .ConfigureAwait(false);
            if (result is { IsFailure: false })
            {
                documentsDeleted++;
            }
        }

        var conversationsDeleted = await conversationService
            .DeleteByScopeContractsAsync(tenantId, actor, contractIds, cancellationToken)
            .ConfigureAwait(false);
        var renewalActionsDeleted = await renewalActionService
            .DeleteForContractsAsync(tenantId, contractIds, cancellationToken)
            .ConfigureAwait(false);
        var renewalAlertsDeleted = await renewalAlertService
            .DeleteForContractsAsync(tenantId, contractIds, cancellationToken)
            .ConfigureAwait(false);
        var contractsPurged = await contractPurgeService
            .PurgeAsync(tenantId, contractIds, cancellationToken)
            .ConfigureAwait(false);

        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                actor,
                PurgedAuditAction,
                "document",
                "all",
                clock.UtcNow,
                $"documentsDeleted={documentsDeleted}; contractsPurged={contractsPurged}; conversationsDeleted={conversationsDeleted}; renewalActionsDeleted={renewalActionsDeleted}; renewalAlertsDeleted={renewalAlertsDeleted}"),
            cancellationToken).ConfigureAwait(false);
    }
}
