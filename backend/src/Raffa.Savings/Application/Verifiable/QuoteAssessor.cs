using System.Globalization;
using System.Text.RegularExpressions;
using Raffa.SharedKernel.Text;

namespace Raffa.Savings.Application.Verifiable;

/// <summary>
/// Message construction for the quote check and the savings report: <c>B(en, it, vars)</c> mirrors the
/// prototype's <c>S(en,it)</c>; the English template is the id, the Italian side feeds the generated
/// catalogue <c>web/src/i18n/it/quote4.ts</c> (not used at runtime).
/// </summary>
internal static class Tx
{
#pragma warning disable IDE0060 // `it` is read by the catalogue generator, not at runtime.
    public static CaseText B(string en, string it, params (string Name, object? Value)[] vars) => CaseText.Message(en, vars);
#pragma warning restore IDE0060
}

/// <summary>Number formatting of <c>savings-quote-v4.js</c>: <c>k</c>, <c>kr</c>, <c>up</c>, <c>pct</c>, <c>num</c>.</summary>
public static class QuoteFormat
{
    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>JavaScript <c>Math.round</c>.</summary>
    public static double Round(double x) => Math.Floor(x + 0.5);

    /// <summary>"CHF 156k" / "CHF 2.4M".</summary>
    public static string K(double n) =>
        n >= 1e6 ? "CHF " + (n / 1e6).ToString("0.0", CultureInfo.InvariantCulture) + "M" : "CHF " + Js(Round(n / 1000)) + "k";

    /// <summary>"CHF 120–180k".</summary>
    public static string Kr(double a, double b) => "CHF " + Js(Round(a / 1000)) + "–" + Js(Round(b / 1000)) + "k";

    /// <summary>"640,000".</summary>
    public static string Num(double n) => Round(n).ToString("#,##0", CultureInfo.InvariantCulture);

    /// <summary>Unit price: thousands separated from 1,000, otherwise as written ("2.34").</summary>
    public static string Up(double n) => n >= 1000 ? Num(n) : Js(n);

    /// <summary>"+3.2%", "-13%" (one decimal below 10%).</summary>
    public static string Pct(double d) =>
        (d > 0 ? "+" : string.Empty) + (Math.Abs(d) < 10 ? d.ToString("0.0", CultureInfo.InvariantCulture) : Js(Round(d))) + "%";

    public static string Js(double n) => CaseText.JsNumber(n);

    /// <summary>"08 Sep 2026".</summary>
    public static string Date(DateOnly d) =>
        (d.Day < 10 ? "0" : string.Empty) + d.Day.ToString(CultureInfo.InvariantCulture) + " " + Months[d.Month - 1] + " " + d.Year.ToString(CultureInfo.InvariantCulture);

    public static string Month(int month) => Months[month - 1];
}

/// <summary>
/// The no-benchmark quote check — port of <c>SQ.assess</c>. A quote is compared only with the
/// customer's own contract price per line, the target the user set, other offers actually received and
/// the company policy. When nothing is comparable the verdict is <c>nocompare</c> with what is missing —
/// never a guessed verdict.
/// </summary>
public static partial class QuoteAssessor
{
    private static int Mult(QuoteLine l) => l.Unit.Contains("/ mo", StringComparison.Ordinal) ? 12 : 1;

