namespace Raffa.Quotes.Application.AssessmentV4;

/// <summary>One offer line as the assessment reads it. <see cref="Existing"/> is set only when the
/// customer's current contract states a price for the same thing.</summary>
public sealed record OfferLine(
    string Name, string? Sku, decimal Qty, string Unit, decimal Price, decimal? Existing, string? ExistingLabel);

/// <summary>A text in both languages (the offer's omissions, <c>B(en,it)</c>).</summary>
public sealed record BiText(string En, string It)
{
    public string In(string lang) => lang == "it" ? It : En;
}

/// <summary>The user's target (<c>p5 | p10 | budget | custom | none</c>), never a benchmark.</summary>
public sealed record OfferTarget(string Kind, string? Label, decimal? Budget);

public sealed record OtherOffer(string Name, decimal Total, string Term);

/// <param name="Stated">payment / uplift / renewal / liability / sla → text, null = not stated (insertion order kept).</param>
public sealed record OfferInput(
    string Supplier,
    string File,
    string? Term,
    string? Valid,
    IReadOnlyList<OfferLine> Lines,
    IReadOnlyList<KeyValuePair<string, string?>> Stated,
    IReadOnlyList<BiText> Gaps,
    OfferTarget? Target);

/// <param name="PlanTerm">Months the renewal plan asks for ("12" | "24" | "36"), or null.</param>
/// <param name="NoticeDate">The contract's notice deadline, display form ("18 Oct 2026").</param>
/// <param name="PolicyLiabilityMin">Company policy minimum liability (months) when the case applies it.</param>
public sealed record AssessOptions(
    string Lang,
    OfferTarget? Target,
    string? PlanTerm,
    IReadOnlyList<OtherOffer> Others,
    string? NoticeDate,
    int? PolicyLiabilityMin);

public sealed record AssessedLine(
    OfferLine Line, decimal Annual, decimal? CurrentAnnual, double? Delta, string Pos);

/// <summary>An evidence point: <c>kind</c> price | target | term | policy | gap | validity | other;
/// <c>tone</c> ok | alert | neutral; <c>Src</c> says where it comes from.</summary>
public sealed record EvidencePoint(string Kind, string Tone, string Text, string Src);

public static class QuoteVerdict
{
    public const string NoCompare = "nocompare";
    public const string MissesTarget = "misses_target";
    public const string MeetsTarget = "meets_target";
    public const string BelowCurrent = "below_current";
    public const string AboveCurrent = "above_current";
    public const string Same = "same";
}

public sealed record QuoteAssessment(
    IReadOnlyList<AssessedLine> Lines,
    decimal Total,
    decimal? CurTotal,
    bool Partial,
    double? Own,
    OfferTarget? Target,
    decimal? TargetTotal,
    bool? TargetMet,
    IReadOnlyList<string> Missing,
    string Basis,
    string Verdict,
    IReadOnlyList<EvidencePoint> Points,
    IReadOnlyList<string> Gaps,
    IReadOnlyList<OtherOffer> Others,
    IReadOnlyList<string> Evaluable);

/// <summary>
/// AskRaffa 4.0 quote check without benchmark: a faithful port of <c>SQ.assess</c> and
/// <c>SQ.counterEmail</c> (reference/savings-quote-v4.js). A quote is compared only with the
/// customer's own contract price, the target the user set, other offers actually received, the
/// company policy and the notice date — never a market price. When nothing is comparable the verdict
/// is <see cref="QuoteVerdict.NoCompare"/> with what is missing, never a guess. At most 5 points.
/// </summary>
public static class QuoteAssessorV4
{
    public const int MaxPoints = 5;

    private static decimal Mult(OfferLine l) => l.Unit.Contains("/ mo", StringComparison.Ordinal) ? 12m : 1m;

