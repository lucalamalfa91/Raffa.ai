namespace Raffa.SharedKernel.Renewals;

/// <summary>
/// AskRaffa 4.0 Offerta "Prepara la controproposta" (BACKEND-SPEC §4 Quotes,
/// <c>POST /api/quotes/{id}/counter-proposal</c>): hands a counter-proposal text to the renewal case
/// as a <b>draft</b> case email — never sent, never with an invented recipient. Implemented by
/// <c>Raffa.Renewals</c>; the default (<see cref="NotAvailableCaseEmailDraftSink"/>) answers
/// <see cref="CaseEmailDraftResult.NotAvailable"/> so the UI says so honestly.
/// </summary>
public interface ICaseEmailDraftSink
{
    Task<CaseEmailDraftResult> CreateDraftAsync(
        TenantId tenantId,
        CaseEmailDraftRequest request,
        CancellationToken cancellationToken = default);
}

/// <param name="CaseId">The case to attach the draft to, when the quote is linked to one.</param>
/// <param name="ContractId">The contract, so the case can be found when only that is known.</param>
/// <param name="Origin">What produced the draft, e.g. <c>quote:{id}</c>.</param>
/// <param name="Actor">Who asked for it (the caller identity).</param>
public sealed record CaseEmailDraftRequest(
    EntityId? CaseId,
    EntityId? ContractId,
    string Subject,
    string Body,
    string Origin,
    string Actor);

public sealed record CaseEmailDraftResult(bool Available, EntityId? CaseId, EntityId? EmailId)
{
    public static CaseEmailDraftResult NotAvailable { get; } = new(false, null, null);
}

/// <summary>Default sink: no renewal case module receives drafts.</summary>
public sealed class NotAvailableCaseEmailDraftSink : ICaseEmailDraftSink
{
    public Task<CaseEmailDraftResult> CreateDraftAsync(
        TenantId tenantId, CaseEmailDraftRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(CaseEmailDraftResult.NotAvailable);
}