    public static QuoteAssessment Assess(Quote q, AssessOptions? options = null)
    {
        var o = options ?? new AssessOptions();
        var lines = q.Lines.Select(l =>
        {
            var m = Mult(l);
            var a = l.Price * l.Qty * m;
            double? cur = l.Existing is { } ex ? ex * l.Qty * m : null;
            double? d = l.Existing is { } ex2 ? (l.Price / ex2 - 1) * 100 : null;
            var pos = d is null ? "none" : d < -0.5 ? "below" : d > 0.5 ? "above" : "same";
            return new AssessedLine(l, a, cur, d, pos);
        }).ToList();
        double total = 0, curSum = 0;
        var cmpN = 0;
        foreach (var l in lines)
        {
            total += l.Annual;
            if (l.CurrentAnnual is { } c)
            {
                curSum += c;
                cmpN++;
            }
        }

        var hasCur = cmpN > 0;
        var partial = hasCur && cmpN < lines.Count;
        double? curTotal = hasCur ? curSum : null;
        double? own = hasCur ? (total / curSum - 1) * 100 : null;
        var tgt = o.Target is { Kind: not (null or "" or "none") } ? o.Target : q.Target;
        double? targetTotal = null;
        bool? targetMet = null;
        if (tgt is not null && hasCur)
        {
            targetTotal = tgt.Kind switch
            {
                "p5" => curSum * 0.95,
                "p10" => curSum * 0.90,
                "budget" when tgt.Budget is { } b && b != 0 => b,
                _ => null,
            };
            if (targetTotal is { } tt)
            {
                targetMet = total <= tt * 1.001;
            }
        }

        var others = o.Others;
        var gaps = q.Gaps;
        var missing = new List<CaseText>();
        if (!hasCur)
        {
            missing.Add(Tx.B("your current contract price for these lines", "il prezzo del tuo contratto attuale per queste righe"));
        }

        if (tgt is null)
        {
            missing.Add(Tx.B("a target set by you (collected in the renewal chat)", "un obiettivo indicato da te (raccolto nella chat del rinnovo)"));
        }

        if (others.Count == 0)
        {
            missing.Add(Tx.B("other offers for the same need", "altre offerte per la stessa esigenza"));
        }

        var basis = hasCur ? "current" : others.Count > 0 ? "others" : "none";
        var verdict = basis == "none" ? QuoteVerdicts.NoCompare
            : tgt is not null && targetMet == false ? QuoteVerdicts.MissesTarget
            : tgt is not null && targetMet == true ? QuoteVerdicts.MeetsTarget
            : own < -0.5 ? QuoteVerdicts.BelowCurrent
            : own > 0.5 ? QuoteVerdicts.AboveCurrent
            : QuoteVerdicts.Same;

        var points = new List<EvidencePoint>();
        var comparisons = new List<LineComparison>();
        foreach (var l in lines)
        {
            if (l.Delta is not { } d)
            {
                continue;
            }

            var src = Tx.B("{label}⟦source⟧", "contratto attuale", ("label", l.Line.ExistingLabel ?? "current contract"));
            points.Add(new EvidencePoint("price", d > 0 ? "alert" : "ok",
                Tx.B("{name}: CHF {price} per {unit} vs CHF {existing} today ({pct})", "{name}: CHF {price} per {unit} contro CHF {existing} oggi ({pct})",
                    ("name", l.Line.Name), ("price", QuoteFormat.Up(l.Line.Price)), ("unit", l.Line.Unit), ("existing", QuoteFormat.Up(l.Line.Existing!.Value)), ("pct", QuoteFormat.Pct(d))),
                src));
            comparisons.Add(new LineComparison(l.Line.Name, l.Line.Price, l.Line.Existing!.Value, d, l.Line.Unit, src));
        }

        var tLabel = tgt?.Label ?? tgt?.Kind;
        if (tgt is not null && targetTotal is { } tt2)
        {
            points.Add(new EvidencePoint("target", targetMet == true ? "ok" : "alert",
                targetMet == true
                    ? Tx.B("Meets your target ({target}): {total} ≤ {targetTotal} per year", "Rispetta il tuo obiettivo ({target}): {total} ≤ {targetTotal} l’anno", ("target", tLabel), ("total", QuoteFormat.K(total)), ("targetTotal", QuoteFormat.K(tt2)))
                    : Tx.B("Misses your target ({target}): {total} vs {targetTotal} per year", "Manca il tuo obiettivo ({target}): {total} contro {targetTotal} l’anno", ("target", tLabel), ("total", QuoteFormat.K(total)), ("targetTotal", QuoteFormat.K(tt2))),
                Tx.B("your objective, not a benchmark", "il tuo obiettivo, non un benchmark")));
        }
        else if (tgt is not null && !hasCur)
        {
            points.Add(new EvidencePoint("target", "neutral",
                Tx.B("Your target ({target}) cannot be measured: no current price for these lines", "Il tuo obiettivo ({target}) non è misurabile: nessun prezzo attuale per queste righe", ("target", tLabel)),
                CaseText.Empty));
        }

        if (!string.IsNullOrEmpty(o.PlanTerm) && !string.IsNullOrEmpty(q.Term) && LeadingInt(q.Term) != o.PlanTerm)
        {
            points.Add(new EvidencePoint("term", "alert",
                Tx.B("Term {term} — your plan asks for {plan} months", "Durata {term} — il tuo piano chiede {plan} mesi", ("term", q.Term), ("plan", o.PlanTerm)),
                Tx.B("renewal plan", "piano di rinnovo")));
        }

        if (o.Policy is not null && q.Stated?.Liability is { } liab && liab.Contains("12", StringComparison.Ordinal))
        {
            points.Add(new EvidencePoint("policy", "alert",
                Tx.B("Liability 12 months — company policy requires at least {min}", "Responsabilità 12 mesi — la policy aziendale richiede almeno {min}", ("min", o.Policy.LiabMin)),
                "Procurement Policy v3.2"));
        }

        if (gaps.Count > 0)
        {
            points.Add(new EvidencePoint("gap", "alert", Tx.B("Not stated: {gaps}", "Non indicato: {gaps}", ("gaps", CaseText.Join("; ", gaps))), Tx.B("the quote itself", "il preventivo stesso")));
        }

        if (o.NoticeDate is { } nd && q.Valid is { } valid && valid >= nd)
        {
            points.Add(new EvidencePoint("validity", "alert",
                Tx.B("Offer valid until {valid} — on or after your notice deadline {notice}", "Offerta valida fino al {valid} — alla pari o dopo la tua scadenza di disdetta {notice}", ("valid", QuoteFormat.Date(valid)), ("notice", QuoteFormat.Date(nd))),
                Tx.B("contract notice clause", "clausola di disdetta")));
        }

        foreach (var x in others)
        {
            points.Add(new EvidencePoint("other", x.Total < total ? "alert" : "ok",
                Tx.B("{name}: {total} / yr for {term} months ({pct} vs this quote)", "{name}: {total} / anno per {term} mesi ({pct} rispetto a questa offerta)",
                    ("name", x.Name), ("total", QuoteFormat.K(x.Total)), ("term", x.Term), ("pct", QuoteFormat.Pct((x.Total / total - 1) * 100))),
                Tx.B("offer received", "offerta ricevuta")));
        }

        return new QuoteAssessment
        {
            Lines = lines,
            Total = total,
            CurTotal = curTotal,
            Partial = partial,
            Own = own,
            Target = tgt,
            TargetTotal = targetTotal,
            TargetMet = targetMet,
            Missing = missing,
            Basis = basis,
            Verdict = verdict,
            Points = points.Take(5).ToList(),
            Gaps = gaps,
            Others = others,
            Evaluable =
            [
                Tx.B("lines, quantities and unit prices", "righe, quantità e prezzi unitari"),
                Tx.B("term and validity", "durata e validità"),
                Tx.B("clauses stated or missing", "clausole indicate o mancanti"),
            ],
            Comparisons = comparisons,
        };
    }

