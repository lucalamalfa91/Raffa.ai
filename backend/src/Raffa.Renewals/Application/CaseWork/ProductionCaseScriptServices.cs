using Raffa.Renewals.Application.Cases;
using Raffa.SharedKernel.Text;

namespace Raffa.Renewals.Application.CaseWork;

/// <summary>
/// The production <see cref="ICaseScriptServices"/>: no scripted demo content. Raffa never invents a
/// recipient (no example address — the button disappears), never invents a supplier reply, never
/// guesses a price the supplier did not state. Where an automatic reading (reply analysis, draft
/// comparison, extraction of the signed file) is not available in this release it says so, and the
/// person records the terms themselves — an honest "not available" instead of a made-up result.
/// </summary>
public sealed class ProductionCaseScriptServices : ICaseScriptServices
{
    private static CaseText B(string en) => CaseText.Message(en);

    public string SampleReply(RenewalCase c, CaseContext ctx, int round) => string.Empty;

    public CaseReplyAnalysis AnalyzeReply(RenewalCase c, CaseContext ctx, int rid)
    {
        var rows = new List<CaseReplyRow>
        {
            new()
            {
                Label = B("Automatic reading"),
                Said = B("Not available in this release — the reply is kept word for word in the case."),
                Plan = B("Read it and record what changed in the terms yourself."),
                State = "unclear",
                Warn = CaseText.Empty,
            },
        };
        return new CaseReplyAnalysis(rows, []);
    }

    public IReadOnlyList<CaseDraftRow> VerifyDraft(RenewalCase c, CaseContext ctx, int version)
    {
        var a = c.Agreed ?? new CaseAgreed();
        var f = CaseRules.Price(ctx);
        var manual = B("not read automatically — check it in the document");
        return
        [
            new CaseDraftRow { Label = B("Unit price"), Agreed = CaseRules.Ps(f, a.Price), Draft = manual, Res = "manual", Src = string.Empty },
            new CaseDraftRow { Label = B("Term"), Agreed = CaseText.Message("{term} months", ("term", a.Term)), Draft = manual, Res = "manual", Src = string.Empty },
            new CaseDraftRow { Label = B("Liability cap"), Agreed = CaseText.Message("{liab} months of fees", ("liab", a.Liab)), Draft = manual, Res = "manual", Src = string.Empty },
        ];
    }

    // The model's reading of the signed file, stored on the case when it was uploaded; nothing read = 0 / 0.
    public CaseSignedCheck CheckSigned(RenewalCase c, CaseContext ctx) => new(c.Signed?.CheckedPoints ?? 0, c.Signed?.Differences ?? 0);

    public CasePipe StartExtraction(RenewalCase c, CaseContext ctx) => new() { Stage = 0, Weak = [] };

    public IReadOnlyList<string> ExampleVendors() => [];

    public CaseAltOffer VendorOffer(RenewalCase c, CaseContext ctx, string vendor, string by) => new()
    {
        Price = 0,
        Total = 0,
        Term = 0,
        Cover = "not stated",
        Mig = "not stated",
        Gap = "Offer not read automatically — add its terms yourself",
        By = by,
    };

    public string? ExampleAddress(CaseEmail email, CaseContext ctx) => null;

    /// <summary>No supplier position has been read automatically: the current contract price stands.</summary>
    public double CounterPrice(CaseContext ctx, int termMonths) => CaseRules.Price(ctx).P0;

    // Only what the contract itself supports (its scope and quantities): never a function nobody read.
    public CaseFeatureSet Features(CaseContext ctx)
    {
        var items = new List<CaseText> { B("Core functions equal to your current contract") };
        if (ctx.Contract.Products.Count > 0)
        {
            items.Add(CaseText.Message("Same scope and quantities as today ({n} line items)", ("n", ctx.Contract.Products.Count)));
        }

        return new(null, items);
    }
}
