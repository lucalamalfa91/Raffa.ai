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
}

/// <summary>The default: no model readings.</summary>
public sealed class NoCaseAiReader : ICaseAiReader
{
    public Task<CaseReplyAnalysis?> ReadReplyAsync(
        TenantId tenantId, RenewalCase c, CaseContractFacts contract, string replyText, int round, string lang, CancellationToken cancellationToken = default) =>
        Task.FromResult<CaseReplyAnalysis?>(null);

    public Task<CaseFeatureSet?> ReadFeaturesAsync(TenantId tenantId, CaseContractFacts contract, string lang, CancellationToken cancellationToken = default) =>
        Task.FromResult<CaseFeatureSet?>(null);
}

/// <summary>The script services of one engine step with the model readings taken before it.</summary>
public sealed class ReadCaseScriptServices(ICaseScriptServices inner, CaseReplyAnalysis? reply, CaseFeatureSet? features) : ICaseScriptServices
{
    public string SampleReply(RenewalCase c, CaseContext ctx, int round) => inner.SampleReply(c, ctx, round);

    public CaseReplyAnalysis AnalyzeReply(RenewalCase c, CaseContext ctx, int rid) => reply ?? inner.AnalyzeReply(c, ctx, rid);

    public IReadOnlyList<CaseDraftRow> VerifyDraft(RenewalCase c, CaseContext ctx, int version) => inner.VerifyDraft(c, ctx, version);

    public CaseSignedCheck CheckSigned(RenewalCase c, CaseContext ctx) => inner.CheckSigned(c, ctx);

    public CasePipe StartExtraction(RenewalCase c, CaseContext ctx) => inner.StartExtraction(c, ctx);

    public IReadOnlyList<string> ExampleVendors() => inner.ExampleVendors();

    public CaseAltOffer VendorOffer(RenewalCase c, CaseContext ctx, string vendor, string by) => inner.VendorOffer(c, ctx, vendor, by);

    public string? ExampleAddress(CaseEmail email, CaseContext ctx) => inner.ExampleAddress(email, ctx);

    public double CounterPrice(CaseContext ctx, int termMonths) => inner.CounterPrice(ctx, termMonths);

    public CaseFeatureSet Features(CaseContext ctx) => features ?? inner.Features(ctx);
}
