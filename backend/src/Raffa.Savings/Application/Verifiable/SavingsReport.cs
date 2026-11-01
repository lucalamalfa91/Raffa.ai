namespace Raffa.Savings.Application.Verifiable;

/// <summary>
/// A contractual result already recorded (signed price vs the previous contract, same quantities) —
/// the prototype's <c>SQ.HIST</c> entry. <see cref="Kind"/> price | term | clause is a negotiated result;
/// volume is a quantity reduction and is never counted as negotiation. <see cref="Realized"/> is true
/// only when invoices prove it; <see cref="RealizedAmount"/> is what they show so far.
/// </summary>
public sealed record SavingResult
{
    public string Id { get; init; } = string.Empty;

    public string Supplier { get; init; } = string.Empty;

    public string Contract { get; init; } = string.Empty;

    public string Cat { get; init; } = string.Empty;

    /// <summary>price | term | clause | volume.</summary>
    public string Kind { get; init; } = "price";

    public string Lever { get; init; } = string.Empty;

    public string Owner { get; init; } = string.Empty;

    /// <summary>Annual amount (CHF).</summary>
    public double Amount { get; init; }

    /// <summary>Signature date.</summary>
    public DateOnly When { get; init; }

    public string Evidence { get; init; } = string.Empty;

    public string Note { get; init; } = string.Empty;

    public string Baseline { get; init; } = string.Empty;

    public string Period { get; init; } = string.Empty;

    public string Method { get; init; } = string.Empty;

    public bool Realized { get; init; }

    public string? RealizedEv { get; init; }

    public double? RealizedAmount { get; init; }

    public string? Quote { get; init; }

    /// <summary>Quantity reductions are shown apart: they never count as a negotiation result.</summary>
    public bool CountsAsNegotiation => Kind != "volume";
}

/// <summary>An open objective: a user target applied to a contract's spend — never an estimate.</summary>
public sealed record OpenObjective
{
    public string Supplier { get; init; } = string.Empty;

    public string Cat { get; init; } = string.Empty;

    public string Owner { get; init; } = string.Empty;

    /// <summary>Null = the objective has no amount yet.</summary>
    public double? TargetAmount { get; init; }

    /// <summary>Days to the notice deadline.</summary>
    public int? Days { get; init; }
}

/// <summary>Filters: supplier, category (or categories), owner and period (fy | q | anything else = last 12 months).</summary>
public sealed record SavingsFilter(string? Sup = null, string? Cat = null, IReadOnlyList<string>? Cats = null, string? Owner = null, string? Period = null);

public sealed record SavingsTargets(double? Annual = null, int? NoticeDays = null);

public sealed record SavingsReportInput
{
    public SavingsFilter Filter { get; init; } = new();

    public SavingsTargets Targets { get; init; } = new();

    public IReadOnlyList<SavingResult> Hist { get; init; } = [];

    public IReadOnlyList<OpenObjective> Open { get; init; } = [];

    /// <summary>Analysed spend (for the % of spend KPI).</summary>
    public double Spend { get; init; }

    public DateOnly Today { get; init; }
}

public sealed record SavingsMonth(int Y, int M, bool Future, string Key, string Label, double Amount, int Count, IReadOnlyList<SavingResult> Items);

public sealed record SavingsGroup(string Label, double Contractual, double Realized, double Objectives, int N, double Total);

public sealed record SavingsReportResult
{
    /// <summary>Signed, negotiated results (price, term, clause) in the period.</summary>
    public double Contractual { get; init; }

    /// <summary>The invoice-proven part of <see cref="Contractual"/>.</summary>
    public double Realized { get; init; }

    /// <summary>Quantity reductions — shown apart, not a negotiation result.</summary>
    public double VolumeAmt { get; init; }

    public int Count { get; init; }

    public int VolumeN { get; init; }

    /// <summary>Sum of open objectives (user targets, never estimates).</summary>
    public double Objectives { get; init; }

