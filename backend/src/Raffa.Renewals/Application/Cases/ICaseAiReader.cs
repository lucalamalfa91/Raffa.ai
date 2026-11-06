using Raffa.SharedKernel;
using Raffa.SharedKernel.Text;

namespace Raffa.Renewals.Application.Cases;

/// <summary>
/// The model-backed readings a renewal case needs in production, computed before the pure engine runs
/// (the engine never awaits): the supplier reply read against the plan and terms, and the functions an
/// alternative supplier must cover. Null = no reading (no model, failure, off-schema): the deterministic
/// <see cref="ICaseScriptServices"/> answer stands. Implemented in AiFlows; the default reads nothing.
/// </summary>
public interface ICaseAiReader
{
    Task<CaseReplyAnalysis?> ReadReplyAsync(
        TenantId tenantId, RenewalCase c, CaseContractFacts contract, string replyText, int round, string lang, CancellationToken cancellationToken = default);

    Task<CaseFeatureSet?> ReadFeaturesAsync(
        TenantId tenantId, CaseContractFacts contract, string lang, CancellationToken cancellationToken = default);

    /// <summary>A received contract draft (its text) against the agreed position: one row per agreed point.</summary>
    Task<IReadOnlyList<CaseDraftRow>?> ReadDraftAsync(
        TenantId tenantId, RenewalCase c, CaseContractFacts contract, string fileName, string documentText, string lang, CancellationToken cancellationToken = default);

    /// <summary>The signed contract (its text) against the agreed position and the last verified draft.</summary>
    Task<CaseSignedCheck?> ReadSignedAsync(
        TenantId tenantId, RenewalCase c, CaseContractFacts contract, string fileName, string documentText, string lang, CancellationToken cancellationToken = default);

    /// <summary>An alternative supplier's offer (its text) against the functions and sizing the case asks for.</summary>
    Task<CaseAltOffer?> ReadOfferAsync(
        TenantId tenantId, RenewalCase c, CaseContractFacts contract, string vendor, string documentText, string by, string lang, CancellationToken cancellationToken = default);

    /// <summary>
    /// The email draft written or changed by the model. With an <paramref name="instruction"/> (the person's
    /// words in the case composer): a revised version when the words ask to change the email, an empty-bodied revision
    /// when they are a question about the contract. Without one: the draft made complete and professional. Null also on any
    /// failure or doubt (a figure the model added, nothing changed): the current draft stands.
    /// </summary>
    Task<CaseEmailRevision?> ReviseEmailAsync(
        TenantId tenantId, RenewalCase c, CaseContractFacts contract, CaseEmail email, string? instruction, string lang, CancellationToken cancellationToken = default) =>
        Task.FromResult<CaseEmailRevision?>(null);

    /// <summary>
    /// The four interview questions worded for this contract (its products, dates, uplift, clauses) and the levers worked
    /// out in chat, instead of the standard text. Null on any failure or doubt: the standard wording stands.
    /// </summary>
    Task<CaseInterviewTailoring?> ReadInterviewAsync(
        TenantId tenantId, CaseContractFacts contract, string? levers, string lang, CancellationToken cancellationToken = default) =>
        Task.FromResult<CaseInterviewTailoring?>(null);
}

/// <summary>A new subject and body for an email draft, and Raffa's one-line note on what changed (user's language).</summary>
public sealed record CaseEmailRevision(string Subject, string Body, string Note);

/// <summary>The default: no model readings.</summary>
public sealed class NoCaseAiReader : ICaseAiReader
{
    public Task<CaseReplyAnalysis?> ReadReplyAsync(
        TenantId tenantId, RenewalCase c, CaseContractFacts contract, string replyText, int round, string lang, CancellationToken cancellationToken = default) =>
        Task.FromResult<CaseReplyAnalysis?>(null);

    public Task<CaseFeatureSet?> ReadFeaturesAsync(TenantId tenantId, CaseContractFacts contract, string lang, CancellationToken cancellationToken = default) =>
        Task.FromResult<CaseFeatureSet?>(null);

    public Task<IReadOnlyList<CaseDraftRow>?> ReadDraftAsync(
        TenantId tenantId, RenewalCase c, CaseContractFacts contract, string fileName, string documentText, string lang, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CaseDraftRow>?>(null);

    public Task<CaseSignedCheck?> ReadSignedAsync(
        TenantId tenantId, RenewalCase c, CaseContractFacts contract, string fileName, string documentText, string lang, CancellationToken cancellationToken = default) =>
        Task.FromResult<CaseSignedCheck?>(null);

    public Task<CaseAltOffer?> ReadOfferAsync(
        TenantId tenantId, RenewalCase c, CaseContractFacts contract, string vendor, string documentText, string by, string lang, CancellationToken cancellationToken = default) =>
        Task.FromResult<CaseAltOffer?>(null);
}

/// <summary>The script services of one engine step with the model readings taken before it.</summary>
public sealed class ReadCaseScriptServices(
    ICaseScriptServices inner,
    CaseReplyAnalysis? reply = null,
    CaseFeatureSet? features = null,
    IReadOnlyList<CaseDraftRow>? draftRows = null,
    CaseAltOffer? offer = null) : ICaseScriptServices
{
    public bool Scripted => inner.Scripted;

    public string SampleReply(RenewalCase c, CaseContext ctx, int round) => inner.SampleReply(c, ctx, round);

    public CaseReplyAnalysis AnalyzeReply(RenewalCase c, CaseContext ctx, int rid) => reply ?? inner.AnalyzeReply(c, ctx, rid);

    public IReadOnlyList<CaseDraftRow> VerifyDraft(RenewalCase c, CaseContext ctx, int version) => draftRows ?? inner.VerifyDraft(c, ctx, version);

    public CaseSignedCheck CheckSigned(RenewalCase c, CaseContext ctx) => inner.CheckSigned(c, ctx);

    public CasePipe StartExtraction(RenewalCase c, CaseContext ctx) => inner.StartExtraction(c, ctx);

    public IReadOnlyList<string> ExampleVendors() => inner.ExampleVendors();

    public CaseAltOffer VendorOffer(RenewalCase c, CaseContext ctx, string vendor, string by) => offer ?? inner.VendorOffer(c, ctx, vendor, by);

    public string? ExampleAddress(CaseEmail email, CaseContext ctx) => inner.ExampleAddress(email, ctx);

    public double CounterPrice(CaseContext ctx, int termMonths) => inner.CounterPrice(ctx, termMonths);

    public CaseFeatureSet Features(CaseContext ctx) => features ?? inner.Features(ctx);
}
