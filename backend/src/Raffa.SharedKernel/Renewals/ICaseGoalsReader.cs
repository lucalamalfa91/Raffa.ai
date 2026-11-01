namespace Raffa.SharedKernel.Renewals;

/// <summary>
/// The negotiation goals a renewal case collected in its interview (the prototype's
/// <c>case.goals</c>: target, term, policy), read by the Offerta assessment (<c>assessQ</c>) to compare
/// a quote with <i>the user's own</i> target — never with a benchmark. Implemented by
/// <c>Raffa.Renewals</c>; the default (<see cref="NoCaseGoalsReader"/>) knows no goals.
/// </summary>
public interface ICaseGoalsReader
{
    /// <summary>The goals of <paramref name="caseId"/>, or of the open case on
    /// <paramref name="contractId"/> when no case id is known; null when there are none.</summary>
    Task<CaseGoals?> GetAsync(
        TenantId tenantId,
        EntityId? caseId,
        EntityId? contractId,
        CancellationToken cancellationToken = default);
}

/// <param name="Target"><c>p5</c> | <c>p10</c> | <c>budget</c> | <c>custom</c> | <c>none</c>, or null.</param>
/// <param name="PlanTermMonths">The term the plan asks for (12 | 24 | 36), or null.</param>
/// <param name="PolicyLiabilityMinMonths">The company policy's minimum liability in months when the
/// case applies the policy, or null.</param>
public sealed record CaseGoals(
    EntityId? CaseId,
    string? Target,
    int? PlanTermMonths,
    int? PolicyLiabilityMinMonths);

/// <summary>Default reader: no renewal case module, no goals.</summary>
public sealed class NoCaseGoalsReader : ICaseGoalsReader
{
    public Task<CaseGoals?> GetAsync(
        TenantId tenantId, EntityId? caseId, EntityId? contractId, CancellationToken cancellationToken = default) =>
        Task.FromResult<CaseGoals?>(null);
}