    /// <summary>Open objectives without an amount.</summary>
    public int UndefinedN { get; init; }

    public double Target { get; init; }

    public double PctTarget { get; init; }

    public double PctSpend { get; init; }

    public required IReadOnlyList<SavingsMonth> Months { get; init; }

    public double Peak { get; init; }

    /// <summary>Top six suppliers by contractual + objectives.</summary>
    public required IReadOnlyList<SavingsGroup> By { get; init; }

    public double MaxBy { get; init; }

    public required IReadOnlyList<SavingResult> Recent { get; init; }

    public required IReadOnlyList<SavingResult> VolumeItems { get; init; }

    public required IReadOnlyList<OpenObjective> Open { get; init; }

    public required IReadOnlyList<OpenObjective> NoticeSoon { get; init; }

    public required IReadOnlyList<string> SupplierOpts { get; init; }

    public required IReadOnlyList<string> CatOpts { get; init; }

    public required IReadOnlyList<string> OwnerOpts { get; init; }
}

/// <summary>
/// The verifiable savings report — port of <c>SQ.report</c>: contractual results (signed), realized
/// (invoices), quantity reductions apart, open objectives. No estimate anywhere.
/// </summary>
public static class SavingsReport
{
    /// <summary>
    /// Contractual result of a price change: (old − new unit price) × contracted quantity × billing
    /// periods per year (12 for monthly units). Same quantities on both sides by construction.
    /// </summary>
    public static double ContractualAnnual(double oldUnitPrice, double newUnitPrice, double quantity, int periodsPerYear = 1) =>
        (oldUnitPrice - newUnitPrice) * quantity * periodsPerYear;

    /// <summary>A quantity reduction: removed units × unchanged unit price (kind = volume, never negotiation).</summary>
    public static double QuantityReduction(double removedUnits, double unitPrice, int periodsPerYear = 1) => removedUnits * unitPrice * periodsPerYear;

    /// <summary>An avoided uplift: uplift % × annual fee (an increase avoided, not a reduction of today's fee).</summary>
    public static double AvoidedUplift(double annualFee, double upliftPct) => annualFee * upliftPct / 100;

    /// <summary><c>inPeriod(when, period, now)</c>: fy = same calendar year, q = same quarter, otherwise the last 12 months.</summary>
    public static bool InPeriod(DateOnly when, string? period, DateOnly now)
    {
        if (period == "fy")
        {
            return when.Year == now.Year;
        }

        if (period == "q")
        {
            var qs = (now.Month - 1) / 3 * 3;
            return when.Year == now.Year && when.Month - 1 >= qs && when.Month - 1 < qs + 3;
        }

        var from = now.AddYears(-1);
        return when > from && when <= now;
    }

    private static List<(int Y, int M, bool Future)> MonthBuckets(string? period, DateOnly now)
    {
        var y = now.Year;
        var m = now.Month - 1;
        var list = new List<(int, int, bool)>();
        if (period == "fy")
        {
            for (var i = 0; i < 12; i++)
            {
                list.Add((y, i, i > m));
            }
        }
        else if (period == "q")
        {
            var qs = m / 3 * 3;
            for (var j = qs; j < qs + 3; j++)
            {
                list.Add((y, j, j > m));
            }
        }
        else
        {
            for (var o = 11; o >= 0; o--)
            {
                var mm = m - o;
                var yy = y;
                while (mm < 0)
                {
                    mm += 12;
                    yy--;
                }

                list.Add((yy, mm, false));
            }
        }

        return list;
    }

