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

    // One row per agreed point, never a point nobody agreed (a liability cap, a payment term…).
    public IReadOnlyList<CaseDraftRow> VerifyDraft(RenewalCase c, CaseContext ctx, int version)
    {
        var a = c.Agreed ?? new CaseAgreed();
        var f = CaseRules.Price(ctx);
        var manual = B("not read automatically — check it in the document");
        var rows = new List<CaseDraftRow>
        {
            new() { Label = B("Unit price"), Agreed = CaseRules.Ps(f, a.Price), Draft = manual, Res = "manual", Src = string.Empty },
            new() { Label = B("Term"), Agreed = CaseText.Message("{term} months", ("term", a.Term)), Draft = manual, Res = "manual", Src = string.Empty },
        };
        if (a.Cap is { } cap)
        {
            rows.Add(new() { Label = Tx.B("Uplift cap", "Tetto aumento"), Agreed = CaseAgreement.CapText(cap), Draft = manual, Res = "manual", Src = string.Empty });
        }

        if (a.Payment is { Length: > 0 } pay)
        {
            rows.Add(new() { Label = Tx.B("Payment terms", "Pagamenti"), Agreed = CaseText.Of(pay), Draft = manual, Res = "manual", Src = string.Empty });
        }

        if (a.Liab is { } liab)
        {
            rows.Add(new() { Label = B("Liability cap"), Agreed = CaseText.Message("{liab} months of fees", ("liab", liab)), Draft = manual, Res = "manual", Src = string.Empty });
        }

        return rows;
    }

    // The model's reading of the signed file, stored on the case when it was uploaded; nothing read = 0 / 0.
    public CaseSignedCheck CheckSigned(RenewalCase c, CaseContext ctx) => new(c.Signed?.CheckedPoints ?? 0, c.Signed?.Differences ?? 0);

    // The signed file is not extracted automatically in this release: the values that will become the
    // contract's new terms are put to the owner, with their source, to check against it before validating.
    public CasePipe StartExtraction(RenewalCase c, CaseContext ctx) => CaseAgreement.SignedFieldsToCheck(c, ctx);

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
