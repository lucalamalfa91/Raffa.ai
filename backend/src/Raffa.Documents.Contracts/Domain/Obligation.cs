using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Domain;

/// <summary>
/// A contractual obligation with a due date/recurrence (product spec §6 "Obligation"). Renewal
/// deadline math is computed deterministically downstream (Renewals module, Appendix C rule 6);
/// this row is the extracted, evidenced fact that computation operates on.
/// </summary>
public sealed class Obligation : TenantScopedEntity
{
    public required EntityId ContractId { get; set; }
    public EntityId? SourceDocumentId { get; set; }

    public required string Party { get; set; }
    public required string ObligationType { get; set; }
    public required string Description { get; set; }
    public DateOnly? DueDate { get; set; }
    public string? RecurrenceRule { get; set; }
    public string? Criticality { get; set; }
    public string? Status { get; set; }
    public double? Confidence { get; set; }

    /// <summary>Page/section evidence pointer (Appendix C rule 2; spec §7.3 "every extracted
    /// fact carries source span + confidence"). Added by task E02/F01/US02/T01
    /// (us-02-staged-extraction, AC-2) — <see cref="SourceDocumentId"/> alone names *which*
    /// document but not *where in it*, unlike <see cref="Clause"/>, which already had both.
    /// </summary>
    /// <summary>Page/section evidence pointer (Appendix C rule 2), mirroring
    /// <see cref="Clause.SourceSpan"/>/<see cref="Clause.SourcePage"/> — <see cref="SourceDocumentId"/>
    /// alone names which document, not where in it.</summary>
    public string? SourceSpan { get; set; }
    public int? SourcePage { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>Optimistic-concurrency guard — see <see cref="Contract.Version"/>.</summary>
    public int Version { get; set; } = 1;
}