    public static SavingsReportResult Build(SavingsReportInput inp)
    {
        var f = inp.Filter;
        var t = inp.Targets;
        bool Match(string supplier, string cat, string owner) =>
            (f.Sup is null || supplier == f.Sup) && (f.Cat is null || cat == f.Cat) && (f.Cats is null || f.Cats.Contains(cat)) && (f.Owner is null || owner == f.Owner);

        var fh = inp.Hist.Where(h => Match(h.Supplier, h.Cat, h.Owner)).Where(h => InPeriod(h.When, f.Period, inp.Today)).OrderByDescending(h => h.When).ToList();
        var neg = fh.Where(h => h.CountsAsNegotiation).ToList();
        var vol = fh.Where(h => !h.CountsAsNegotiation).ToList();
        var contractual = neg.Sum(h => h.Amount);
        var realized = neg.Sum(h => h.Realized ? h.RealizedAmount ?? 0 : 0);
        var volumeAmt = vol.Sum(h => h.Amount);
        var fo = inp.Open.Where(o => Match(o.Supplier, o.Cat, o.Owner)).ToList();
        var objectives = fo.Sum(o => o.TargetAmount ?? 0);
        var undefinedN = fo.Count(o => o.TargetAmount is null);

        var buckets = MonthBuckets(f.Period, inp.Today);
        var months = buckets.Select((b, i) =>
        {
            var items = neg.Where(h => h.When.Year == b.Y && h.When.Month - 1 == b.M).ToList();
            var label = QuoteFormat.Month(b.M + 1) + (b.M == 0 || i == 0 ? " " + (b.Y % 100).ToString("00", System.Globalization.CultureInfo.InvariantCulture) : string.Empty);
            return new SavingsMonth(b.Y, b.M, b.Future, b.Y + "-" + b.M, label, items.Sum(h => h.Amount), items.Count, items);
        }).ToList();
        var peak = months.Aggregate(0.0, (mx, b) => Math.Max(mx, b.Amount));

        var groups = new List<(string Label, double C, double R, double O, int N)>();
        void G(string label, double c, double r, double o)
        {
            var i = groups.FindIndex(x => x.Label == label);
            if (i < 0)
            {
                groups.Add((label, c, r, o, 1));
            }
            else
            {
                var x = groups[i];
                groups[i] = (label, x.C + c, x.R + r, x.O + o, x.N + 1);
            }
        }

        foreach (var h in neg)
        {
            G(h.Supplier, h.Amount, h.Realized ? h.RealizedAmount ?? 0 : 0, 0);
        }

        foreach (var o in fo)
        {
            G(o.Supplier, 0, 0, o.TargetAmount ?? 0);
        }

        var by = groups.Select(x => new SavingsGroup(x.Label, x.C, x.R, x.O, x.N, x.C + x.O)).OrderByDescending(x => x.Total).Take(6).ToList();
        var maxBy = by.Aggregate(0.0, (mx, x) => Math.Max(mx, x.Total));

        static IReadOnlyList<string> Opts(IEnumerable<string> values) =>
            values.Where(v => !string.IsNullOrEmpty(v)).Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToList();

        var noticeDays = t.NoticeDays is { } nd && nd != 0 ? nd : 45;
        return new SavingsReportResult
        {
            Contractual = contractual,
            Realized = realized,
            VolumeAmt = volumeAmt,
            Count = neg.Count,
            VolumeN = vol.Count,
            Objectives = objectives,
            UndefinedN = undefinedN,
            Target = t.Annual ?? 0,
            PctTarget = t.Annual is { } ta && ta != 0 ? contractual / ta : 0,
            PctSpend = inp.Spend != 0 ? contractual / inp.Spend : 0,
            Months = months,
            Peak = peak,
            By = by,
            MaxBy = maxBy,
            Recent = neg,
            VolumeItems = vol,
            Open = fo.OrderBy(o => o.Days ?? 9e9).ToList(),
            NoticeSoon = fo.Where(o => o.Days is { } d && d >= 0 && d <= noticeDays).ToList(),
            SupplierOpts = Opts(inp.Hist.Select(h => h.Supplier).Concat(inp.Open.Select(o => o.Supplier))),
            CatOpts = Opts(inp.Hist.Select(h => h.Cat).Concat(inp.Open.Select(o => o.Cat))),
            OwnerOpts = Opts(inp.Hist.Select(h => h.Owner).Concat(inp.Open.Select(o => o.Owner))),
        };
    }
}