    public static QuoteAssessment Assess(OfferInput q, AssessOptions o)
    {
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(o);
        var it = o.Lang == "it";
        string S(string en, string itText) => it ? itText : en;

        var lines = q.Lines.Select(l =>
        {
            var m = Mult(l);
            var a = l.Price * l.Qty * m;
            decimal? cur = l.Existing is { } ex ? ex * l.Qty * m : null;
            double? d = l.Existing is { } ex2 && ex2 != 0 ? ((double)(l.Price / ex2) - 1) * 100 : null;
            var pos = d is null ? "none" : d < -0.5 ? "below" : d > 0.5 ? "above" : "same";
            return new AssessedLine(l, a, cur, d, pos);
        }).ToList();

        decimal total = 0, curSum = 0;
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
        decimal? curTotal = hasCur ? curSum : null;
        double? own = hasCur && curSum != 0 ? ((double)(total / curSum) - 1) * 100 : null;

        var tgt = o.Target is { Kind: not null and not "none" } ? o.Target : q.Target;
        decimal? targetTotal = null;
        bool? targetMet = null;
        if (tgt is not null && hasCur)
        {
            targetTotal = tgt.Kind switch
            {
                "p5" => curSum * 0.95m,
                "p10" => curSum * 0.90m,
                "budget" when tgt.Budget is { } b => b,
                _ => null,
            };
            if (targetTotal is { } tt)
            {
                targetMet = total <= tt * 1.001m;
            }
        }

        var others = o.Others;
        var gaps = q.Gaps.Select(g => g.In(o.Lang)).ToList();
        var missing = new List<string>();
        if (!hasCur)
        {
            missing.Add(S("your current contract price for these lines", "il prezzo del tuo contratto attuale per queste righe"));
        }

        if (tgt is null)
        {
            missing.Add(S("a target set by you (collected in the renewal chat)", "un obiettivo indicato da te (raccolto nella chat del rinnovo)"));
        }

        if (others.Count == 0)
        {
            missing.Add(S("other offers for the same need", "altre offerte per la stessa esigenza"));
        }

        var basis = hasCur ? "current" : others.Count > 0 ? "others" : "none";
        var verdict = basis == "none" ? QuoteVerdict.NoCompare
            : tgt is not null && targetMet == false ? QuoteVerdict.MissesTarget
            : tgt is not null && targetMet == true ? QuoteVerdict.MeetsTarget
            : own < -0.5 ? QuoteVerdict.BelowCurrent
            : own > 0.5 ? QuoteVerdict.AboveCurrent
            : QuoteVerdict.Same;

        var points = new List<EvidencePoint>();
        foreach (var l in lines)
        {
            if (l.Delta is { } d && l.Line.Existing is { } ex)
            {
                var name = l.Line.Name;
                points.Add(new EvidencePoint(
                    "price",
                    d > 0 ? "alert" : "ok",
                    S($"{name}: CHF {QuoteFormat.Up(l.Line.Price)} per {l.Line.Unit} vs CHF {QuoteFormat.Up(ex)} today ({QuoteFormat.Pct(d)})",
                      $"{name}: CHF {QuoteFormat.Up(l.Line.Price)} per {l.Line.Unit} contro CHF {QuoteFormat.Up(ex)} oggi ({QuoteFormat.Pct(d)})"),
                    S(l.Line.ExistingLabel ?? "current contract", "contratto attuale")));
            }
        }

        var label = tgt?.Label ?? tgt?.Kind;
        if (tgt is not null && targetTotal is { } tTotal)
        {
            points.Add(new EvidencePoint(
                "target",
                targetMet == true ? "ok" : "alert",
                targetMet == true
                    ? S($"Meets your target ({label}): {QuoteFormat.K(total)} ≤ {QuoteFormat.K(tTotal)} per year",
                        $"Rispetta il tuo obiettivo ({label}): {QuoteFormat.K(total)} ≤ {QuoteFormat.K(tTotal)} l’anno")
                    : S($"Misses your target ({label}): {QuoteFormat.K(total)} vs {QuoteFormat.K(tTotal)} per year",
                        $"Manca il tuo obiettivo ({label}): {QuoteFormat.K(total)} contro {QuoteFormat.K(tTotal)} l’anno"),
                S("your objective, not a benchmark", "il tuo obiettivo, non un benchmark")));
        }
        else if (tgt is not null && !hasCur)
        {
            points.Add(new EvidencePoint(
                "target",
                "neutral",
                S($"Your target ({label}) cannot be measured: no current price for these lines",
                  $"Il tuo obiettivo ({label}) non è misurabile: nessun prezzo attuale per queste righe"),
                ""));
        }

        if (o.PlanTerm is { } planTerm && q.Term is { } term && LeadingInt(term) != planTerm)
        {
            points.Add(new EvidencePoint(
                "term",
                "alert",
                S($"Term {term} — your plan asks for {planTerm} months", $"Durata {term} — il tuo piano chiede {planTerm} mesi"),
                S("renewal plan", "piano di rinnovo")));
        }

        var liability = q.Stated.FirstOrDefault(kv => kv.Key == "liability").Value;
        if (o.PolicyLiabilityMin is { } liabMin && q.Stated.Count > 0 && (liability ?? "").Contains("12", StringComparison.Ordinal))
        {
            points.Add(new EvidencePoint(
                "policy",
                "alert",
                S($"Liability 12 months — company policy requires at least {liabMin}",
                  $"Responsabilità 12 mesi — la policy aziendale richiede almeno {liabMin}"),
                "Procurement Policy v3.2"));
        }

        if (gaps.Count > 0)
        {
            points.Add(new EvidencePoint(
                "gap",
                "alert",
                S("Not stated: " + string.Join("; ", gaps), "Non indicato: " + string.Join("; ", gaps)),
                S("the quote itself", "il preventivo stesso")));
        }

        if (o.NoticeDate is { } nd && QuoteFormat.Pd(q.Valid) is { } valid && QuoteFormat.Pd(nd) is { } notice && valid >= notice)
        {
            points.Add(new EvidencePoint(
                "validity",
                "alert",
                S($"Offer valid until {q.Valid} — on or after your notice deadline {nd}",
                  $"Offerta valida fino al {q.Valid} — alla pari o dopo la tua scadenza di disdetta {nd}"),
                S("contract notice clause", "clausola di disdetta")));
        }

        foreach (var x in others)
        {
            var rel = total == 0 ? 0 : ((double)(x.Total / total) - 1) * 100;
            points.Add(new EvidencePoint(
                "other",
                x.Total < total ? "alert" : "ok",
                S($"{x.Name}: {QuoteFormat.K(x.Total)} / yr for {x.Term} months ({QuoteFormat.Pct(rel)} vs this quote)",
                  $"{x.Name}: {QuoteFormat.K(x.Total)} / anno per {x.Term} mesi ({QuoteFormat.Pct(rel)} rispetto a questa offerta)"),
                S("offer received", "offerta ricevuta")));
        }

        var evaluable = new[]
        {
            S("lines, quantities and unit prices", "righe, quantità e prezzi unitari"),
            S("term and validity", "durata e validità"),
            S("clauses stated or missing", "clausole indicate o mancanti"),
        };

        return new QuoteAssessment(
            lines, total, curTotal, partial, own, tgt, targetTotal, targetMet, missing, basis, verdict,
            points.Take(MaxPoints).ToList(), gaps, others, evaluable);
    }

