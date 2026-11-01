using Raffa.SharedKernel.Text;

namespace Raffa.SharedKernel.Cases;

/// <summary>Quotes waiting for a decision (status <c>checked</c>) for the Today feed. Implemented by Quotes (slice 6); default empty.</summary>
public interface IOpenQuoteDecisions
{
    Task<IReadOnlyList<OpenQuoteDecision>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <param name="OwnerName">Display name of who decides (the uploader / case owner).</param>
/// <param name="Why">Why the decision is pending (e.g. "No price comparison available…").</param>
public sealed record OpenQuoteDecision(
    Guid QuoteId,
    Guid? ContractId,
    string Supplier,
    string? Contract,
    string OwnerName,
    CaseText Why,
    DateOnly ValidUntil);

/// <summary>Documents whose extraction waits for the user's check (Today "review" items). Implemented by Documents (slice 4); default empty.</summary>
public interface IDocumentsNeedingReview
{
    Task<IReadOnlyList<DocumentNeedingReview>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

public sealed record DocumentNeedingReview(Guid DocumentId, string File, int FieldsToCheck, string? OwnerName, DateOnly? Due);
