using Microsoft.EntityFrameworkCore;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Documents.Contracts.Application.Facts;

/// <summary><see cref="IContractTermWriter"/> over <c>contract_term_version</c>: the new version
/// becomes current, the previous current one is kept as history.</summary>
public sealed class ContractTermWriter(DocumentsContractsDbContext dbContext, ITenantContext tenantContext, IClock clock)
    : IContractTermWriter
{
    public async Task<Guid> WriteCurrentAsync(
        TenantId tenantId, ContractTermVersionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.ValidTo < input.ValidFrom)
        {
            throw new ArgumentException("ValidTo must not be before ValidFrom.", nameof(input));
        }

        using var _ = tenantContext.BeginScope(tenantId);
        var contractId = new EntityId(input.ContractId);
        var exists = await dbContext.Contracts
            .AnyAsync(c => c.TenantId == tenantId && c.Id == contractId, cancellationToken)
            .ConfigureAwait(false);
        if (!exists)
        {
            throw new InvalidOperationException($"Contract {input.ContractId} does not exist in this tenant.");
        }

        var current = await dbContext.ContractTermVersions
            .Where(v => v.TenantId == tenantId && v.ContractId == contractId && v.IsCurrent)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var previous in current)
        {
            previous.IsCurrent = false;
        }

        // The partial unique index allows one current row: clear the old one first.
        if (current.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var version = new ContractTermVersion
        {
            TenantId = tenantId,
            ContractId = contractId,
            SourceDocumentId = input.SourceDocumentId is { } doc ? new EntityId(doc) : null,
            CaseId = input.CaseId,
            ValidFrom = input.ValidFrom,
            ValidTo = input.ValidTo,
            NoticeDays = input.NoticeDays,
            AnnualValue = input.AnnualValue,
            Currency = string.IsNullOrWhiteSpace(input.Currency) ? "CHF" : input.Currency.Trim().ToUpperInvariant(),
            AutoRenew = input.AutoRenew,
            UpliftPct = input.UpliftPercent,
            IsCurrent = true,
            CreatedAt = clock.UtcNow,
        };
        dbContext.ContractTermVersions.Add(version);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return version.Id.Value;
    }
}