    /// <summary><c>String(parseInt(s, 10))</c>.</summary>
    private static string LeadingInt(string s)
    {
        var m = LeadingIntRe().Match(s);
        return m.Success ? int.Parse(m.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) : "NaN";
    }

    [GeneratedRegex(@"^\s*[-+]?\d+")]
    private static partial Regex LeadingIntRe();
}

/// <summary>
/// The counter-proposal draft for a checked quote — port of <c>SQ.counterEmail</c>. English (emails are
/// English in both languages in the prototype). It is a draft: it never sends anything.
/// </summary>
public static partial class CounterEmail
{
    public static string Build(Quote q, QuoteAssessment a, string by)
    {
        var asks = new List<string>();
        foreach (var l in a.Lines)
        {
            var line = l.Line;
            if (a.Target is not null && a.TargetTotal is { } tt && a.TargetMet != true && line.Existing is { } ex)
            {
                var factor = a.Target.Kind == "p5" ? 0.95 : a.Target.Kind == "p10" ? 0.90 : tt / (a.CurTotal ?? double.NaN);
                var tp = ex * factor;
                asks.Add("• " + line.Name + ": you quoted CHF " + QuoteFormat.Up(line.Price) + " per " + line.Unit + "; our target is CHF " +
                         QuoteFormat.Up(QuoteFormat.Round(tp * 100) / 100) + " or better.");
            }
            else if (a.Target is not null && a.TargetTotal is not null && a.TargetMet != true)
            {
                // The prototype multiplies an undefined current price here (NaN); a line without a current price gets no ask.
            }
            else if (l.Delta is > 0)
            {
                asks.Add("• " + line.Name + ": you quoted CHF " + QuoteFormat.Up(line.Price) + " per " + line.Unit + ", above our current CHF " +
                         QuoteFormat.Up(line.Existing!.Value) + ". We ask you to hold the current price.");
            }
        }

        foreach (var g in a.Gaps)
        {
            var text = NotStated().Replace(g.Render(), string.Empty);
            asks.Add("• Please state in writing: " + text + ".");
        }

        if (asks.Count == 0)
        {
            asks.Add("• Please confirm the quoted prices, term and clauses in a revised Order Form.");
        }

        return "Dear " + q.Supplier + " team,\n\nThank you for proposal " + FileExt().Replace(q.File, string.Empty) +
               ". Before we can proceed we need the following:\n" + string.Join("\n", asks) + "\n\nWe can offer a " + (q.Term ?? "undefined") +
               " commitment in exchange. Please send a revised proposal.\n\nKind regards,\n" + by + "\nProcurement, Helvetia Foods AG";
    }

    [GeneratedRegex(@" not stated$")]
    private static partial Regex NotStated();

    [GeneratedRegex(@"\.(pdf|eml)$")]
    private static partial Regex FileExt();
}
