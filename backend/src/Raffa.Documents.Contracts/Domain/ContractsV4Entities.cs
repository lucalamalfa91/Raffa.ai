using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Domain;

/// <summary>
/// AskRaffa 4.0 Contratto, "Cosa richiede una decisione" (BACKEND-SPEC §2.4): the user's choice on
/// one negotiable point of a contract — <c>negotiate | accept | clarify</c> — with who and when.
/// One row per point per contract; clearing the choice deletes the row.
/// </summary>
public sealed class ContractPointDecision : TenantScopedEntity
{
    public required EntityId ContractId { get; set; }

    /// <summary>The point's stable key (slug of its topic, e.g. <c>price-increase</c>).</summary>
    public required string PointKey { get; set; }

    /// <summary><c>negotiate | accept | clarify</c>.</summary>
    public required string Decision { get; set; }

    /// <summary>The display name of who decided (as the card shows it).</summary>
    public required string DecidedBy { get; set; }

    public required DateTimeOffset DecidedAt { get; set; }
}

/// <summary>
/// AskRaffa 4.0 renewed terms (BACKEND-SPEC §2.9): one version of a contract's running terms. The
/// row with <see cref="IsCurrent"/> overrides the contract's own dates and spend in
/// <c>IContractFactsReader</c>.
/// </summary>
public sealed class ContractTermVersion : TenantScopedEntity
{
    public required EntityId ContractId { get; set; }

    public EntityId? SourceDocumentId { get; set; }

    public Guid? CaseId { get; set; }

    public required DateOnly ValidFrom { get; set; }

    public required DateOnly ValidTo { get; set; }

    public int? NoticeDays { get; set; }

    public decimal? AnnualValue { get; set; }

    public required string Currency { get; set; }

    public bool? AutoRenew { get; set; }

    public decimal? UpliftPct { get; set; }

    public bool IsCurrent { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// AskRaffa 4.0 Revisione (one field at a time): the user's decision on one flagged field of a
/// document being reviewed — <c>accept</c>, or <c>correct</c> with the corrected value.
/// </summary>
public sealed class DocumentFieldDecision : TenantScopedEntity
{
    public required EntityId DocumentId { get; set; }

    public required string FieldKey { get; set; }

    /// <summary><c>accept | correct</c>.</summary>
    public required string Decision { get; set; }

    public string? Value { get; set; }

    public required string DecidedBy { get; set; }

    public required DateTimeOffset DecidedAt { get; set; }
}
