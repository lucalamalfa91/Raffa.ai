namespace Raffa.Renewals.Domain;

/// <summary>
/// Contract-risk input for
/// <see cref="Raffa.Renewals.Application.PriorityScoreCalculator"/>'s contract-risk component
/// (product spec §9.2 "Priority Score = ... + Contract Risk"; task E03/F01/US02/T01, parent story
/// us-02-priority-score AC-1).
///
/// <para>
/// Mirrors <c>Raffa.Documents.Contracts.Domain.RiskSeverity</c>'s four levels rather than
/// referencing that type: ADR-002's dependency-direction rule allows <c>Raffa.Renewals</c> to
/// reference only <c>Raffa.SharedKernel</c> and <c>Raffa.Benchmark</c>
/// (<c>Raffa.ArchitectureTests.DependencyDirectionTests</c>'s allow-list for this module; see
/// <c>backend/README.md</c>'s "Dependency direction" table) — the same shape decision
/// <see cref="Raffa.Renewals.Application.ContractRenewalTerms"/> and
/// <c>Raffa.Chat.Application.ContractFact</c> already made for the same reason. A composition
/// root maps <c>PortfolioListItem.Risk</c> (the highest <c>RiskSeverity</c> across a contract's
/// <c>Risk</c> rows) onto this enum 1:1; no task in this wave wires that composition yet — the
/// same "caller supplies it however it likes today, a real mapping lands later" gap
/// <see cref="Raffa.Renewals.Application.ContractRenewalTerms"/>'s own doc comment already
/// documents for this module.
/// </para>
/// </summary>
public enum ContractRiskLevel
{
    Low,
    Medium,
    High,
    Critical,
}
