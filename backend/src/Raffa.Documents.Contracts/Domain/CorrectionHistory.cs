using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Domain;

/// <summary>
/// Append-only record of a human correction to any extracted field in this bounded context
/// (Appendix C rule 5 — never destructively overwrite contract history or human corrections;
/// rule 9 — capture corrections from day one). <see cref="TargetEntityType"/> /
/// <see cref="TargetEntityId"/> are a loose (non-FK) pointer because a correction can target
/// any entity owned by this module (Contract, Clause, Obligation, ...); no single foreign key
/// could express that.
/// </summary>
public sealed class CorrectionHistory : TenantScopedEntity
{
    /// <summary>Simple discriminator, e.g. "Contract", "Clause", "Obligation" — not modelled as
    /// an enum because new correctable entity types can be added to this module without
    /// touching this type.</summary>
    public required string TargetEntityType { get; set; }
    public required EntityId TargetEntityId { get; set; }
    public required string FieldName { get; set; }
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    public required string CorrectedBy { get; set; }
    public required DateTimeOffset CorrectedAt { get; set; }
    public string? Reason { get; set; }
}