    /// <summary><c>SQ.counterEmail</c>: the counter-proposal draft (English, as the prototype).</summary>
    public static string CounterEmail(OfferInput q, QuoteAssessment a, string by, string? organisation)
    {
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(a);
        var asks = new List<string>();
        foreach (var l in a.Lines)
        {
            var line = l.Line;
            if (a.Target is not null && a.TargetTotal is { } tt && a.TargetMet != true && line.Existing is { } ex)
            {
                var factor = a.Target.Kind switch
                {
                    "p5" => 0.95m,
                    "p10" => 0.90m,
                    _ => a.CurTotal is { } ct && ct != 0 ? tt / ct : 1m,
                };
                var tp = Math.Round(ex * factor * 100m, MidpointRounding.AwayFromZero) / 100m;
                asks.Add($"• {line.Name}: you quoted CHF {QuoteFormat.Up(line.Price)} per {line.Unit}; our target is CHF {QuoteFormat.Up(tp)} or better.");
            }
            else if (l.Delta is > 0 && line.Existing is { } cur)
            {
                asks.Add($"• {line.Name}: you quoted CHF {QuoteFormat.Up(line.Price)} per {line.Unit}, above our current CHF {QuoteFormat.Up(cur)}. We ask you to hold the current price.");
            }
        }

        foreach (var g in a.Gaps)
        {
            var subject = g.EndsWith(" not stated", StringComparison.Ordinal) ? g[..^" not stated".Length]
                : g.EndsWith(" non indicato", StringComparison.Ordinal) ? g[..^" non indicato".Length]
                : g;
            asks.Add("• Please state in writing: " + subject + ".");
        }

        if (asks.Count == 0)
        {
            asks.Add("• Please confirm the quoted prices, term and clauses in a revised Order Form.");
        }

        var file = q.File.EndsWith(".pdf", StringComparison.Ordinal) || q.File.EndsWith(".eml", StringComparison.Ordinal) ? q.File[..^4] : q.File;
        return $"Dear {q.Supplier} team,\n\nThank you for proposal {file}. Before we can proceed we need the following:\n" +
               string.Join("\n", asks) +
               $"\n\nWe can offer a {q.Term} commitment in exchange. Please send a revised proposal.\n\nKind regards,\n{by}\nProcurement" +
               (string.IsNullOrWhiteSpace(organisation) ? "" : ", " + organisation);
    }

    private static string LeadingInt(string text)
    {
        var digits = new string(text.TrimStart().TakeWhile(char.IsDigit).ToArray());
        return digits.Length == 0 ? "NaN" : int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
