using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Documents.Contracts.Application.Facts;

/// <summary><see cref="IContractFactsReader"/> over <c>contract_facts</c> (tenant-scoped, RLS).</summary>
public sealed class ContractFactsReader(DocumentsContractsDbContext dbContext, ITenantContext tenantContext)
    : IContractFactsReader
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ContractFacts>> ListValidatedAsync(
        TenantId tenantId, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);
        var rows = await dbContext.ContractFacts.AsNoTracking()
            .Where(f => f.TenantId == tenantId && f.ValidatedAt != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(r => Deserialize(r.Facts, r.ContractId, r.ValidatedAt))
            .OrderBy(f => f.NoticeDeadline ?? DateOnly.MaxValue)
            .ThenBy(f => f.Supplier, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<ContractFacts?> GetAsync(
        TenantId tenantId, EntityId contractId, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);
        var row = await dbContext.ContractFacts.AsNoTracking()
            .Where(f => f.TenantId == tenantId && f.ContractId == contractId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : Deserialize(row.Facts, row.ContractId, row.ValidatedAt);
    }

    public static string Serialize(ContractFacts facts) => JsonSerializer.Serialize(facts, Json);

    /// <summary>The row's own columns win over what the JSON says (one source of truth for the
    /// id and the validation state).</summary>
    private static ContractFacts Deserialize(string json, EntityId contractId, DateTimeOffset? validatedAt) =>
        (JsonSerializer.Deserialize<ContractFacts>(json, Json) ?? new ContractFacts()) with
        {
            ContractId = contractId.Value,
            ValidatedAt = validatedAt,
        };
}
