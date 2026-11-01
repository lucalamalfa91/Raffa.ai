namespace Raffa.SharedKernel.Contracts;

/// <summary>
/// The one way other modules read a contract (IMPLEMENTATION-PLAN.md §2). Implemented by
/// <c>Raffa.Documents.Contracts</c> over the <c>contract_facts</c> table; tenant-scoped (RLS).
/// </summary>
public interface IContractFactsReader
{
    /// <summary>Every validated contract of the tenant, nearest notice deadline first.</summary>
    Task<IReadOnlyList<ContractFacts>> ListValidatedAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>One contract's facts (validated or not), or null when the contract has none.</summary>
    Task<ContractFacts?> GetAsync(TenantId tenantId, EntityId contractId, CancellationToken cancellationToken = default);
}
