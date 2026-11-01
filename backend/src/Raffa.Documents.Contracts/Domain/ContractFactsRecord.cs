using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Domain;

/// <summary>
/// AskRaffa 4.0: the <see cref="Raffa.SharedKernel.Contracts.ContractFacts"/> profile of one
/// contract, stored as jsonb (<c>contract_facts</c>). Written at validation (or by the fixture
/// seeder) and read through <see cref="Raffa.SharedKernel.Contracts.IContractFactsReader"/>.
/// </summary>
public sealed class ContractFactsRecord : TenantScopedEntity
{
    public required EntityId ContractId { get; set; }

    /// <summary>The serialised <c>ContractFacts</c> (camelCase JSON).</summary>
    public required string Facts { get; set; }

    /// <summary>When the contract was validated; null while it is not.</summary>
    public DateTimeOffset? ValidatedAt { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }
}
