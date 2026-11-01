namespace Raffa.SharedKernel.Savings;

/// <summary>
/// AskRaffa 4.0 Risultati (BACKEND-SPEC §2.10 <c>case_objective</c>): the objective the user set in a
/// renewal case (interview question 3) — a user target, never an estimate. Risultati reads every open
/// case's objective through <see cref="ICaseObjectiveSource"/>; the default implementation (in
/// <c>Raffa.Savings</c>) reads the <c>case_objective</c> table, which the Renewals module keeps up to
/// date through <see cref="ICaseObjectiveWriter"/> (or replaces the source with its own reader).
/// </summary>
/// <param name="CaseId">The renewal case.</param>
/// <param name="ContractId">The contract the case renews.</param>
/// <param name="OwnerName">The case owner's display name, when known.</param>
/// <param name="Kind">
/// <c>null</c> (no objective asked yet) | <c>none</c> (no target chosen) | <c>p5</c> | <c>p10</c> |
/// <c>budget</c> (budget −10%) | <c>custom</c>.
/// </param>
/// <param name="Pct">Percent target for <c>custom</c>, when the user gave one.</param>
/// <param name="Amount">Annual amount target for <c>custom</c>, when the user gave one.</param>
/// <param name="Text">The user's own words, when given.</param>
/// <param name="Outcome">
/// <c>null</c> while the case is open; <c>renewed</c> | <c>terminated</c> once it is closed (a closed
/// case has no open objective).
/// </param>
public sealed record CaseObjectiveInfo(
    Guid CaseId,
    Guid ContractId,
    string? OwnerName,
    string? Kind,
    decimal? Pct,
    decimal? Amount,
    string? Text,
    string? Outcome)
{
    /// <summary>The percent the objective applies to the contract spend (prototype: p5 → 5, p10 and
    /// budget → 10, anything else → none).</summary>
    public int TargetPercent => Kind switch
    {
        "p5" => 5,
        "p10" or "budget" => 10,
        _ => 0,
    };

    public bool IsOpen => Outcome is null;
}

/// <summary>Reads the renewal cases' objectives (one row per case) for a tenant.</summary>
public interface ICaseObjectiveSource
{
    /// <summary>Every case objective of the tenant, open and closed.</summary>
    Task<IReadOnlyList<CaseObjectiveInfo>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Writes a case's objective (called by the Renewals module when a case opens, when the
/// interview fixes the target and when the case closes).</summary>
public interface ICaseObjectiveWriter
{
    Task UpsertAsync(TenantId tenantId, CaseObjectiveInfo objective, CancellationToken cancellationToken = default);
}
