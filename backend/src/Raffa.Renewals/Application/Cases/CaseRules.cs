using System.Text.RegularExpressions;
using Raffa.SharedKernel.Text;

namespace Raffa.Renewals.Application.Cases;

/// <summary>
/// Message construction. <c>B(en, it, vars)</c> mirrors the prototype's <c>B(en,it)</c> / <c>S(en,it)</c>:
/// the English template is the message id; the Italian argument is not used at runtime — it is the
/// source of the generated catalogue <c>web/src/i18n/it/case4.ts</c> (kept next to the id so the two
/// never drift). <c>N(template, vars)</c> is a language-neutral template (same text in both languages).
/// </summary>
internal static class Tx
{
#pragma warning disable IDE0060 // `it` is read by the catalogue generator, not at runtime.
    public static CaseText B(string en, string it, params (string Name, object? Value)[] vars) => CaseText.Message(en, vars);
#pragma warning restore IDE0060

    public static CaseText N(string template, params (string Name, object? Value)[] vars) => CaseText.Message(template, vars);
}

/// <summary>The first product's price facts (<c>F(ctx)</c> in the prototype).</summary>
public sealed record CasePriceFacts(CaseProduct P, double P0, double Qty, string Unit, int Mult);

/// <summary>A clause the plan can change (<c>clauses(ctx)</c>): key p0/r1…, policy key, stance push|raise.</summary>
public sealed record CaseClauseRef(string K, string Lk, string Type, string Norm, string Ask, string Src, string Stance);

/// <summary>Price arithmetic, permissions and clause keys shared by the engine and the fixture services.</summary>
public static partial class CaseRules
{
    public const string Raffa = "Raffa.ai";

    public static CasePriceFacts Price(CaseContext ctx)
    {
        // The line the renewal prices is the first recurring one: a one-off amount (onboarding, setup)
        // is never renewed, so it never drives the price arithmetic.
        var products = ctx.Contract.Products;
        var p = products.FirstOrDefault(x => !x.OneOff) ?? (products.Count > 0 ? products[0] : new CaseProduct("—", string.Empty, "unit", 1, 0));
        return new CasePriceFacts(p, p.Price, p.Qty, p.Unit, p.Unit.Contains("month", StringComparison.Ordinal) ? 12 : 1);
    }

    /// <summary><c>ps(f,x)</c>: "CHF 148 per user / month" for unit prices, "CHF 2,280,000 / yr" for annual lines.</summary>
    public static string Ps(CasePriceFacts f, double x) =>
        f.P0 < 1000 ? "CHF " + CaseFormat.Js(x) + " per " + f.Unit : CaseFormat.Chf(x) + " / yr";

    public static string Role(RenewalCase c, string v) =>
        v == c.Approver ? "approver" : v == c.Owner ? "owner" : c.Delegates.ContainsKey(v) ? "delegate" : "collab";

    /// <summary>Only the approver or an explicit delegate approves emails.</summary>
    public static bool CanApprove(RenewalCase c, string v) => v == c.Approver || c.Delegates.ContainsKey(v);

    /// <summary>Owner, approver or delegate work the case. Chat participation grants nothing.</summary>
    public static bool CanWork(RenewalCase c, string v) => v == c.Owner || v == c.Approver || c.Delegates.ContainsKey(v);

    /// <summary>Only the approver, or a delegate with closing rights, closes.</summary>
    public static bool CanClose(RenewalCase c, string v) =>
        v == c.Approver || (c.Delegates.TryGetValue(v, out var d) && d.Close);

    /// <summary>The policy key of a clause type (<c>lk</c>).</summary>
    public static string Lk(string type)
    {
        if (LiabRe().IsMatch(type))
        {
            return "liab";
        }

        if (PaymentRe().IsMatch(type))
        {
            return "payment";
        }

        if (UpliftRe().IsMatch(type))
        {
            return "uplift";
        }

        if (RenewRe().IsMatch(type))
        {
            return "renew";
        }

        return "c_" + NonWord().Replace(type.ToLowerInvariant(), string.Empty);
    }

    public static IReadOnlyList<CaseClauseRef> Clauses(CaseContext ctx)
    {
        var list = new List<CaseClauseRef>();
        for (var i = 0; i < ctx.Contract.Push.Count; i++)
        {
            var x = ctx.Contract.Push[i];
            list.Add(new CaseClauseRef("p" + CaseFormat.Js(i), Lk(x.Type), x.Type, x.Norm, x.Ask, x.Src, "push"));
        }

        for (var i = 0; i < ctx.Contract.Raise.Count; i++)
        {
            var x = ctx.Contract.Raise[i];
            list.Add(new CaseClauseRef("r" + CaseFormat.Js(i), Lk(x.Type), x.Type, x.Norm, x.Ask, x.Src, "raise"));
        }

        return list;
    }

    /// <summary><c>parseFloat(C.uplift)</c>.</summary>
    public static double UpliftPct(CaseContext ctx)
    {
        var m = LeadingNumber().Match(ctx.Contract.Uplift ?? string.Empty);
        return m.Success ? double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture) : double.NaN;
    }

    public static bool HasUplift(CaseContext ctx) => UpliftPct(ctx) > 0;

    /// <summary>Company policy rules (<c>POL</c>): liab (binding, min 24), renew (binding), payment (guidance).</summary>
    public static readonly IReadOnlyList<(string Key, bool Bind, CaseText Text)> Policy =
    [
        ("liab", true, Tx.B("Liability cap: at least 24 months of fees", "Limite di responsabilità: almeno 24 mesi di canone")),
        ("renew", true, Tx.B("No auto-renewal on software contracts above CHF 100k", "Niente rinnovo automatico sui contratti software oltre CHF 100k")),
        ("payment", false, Tx.B("Payment terms: quarterly or Net 60 preferred", "Pagamenti: preferibile trimestrale o Net 60")),
    ];

    public static (string Key, bool Bind, CaseText Text)? PolicyFor(string lk)
    {
        foreach (var p in Policy)
        {
            if (p.Key == lk)
            {
                return p;
            }
        }

        return null;
    }

    [GeneratedRegex("liab", RegexOptions.IgnoreCase)]
    private static partial Regex LiabRe();

    [GeneratedRegex("payment", RegexOptions.IgnoreCase)]
    private static partial Regex PaymentRe();

    [GeneratedRegex("increase|uplift|protection", RegexOptions.IgnoreCase)]
    private static partial Regex UpliftRe();

    [GeneratedRegex("renew", RegexOptions.IgnoreCase)]
    private static partial Regex RenewRe();

    [GeneratedRegex(@"\W+", RegexOptions.ECMAScript)]
    private static partial Regex NonWord();

    [GeneratedRegex(@"^\s*[-+]?(\d+\.?\d*|\.\d+)")]
    private static partial Regex LeadingNumber();
}
