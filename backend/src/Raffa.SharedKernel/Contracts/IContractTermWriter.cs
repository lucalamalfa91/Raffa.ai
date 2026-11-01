namespace Raffa.SharedKernel.Contracts;

/// <summary>
/// AskRaffa 4.0 renewed terms (BACKEND-SPEC §2.9): the terms a contract runs on after a renewal,
/// from the validated signed document. <see cref="IContractFactsReader"/> returns the current
/// version's dates and spend, so Oggi, Rinnovi, Contratti and Risultati read the new values.
/// </summary>
public sealed record ContractTermVersionInput(
    Guid ContractId,
    Guid? SourceDocumentId,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    int? NoticeDays,
    decimal? AnnualValue,
    string Currency,
    bool? AutoRenew,
    decimal? UpliftPercent,
    Guid? CaseId = null);

/// <summary>
/// Writes a new current <c>contract_term_version</c> (the previous current one is superseded).
/// Implemented by <c>Raffa.Documents.Contracts</c>; called by <c>Raffa.Renewals</c> when a case
/// closes as renewed.
/// </summary>
public interface IContractTermWriter
{
    /// <returns>The id of the new current version.</returns>
    Task<Guid> WriteCurrentAsync(TenantId tenantId, ContractTermVersionInput input, CancellationToken cancellationToken = default);
}
