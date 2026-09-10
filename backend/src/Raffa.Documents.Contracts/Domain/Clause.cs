using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Domain;

/// <summary>
/// A single extracted contract clause (product spec §6 "ContractClause"). Every consequential
/// fact carries source evidence and a confidence score — never shown as bare truth without both
/// (Appendix C rule 2).
/// </summary>
public sealed class Clause : TenantScopedEntity
{
    public required EntityId ContractId { get; set; }
    public EntityId? SourceDocumentId { get; set; }

    public required string ClauseType { get; set; }
    public required string RawText { get; set; }
    public string? NormalizedValue { get; set; }
    public RiskSeverity? RiskLevel { get; set; }

    /// <summary>Page/section evidence pointer (Appendix C rule 2).</summary>
    public string? SourceSpan { get; set; }
    public int? SourcePage { get; set; }
    public double? Confidence { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>Optimistic-concurrency guard — see <see cref="Contract.Version"/>.</summary>
    public int Version { get; set; } = 1;
}
