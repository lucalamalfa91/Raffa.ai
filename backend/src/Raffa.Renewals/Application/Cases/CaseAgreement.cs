using System.Globalization;
using System.Text.RegularExpressions;
using Raffa.SharedKernel.Text;
using static Raffa.Renewals.Application.Cases.Tx;

namespace Raffa.Renewals.Application.Cases;

/// <summary>
/// The negotiation's figures as the case actually holds them, outside the demo script
/// (<see cref="ICaseScriptServices.Scripted"/> false): what the supplier's replies proposed
/// (<see cref="RenewalCase.Terms"/>, evidence "reply n"), what the owner asked (plan, limits), what the
/// owner confirmed (<see cref="CaseAgreed"/>), and the new contract value computed from those. Nothing
/// here supplies a default: a point nobody stated stays absent.
/// </summary>
/// <summary>Keys of the values checked against the signed document (<see cref="CaseAgreement.SignedFieldsToCheck"/>).</summary>
public static class CaseSignedFields
{
    public const string Price = "price";
    public const string Qty = "qty";
    public const string Term = "term";
    public const string Annual = "annual";
}

public static partial class CaseAgreement
{
    private static readonly HashSet<string> QtyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "qty", "quantity", "quantities", "volume", "seats", "licenses", "licences", "users", "endpoints", "units",
    };

    private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "price", "term", "uplift", "payment", "liab",
    };

    /// <summary>The first amount in a value as the supplier wrote it: "CHF 76.50 per endpoint" → 76.5,
    /// "2'850" / "2,850" → 2850, "76,50" → 76.5. Null when the text holds no number.</summary>
    public static double? Number(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var m = NumberToken().Match(text);
        return m.Success ? ParseToken(m.Value) : null;
    }

    /// <summary>Every amount in <paramref name="text"/>, read as <see cref="Number"/> reads one.</summary>
    public static IReadOnlyList<double> Numbers(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : NumberToken().Matches(text).Select(m => ParseToken(m.Value)).OfType<double>().ToList();

    private static double? ParseToken(string token)
    {
        var t = token.Replace("'", string.Empty, StringComparison.Ordinal).Replace("’", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal).Replace("\u00a0", string.Empty, StringComparison.Ordinal);
        var lastDot = t.LastIndexOf('.');
        var lastComma = t.LastIndexOf(',');
        if (lastDot >= 0 && lastComma >= 0)
        {
            // Both separators: the last one is the decimal mark.
            t = lastDot > lastComma
                ? t.Replace(",", string.Empty, StringComparison.Ordinal)
                : t.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        }
        else if (lastDot >= 0 || lastComma >= 0)
        {
            var sep = lastDot >= 0 ? '.' : ',';
            var parts = t.Split(sep);
            // "2,850" / "1.250.000": groups of three after the first are thousands; "76,50" / "76.5" is a decimal.
            var thousands = parts.Length > 2 || (parts.Length == 2 && parts[1].Length == 3 && parts[0].Length is >= 1 and <= 3 && parts[0] != "0");
            t = thousands ? string.Concat(parts) : parts[0] + "." + parts[1];
        }

        return double.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>A duration in months: "12 months" → 12, "2 years" / "2 anni" → 24, "24" → 24.</summary>
    public static int? Months(string? text)
    {
        if (Number(text) is not { } n || n <= 0 || n != Math.Floor(n))
        {
            return null;
        }

        var years = YearWord().IsMatch(text!) && !MonthWord().IsMatch(text!);
        return (int)n * (years ? 12 : 1);
    }

    /// <summary>The text counts in years ("1 year", "2 anni").</summary>
    public static bool MentionsYears(string? text) => !string.IsNullOrWhiteSpace(text) && YearWord().IsMatch(text);

    /// <summary>A cap on yearly increases in %: "3%" → 3; "none", "no uplift", "0%", "nessun aumento" → 0.</summary>
    public static int? CapPct(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (Number(text) is { } n && n >= 0)
        {
            return (int)Math.Round(n, MidpointRounding.AwayFromZero);
        }

        return NoneWord().IsMatch(text) ? 0 : null;
    }

    /// <summary>"3%", or "no increase" for 0.</summary>
    public static CaseText CapText(int cap) =>
        cap == 0 ? B("no increase", "nessun aumento") : CaseText.Of(cap.ToString(CultureInfo.InvariantCulture) + "%");

    /// <summary>The supplier offers a lower unit price for 24 months than for 12, or prices only 24 months
    /// while the plan asks for another term: a real price/term trade-off for the owner to decide.</summary>
    public static bool OffersTermTradeOff(RenewalCase c)
    {
        var offered = c.OfferedPrices;
        if (offered is null || !offered.TryGetValue("24", out var p24) || p24 <= 0 || c.Goals.Term == "24")
        {
            return false;
        }

        return !offered.TryGetValue("12", out var p12) || p12 <= 0 || p24 < p12;
    }

    /// <summary>The term (months) the supplier's latest reading proposes; else the plan's term; else null.</summary>
    public static int? OfferedTerm(RenewalCase c, CaseReplyBenchmark? benchmark)
    {
        if (c.Terms.TryGetValue("term", out var t) && t.State is "proposed" or "agreed" && Months(t.Val) is { } m)
        {
            return m;
        }

        if (benchmark?.TermMonths is > 0 and var bm)
        {
            return bm;
        }

        return PlanTerm(c);
    }

    /// <summary>The term the plan asks for, in months; null for a custom term that names no number.</summary>
    public static int? PlanTerm(RenewalCase c) =>
        c.Goals.Term == "custom" ? Months(c.Goals.TermText) : Months(c.Goals.Term);

    /// <summary>The unit price the supplier offered for <paramref name="term"/> months in a reply; null when it named none.</summary>
    public static double? OfferedPrice(RenewalCase c, int term) =>
        c.OfferedPrices is { } offered && offered.TryGetValue(term.ToString(CultureInfo.InvariantCulture), out var p) && p > 0 ? p : null;

    /// <summary>The unit price the supplier's replies name for the agreed position: its offer for the term,
    /// else the price term it proposed.</summary>
    public static double? ProposedPrice(RenewalCase c, int? term) =>
        (term is { } t ? OfferedPrice(c, t) : null)
        ?? (c.Terms.TryGetValue("price", out var pt) && pt.State is "proposed" or "agreed" ? Number(pt.Val) : null);

    private static bool IsQty(string key, CaseTerm t) =>
        QtyKeys.Contains(key) || t.Label.Render().Contains("quant", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The agreed position from the terms the owner just confirmed (state "agreed"). Price and term are
    /// required: when the replies do not state them, <paramref name="missing"/> says which and the result
    /// is null — the case asks for the supplier's written figure instead of assuming today's price.
    /// </summary>
    public static CaseAgreed? FromConfirmedTerms(RenewalCase c, out CaseText? missing)
    {
        ArgumentNullException.ThrowIfNull(c);
        var agreed = c.Terms.Where(kv => kv.Value.State == "agreed").ToList();
        string? Val(string key) => agreed.FirstOrDefault(kv => kv.Key == key).Value?.Val;

        var term = Months(Val("term")) ?? (c.Limits is { Term: > 0 } l ? l.Term : null);
        var price = (term is { } t ? OfferedPrice(c, t) : null) ?? Number(Val("price"));
        if (price is null || term is null)
        {
            missing = (price, term) switch
            {
                (null, null) => B("the unit price and the term", "il prezzo unitario e la durata"),
                (null, _) => B("the unit price", "il prezzo unitario"),
                _ => B("the term", "la durata"),
            };
            return null;
        }

        missing = null;
        var qty = agreed.Where(kv => IsQty(kv.Key, kv.Value)).Select(kv => Number(kv.Value.Val)).FirstOrDefault(n => n is > 0);
        var liab = Months(Val("liab"));
        var payment = Val("payment");
        return new CaseAgreed
        {
            Price = price.Value,
            Term = term.Value,
            Cap = agreed.Any(kv => kv.Key == "uplift") ? CapPct(Val("uplift")) : null,
            Liab = liab,
            Qty = qty,
            Payment = string.IsNullOrWhiteSpace(payment) ? null : payment.Trim(),
            Points = agreed
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value.Val))
                .Select(kv => new CaseAgreedPoint { Key = kv.Key, Label = kv.Value.Label, Val = kv.Value.Val!.Trim(), Ev = kv.Value.Ev ?? string.Empty })
                .ToList(),
        };
    }

    /// <summary>The yearly recurring value of the current contract: its recurring lines, or its stated
    /// annual spend when no line was read.</summary>
    public static double CurrentRecurringAnnual(CaseContext ctx)
    {
        var recurring = ctx.Contract.Products.Where(p => !p.OneOff).ToList();
        return recurring.Count == 0 || recurring.All(p => p.Price <= 0) ? ctx.Contract.Spend : recurring.Sum(p => LineAnnual(p));
    }

    /// <summary>The new contract's yearly value: the priced line at the agreed price and quantity, plus the
    /// other recurring lines as they are. One-off amounts are not renewed.</summary>
    public static double NewAnnual(CaseContext ctx, CasePriceFacts f, double price, double qty)
    {
        var recurring = ctx.Contract.Products.Where(p => !p.OneOff).ToList();
        // The priced line is the first recurring one (CaseRules.Price); every other recurring line runs on as it is.
        var priced = recurring.IndexOf(f.P);
        var others = recurring.Where((_, i) => i != priced).Sum(LineAnnual);
        return qty * price * f.Mult + others;
    }

    private static double LineAnnual(CaseProduct p) => p.Qty * p.Price * (p.Unit.Contains("month", StringComparison.Ordinal) ? 12 : 1);

    /// <summary>The policy's minimum liability cap in months ("at least 24 months of fees").</summary>
    public const int PolicyLiabilityMonths = 24;

    /// <summary>The supplier proposes (or the owner agreed) a liability cap below the policy minimum.</summary>
    public static bool LiabilityBelowPolicy(RenewalCase c) =>
        c.Goals.Policy && c.Terms.TryGetValue("liab", out var lt) && lt.State is "proposed" or "agreed"
        && Months(lt.Val) is { } m && m < PolicyLiabilityMonths;

    /// <summary>The proposed liability cap in months, for the policy banner; null when none was read.</summary>
    public static int? ProposedLiability(RenewalCase c) =>
        c.Terms.TryGetValue("liab", out var lt) ? Months(lt.Val) : null;

    /* ---------- email lines (plain English, like every case email) ---------- */

    /// <summary>"• Unit price: CHF 76.50 per endpoint (reply 1)" for every point the latest reply proposed.</summary>
    public static IReadOnlyList<string> SupplierProposalLines(RenewalCase c)
    {
        var ev = c.Reps.Count == 0 ? null : "reply " + CaseFormat.Js(c.Reps.Count);
        return c.Terms
            .Where(kv => kv.Value.Ev == ev && kv.Value.State == "proposed" && !string.IsNullOrWhiteSpace(kv.Value.Val))
            .Select(kv => "• " + kv.Value.Label.Render() + ": " + kv.Value.Val!.Trim())
            .ToList();
    }

    /// <summary>What the team asks on top of the supplier's proposal, each line a request of ours: the term
    /// the owner decided or the plan asks, the plan's clause asks, and a clarification for every point the
    /// reply left unclear (quoting what the supplier wrote).</summary>
    public static IReadOnlyList<string> OurRequestLines(RenewalCase c, CaseContext ctx, CasePriceFacts f, IReadOnlyList<CaseClauseRef> planClauses)
    {
        var lines = new List<string>();
        var offeredTerm = c.Terms.TryGetValue("term", out var tt) && tt.State == "proposed" ? Months(tt.Val) : null;
        var l = c.Limits;
        if (l is { Term: > 0 } && l.Term != offeredTerm)
        {
            var price = OfferedPrice(c, l.Term);
            lines.Add("• Term: " + CaseFormat.Js(l.Term) + " months" +
                (price is { } p ? ", at the unit price you offered for it (" + CaseRules.Ps(f, p) + ")" : string.Empty) +
                (l.Exit is { } ex && ex != 0 ? ", with an exit option at month " + CaseFormat.Js(ex) : string.Empty));
        }
        else if (l is { Exit: { } exit } && exit != 0)
        {
            lines.Add("• An exit option at month " + CaseFormat.Js(exit));
        }

        foreach (var x in planClauses)
        {
            lines.Add("• " + x.Type + ": " + x.Ask);
        }

        var last = c.Reps.Count == 0 ? null : c.Reps[^1];
        foreach (var r in last?.Rows.Where(r => r.State == "unclear") ?? [])
        {
            lines.Add("• Please clarify " + r.Label.Render().ToLowerInvariant() + ": you wrote “" + r.Said.Render() + "”");
        }

        return lines;
    }

    /// <summary>The agreed points for the Order Form request, one line each; a point nobody agreed is not written.</summary>
    public static IReadOnlyList<string> AgreedLines(CaseAgreed a, CaseContext ctx, CasePriceFacts f)
    {
        var lines = new List<string>
        {
            "• Quantities: " + (a.Qty is { } q ? CaseFormat.Num(q) + " × " + f.P.Name : "unchanged"),
            "• Unit price: " + CaseRules.Ps(f, a.Price),
            "• Term: " + CaseFormat.Js(a.Term) + " months",
        };
        if (a.Cap is { } cap)
        {
            lines.Add(cap == 0 ? "• Uplift: no price increase during the term" : "• Uplift: capped at " + CaseFormat.Js(cap) + "%");
        }

        if (a.Payment is { Length: > 0 } pay)
        {
            lines.Add("• Payment: " + pay);
        }

        if (a.Liab is { } liab)
        {
            lines.Add("• Limitation of liability: " + CaseFormat.Js(liab) + " months of fees");
        }

        foreach (var pt in a.Points.Where(pt => !KnownKeys.Contains(pt.Key) && !QtyKeys.Contains(pt.Key) && !pt.Label.Render().Contains("quant", StringComparison.OrdinalIgnoreCase)))
        {
            lines.Add("• " + pt.Label.Render() + ": " + pt.Val);
        }

        if (OneOffNote(ctx) is { } note)
        {
            lines.Add(note);
        }

        return lines;
    }

    /// <summary>"• One-off items of the current contract (…) are not part of this renewal"; null when there are none.</summary>
    public static string? OneOffNote(CaseContext ctx) =>
        RenewalCaseEngine.OneOffText(ctx) is { Length: > 0 } oneOff
            ? "• One-off items of the current contract, not part of this renewal: " + oneOff
            : null;

    /// <summary>The draft's differences and unread points, from the last verified draft.</summary>
    public static IReadOnlyList<string> DraftCorrectionLines(RenewalCase c)
    {
        var rows = c.Docs.Count == 0 ? [] : c.Docs[^1].Rows;
        var lines = rows
            .Where(r => r.Res == "diff")
            .Select(r => "• " + r.Label.Render() + ": the draft states " + r.Draft.Render() + "; we agreed " + r.Agreed.Render() +
                         (string.IsNullOrWhiteSpace(r.Src) ? string.Empty : " (" + r.Src + ")"))
            .ToList();
        lines.AddRange(rows
            .Where(r => r.Res == "unread")
            .Select(r => "• " + r.Label.Render() + ": we could not read it in the draft" +
                         (string.IsNullOrWhiteSpace(r.Src) ? string.Empty : " (" + r.Src + ")") + " — please send a readable version"));
        return lines;
    }

    /// <summary>
    /// The values that will become the contract's new terms, for the owner to check against the signed
    /// document before validating (no automatic extraction ran on it): unit price, quantity, term and the
    /// yearly value computed from them, each with where it comes from. Every one starts open.
    /// </summary>
    public static CasePipe SignedFieldsToCheck(RenewalCase c, CaseContext ctx)
    {
        var pipe = new CasePipe { Stage = 3, Extracted = false };
        if (c.Agreed is not { } a)
        {
            return pipe;
        }

        var f = CaseRules.Price(ctx);
        var qty = a.Qty ?? f.Qty;
        var file = c.Signed?.File ?? string.Empty;
        CaseText Src(string? ev) => string.IsNullOrEmpty(ev)
            ? B("Check it in {file}", "Verificalo in {file}", ("file", file))
            : B("Agreed in {ev} · check it in {file}", "Concordato in {ev} · verificalo in {file}", ("ev", ev), ("file", file));
        string? Ev(Func<CaseAgreedPoint, bool> match) => a.Points.FirstOrDefault(match)?.Ev;

        pipe.Weak =
        [
            new CaseWeakField { N = CaseSignedFields.Price, Name = B("Unit price", "Prezzo unitario"), Val = CaseRules.Ps(f, a.Price), Src = Src(Ev(x => x.Key == "price")) },
            new CaseWeakField
            {
                N = CaseSignedFields.Qty, Name = B("Quantity", "Quantità"), Val = CaseFormat.Num(qty) + " × " + f.P.Name,
                Src = a.Qty is null ? B("Unchanged from the current contract · check it in {file}", "Invariata rispetto al contratto attuale · verificala in {file}", ("file", file)) : Src(Ev(x => QtyKeys.Contains(x.Key))),
            },
            new CaseWeakField { N = CaseSignedFields.Term, Name = B("Term", "Durata"), Val = CaseFormat.Js(a.Term) + " months", Src = Src(Ev(x => x.Key == "term")) },
            new CaseWeakField
            {
                N = CaseSignedFields.Annual, Name = B("Annual value", "Valore annuo"), Val = CaseFormat.Chf(NewAnnual(ctx, f, a.Price, qty)),
                Src = B("Unit price × quantity of the recurring lines, one-off amounts excluded · check it in {file}", "Prezzo unitario × quantità delle righe ricorrenti, importi una tantum esclusi · verificalo in {file}", ("file", file)),
            },
        ];
        foreach (var w in pipe.Weak)
        {
            w.Conf = null;
        }

        return pipe;
    }

    [GeneratedRegex(@"\d[\d'’\u00a0.,]*\d|\d")]
    private static partial Regex NumberToken();

    [GeneratedRegex(@"\b(year|years|yr|yrs|anno|anni|jahr|jahre|an|ans)\b", RegexOptions.IgnoreCase)]
    private static partial Regex YearWord();

    [GeneratedRegex(@"\b(month|months|mo|mesi|mese|monat|monate|mois)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MonthWord();

    [GeneratedRegex(@"\b(none|no|nessun|nessuno|nessuna|kein|keine|aucun|aucune|without|senza|ohne|sans|fixed|frozen|freeze|bloccat[oa])\b", RegexOptions.IgnoreCase)]
    private static partial Regex NoneWord();
}
