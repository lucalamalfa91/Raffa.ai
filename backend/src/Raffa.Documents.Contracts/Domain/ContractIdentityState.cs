namespace Raffa.Documents.Contracts.Domain;

/// <summary>
/// Tracks whether a <see cref="Contract"/>'s identity fields have been set by the fast
/// headline pass (provisional) or by the full 7-stage enrich pipeline (official).
///
/// <list type="bullet">
/// <item><b>Provisional</b> — set at upload time and after the one-call headline extract.
/// The UI shows these rows on Portfolio and Renewals; KPI counts and Ask Raffa ignore them.</item>
/// <item><b>Official</b> — set after <c>StagedExtractionService</c> completes all seven stages
/// and <c>ISupplierResolver</c> links a canonical supplier. KPI counts and Ask Raffa include
/// only official contracts. Human corrections also mark a contract official.</item>
/// </list>
/// </summary>
public enum ContractIdentityState
{
    Provisional,
    Official,
}
