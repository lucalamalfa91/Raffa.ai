using Raffa.SharedKernel.Text;

namespace Raffa.SharedKernel.Cases;

/// <summary>
/// The renewal case as other modules see it (AskRaffa 4.0, slices 2+3): one entry per contract that
/// has, or had, a case. Implemented by <c>Raffa.Renewals</c>; read by Contratti/Contratto (slice 4),
/// Risultati (slice 5) and Chiedi (slice 6) without referencing the Renewals module.
/// </summary>
public interface IContractCaseStatusReader
{
    /// <summary>The latest case of every contract of the tenant that has one (open or closed).</summary>
    Task<IReadOnlyList<ContractCaseStatus>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>The latest case of one contract, or null when none was ever opened.</summary>
    Task<ContractCaseStatus?> GetAsync(TenantId tenantId, EntityId contractId, CancellationToken cancellationToken = default);
}

/// <param name="ContractId">The contract.</param>
/// <param name="HasOpenCase">An open (not closed) case exists.</param>
/// <param name="CaseId">The case id (latest), null when none.</param>
/// <param name="Outcome"><c>renewed</c> | <c>terminated</c> | <c>replaced</c>, null while open.</param>
/// <param name="Stage">0–5 (Start … Acquisition), -1 when no case.</param>
/// <param name="OwnerUserId">Workspace user id of the owner, when known.</param>
/// <param name="Owner">Owner display name.</param>
/// <param name="Next">"Who — next action" of the case header.</param>
public sealed record ContractCaseStatus(
    Guid ContractId,
    bool HasOpenCase,
    Guid? CaseId,
    string? Outcome,
    int Stage,
    Guid? OwnerUserId,
    string? Owner,
    CaseText? Next);

/// <summary>The economic objectives users set inside their cases (Risultati "Obiettivi nelle pratiche").</summary>
public interface ICaseObjectiveSource
{
    Task<IReadOnlyList<CaseObjective>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <param name="Kind"><c>p5</c> | <c>p10</c> | <c>budget</c> | <c>custom</c> | <c>none</c>.</param>
/// <param name="Pct">Percent target (5, 10) when the kind implies one; never an estimate.</param>
/// <param name="Text">The user's own words for a custom objective.</param>
public sealed record CaseObjective(Guid CaseId, Guid ContractId, string Kind, decimal? Pct, decimal? Amount, string? Text, string Owner, bool Open);

/// <summary>
/// Puts a counter-proposal draft (from Offerta, slice 6) into the case's email lifecycle: it becomes
/// a <c>draft</c> email of the contract's open case — never sent, recipient never invented.
/// </summary>
public interface ICaseEmailDraftSink
{
    Task<CaseEmailDraftResult> AddCounterProposalDraftAsync(
        TenantId tenantId,
        EntityId contractId,
        string subject,
        string body,
        string actorExternalId,
        CancellationToken cancellationToken = default);
}

/// <param name="Ok">False when the contract has no open case (the caller should open one first).</param>
public sealed record CaseEmailDraftResult(bool Ok, Guid? CaseId, int? EmailIndex, string? Error);

/// <summary>
/// The case composer's free question (README §3 "Composer"): an answer about one contract that cites
/// its source, or says it cannot. Slice 6 owns the production sourced-answer path; the Renewals module
/// registers a deterministic answerer over the validated contract facts as the default.
/// </summary>
public interface ICaseQuestionAnswerer
{
    Task<CaseAnswer> AnswerAsync(TenantId tenantId, EntityId contractId, string text, CancellationToken cancellationToken = default);
}

/// <summary>The answer and where it comes from (a page/section, "validated fields", or the honest "no evidence").</summary>
public sealed record CaseAnswer(CaseText Text, CaseText Source);

/// <summary>
/// Writes the renewed terms of a contract (BACKEND-SPEC §2.9 <c>contract_term_version</c>) when a case
/// closes as renewed. Implemented by Documents/Contracts (slice 4); default no-op.
/// </summary>
public interface IContractTermWriter
{
    Task WriteRenewedTermsAsync(TenantId tenantId, RenewedContractTerms terms, CancellationToken cancellationToken = default);
}

public sealed record RenewedContractTerms(
    Guid ContractId,
    Guid CaseId,
    DateOnly? ValidFrom,
    DateOnly ValidTo,
    DateOnly? NoticeDeadline,
    int? NoticeDays,
    decimal? AnnualValue,
    string Currency,
    string? SourceFile);

/// <summary>
/// Records a contractual saving result (BACKEND-SPEC §2.10 <c>saving_result</c>) from a signed renewal.
/// Implemented by Savings (slice 5); default no-op. Only signed contracts produce one — never an estimate.
/// </summary>
public interface ISavingResultWriter
{
    Task RecordAsync(TenantId tenantId, SavingResultRecord result, CancellationToken cancellationToken = default);
}

public sealed record SavingResultRecord(
    Guid ContractId,
    Guid CaseId,
    string Supplier,
    string Kind,
    bool CountsAsNegotiation,
    decimal AmountAnnual,
    string Currency,
    string BaselineDescription,
    string Method,
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo,
    string? EvidenceFile,
    string RecordedBy);

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
