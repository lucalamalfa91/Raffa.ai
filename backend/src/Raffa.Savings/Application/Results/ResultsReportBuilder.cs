namespace Raffa.Savings.Application.Results;

/// <summary>
/// Port of <c>SQ.report()</c> / <c>SQ.inPeriod()</c> / <c>SQ.months()</c>
/// (reference/savings-quote-v4.js) plus the derived numbers of the prototype's Risultati
/// (<c>renderVals()</c> → <c>rs</c>): contractual results (negotiated, signed), realized (invoices),
/// quantity reductions (apart) and open objectives (user targets, never estimates). Pure.
/// </summary>
public static class ResultsReportBuilder
{
    public const string PeriodFiscalYear = "fy";
    public const string PeriodQuarter = "q";
    public const string PeriodTwelveMonths = "12m";

    private static readonly string[] MonthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    public static string NormalizePeriod(string? period) => period switch
    {
        PeriodQuarter => PeriodQuarter,
        PeriodTwelveMonths => PeriodTwelveMonths,
        _ => PeriodFiscalYear,
    };

    /// <summary><c>SQ.inPeriod(when, period, now)</c>.</summary>
    public static bool InPeriod(DateOnly when, string period, DateOnly today)
    {
        switch (NormalizePeriod(period))
        {
            case PeriodFiscalYear:
                return when.Year == today.Year;
            case PeriodQuarter:
                var quarterStart = (today.Month - 1) / 3 * 3;
                return when.Year == today.Year && when.Month - 1 >= quarterStart && when.Month - 1 < quarterStart + 3;
            default:
                var from = today.AddYears(-1);
                return when > from && when <= today;
        }
    }

    /// <summary><c>SQ.months(period, now)</c>: the bars of "Quando sono stati firmati".</summary>
    public static IReadOnlyList<(int Year, int Month, bool Future, string Label)> Months(string period, DateOnly today)
    {
        var buckets = new List<(int Year, int Month, bool Future)>();
        var y = today.Year;
        var m = today.Month - 1;
        switch (NormalizePeriod(period))
        {
            case PeriodFiscalYear:
                for (var i = 0; i < 12; i++) buckets.Add((y, i, i > m));
                break;
            case PeriodQuarter:
                var qs = m / 3 * 3;
                for (var j = qs; j < qs + 3; j++) buckets.Add((y, j, j > m));
                break;
            default:
                for (var o = 11; o >= 0; o--)
                {
                    var mm = m - o;
                    var yy = y;
                    while (mm < 0)
                    {
                        mm += 12;
                        yy--;
                    }

                    buckets.Add((yy, mm, false));
                }

                break;
        }

        return buckets
            .Select((b, index) => (b.Year, b.Month, b.Future,
                MonthNames[b.Month] + (b.Month == 0 || index == 0 ? " " + (b.Year % 100).ToString("00", System.Globalization.CultureInfo.InvariantCulture) : "")))
            .ToList();
    }

    /// <summary>Days from <paramref name="today"/> to a notice deadline (negative once passed).</summary>
    public static int DaysTo(DateOnly? deadline, DateOnly today, int fallback) =>
        deadline is { } d ? d.DayNumber - today.DayNumber : fallback;

    /// <summary><c>Math.round(spend * pct / 100)</c>.</summary>
    public static decimal PercentOf(decimal spend, decimal pct) =>
        Math.Round(spend * pct / 100m, 0, MidpointRounding.AwayFromZero);

    public static ResultsReport Build(
        DateOnly today,
        string? period,
        IReadOnlyList<ResultsReportItem> results,
        IReadOnlyList<ResultsContract> validated,
        IReadOnlyList<Raffa.SharedKernel.Savings.CaseObjectiveInfo> caseObjectives,
        string currency = "CHF")
    {
        var p = NormalizePeriod(period);

        // Cases: the latest row per contract decides (open with or without an objective, or closed).
        var caseByContract = caseObjectives
            .GroupBy(c => c.ContractId)
            .ToDictionary(g => g.Key, g => g.Last());
        bool IsClosed(Guid contractId) => caseByContract.TryGetValue(contractId, out var c) && !c.IsOpen;

        // An expired contract (no automatic renewal, end passed) has no objective left to set.
        var open = validated
            .Where(c => !IsClosed(c.ContractId) && c.TermStatus != "expired")
            .Select(c =>
            {
                caseByContract.TryGetValue(c.ContractId, out var cc);
                var pct = cc?.TargetPercent ?? 0;
                return new ResultsOpenObjective(
                    c.ContractId,
                    cc?.CaseId,
                    c.Supplier,
                    c.Category,
                    cc?.OwnerName,
                    c.Days,
                    c.NoticeDeadline,
                    c.Spend,
                    cc?.Kind,
                    pct,
                    pct > 0 ? PercentOf(c.Spend, pct) : null,
                    cc is not null);
            })
            .ToList();

        // SQ.report(): filter by period, newest first.
        var inPeriod = results
            .Where(r => InPeriod(r.SignedOn, p, today))
            .OrderByDescending(r => r.SignedOn)
            .ToList();
        var negotiated = inPeriod.Where(r => r.Kind != "volume").ToList();
        var volume = inPeriod.Where(r => r.Kind == "volume").ToList();

        var contractual = negotiated.Sum(r => r.Amount);
        var realized = negotiated.Sum(r => r.RealizedAmount);
        var volumeAmount = volume.Sum(r => r.Amount);
        var objectives = open.Sum(o => o.TargetAmount ?? 0m);
        var withoutObjective = open.Count(o => o.TargetAmount is null);

        var months = Months(p, today)
            .Select(b =>
            {
                var items = negotiated.Where(r => r.SignedOn.Year == b.Year && r.SignedOn.Month - 1 == b.Month).ToList();
                return new ResultsMonth(
                    b.Year,
                    b.Month + 1,
                    b.Label,
                    b.Future,
                    items.Sum(i => i.Amount),
                    items.Count,
                    items.Select(i => new ResultsMonthSupplier(i.Supplier, i.Amount)).ToList());
            })
            .ToList();
        var peak = months.Count == 0 ? 0m : months.Max(m => m.Amount);

        // Groups by supplier, in insertion order (results newest first, then open objectives).
        var groups = new List<(string Label, decimal Contractual, decimal Realized, decimal Objectives, int N)>();
        void Add(string label, decimal c, decimal r, decimal o)
        {
            var i = groups.FindIndex(g => g.Label == label);
            if (i < 0)
            {
                groups.Add((label, c, r, o, 1));
            }
            else
            {
                var g = groups[i];
                groups[i] = (label, g.Contractual + c, g.Realized + r, g.Objectives + o, g.N + 1);
            }
        }

        foreach (var r in negotiated) Add(r.Supplier, r.Amount, r.RealizedAmount, 0m);
        foreach (var o in open) Add(o.Supplier, 0m, 0m, o.TargetAmount ?? 0m);
        var bySupplier = groups
            .OrderByDescending(g => g.Contractual + g.Objectives)
            .Take(6)
            .Select(g => new ResultsBySupplier(g.Label, g.Contractual, g.Realized, g.Objectives, g.N))
            .ToList();
        var maxBy = bySupplier.Count == 0 ? 0m : bySupplier.Max(b => b.Contractual + b.Objectives);

        // Levers: price / term / clause (quantity reductions are excluded), biggest first.
        var levers = new List<(string Kind, int Count, decimal Amount)>();
        foreach (var r in negotiated)
        {
            var kind = r.Kind is "price" or "term" or "clause" ? r.Kind : "other";
            var i = levers.FindIndex(l => l.Kind == kind);
            if (i < 0) levers.Add((kind, 1, r.Amount));
            else levers[i] = (kind, levers[i].Count + 1, levers[i].Amount + r.Amount);
        }

        var nextNotice = validated
            .Where(c => !IsClosed(c.ContractId) && c.Days >= 0)
            .OrderBy(c => c.Days)
            .Select(c => c.NoticeDeadline is { } d ? new ResultsNextNotice(c.ContractId, c.Supplier, d, c.Days) : null)
            .FirstOrDefault(n => n is not null);

        var kpis = new ResultsKpis(
            negotiated.Count,
            negotiated.Count > 0 ? contractual / negotiated.Count : null,
            contractual > 0 ? (int)Math.Round(realized / contractual * 100m, MidpointRounding.AwayFromZero) : null,
            realized,
            contractual,
            validated.Sum(c => c.Spend),
            validated.Count,
            objectives,
            open.Count,
            withoutObjective,
            nextNotice);

        return new ResultsReport(
            p,
            today,
            today.Year,
            new ResultsHeadline(contractual, realized, volumeAmount, negotiated.Count, volume.Count, currency),
            kpis,
            months,
            peak,
            bySupplier,
            maxBy,
            levers.OrderByDescending(l => l.Amount).Select(l => new ResultsLever(l.Kind, l.Count, l.Amount)).ToList(),
            negotiated.Select(r => new ResultsProof(r.Id, r.Supplier, r.Amount, r.RealizedAmount)).ToList(),
            open.OrderBy(o => o.Days).ToList(),
            negotiated.Select(r => new ResultsContractualRow(
                r.Id, r.SignedOn, r.Supplier, r.Kind, r.Note, r.Baseline, r.Period, r.Method, r.Evidence,
                r.Amount, r.Proof is not null, r.RealizedAmount, r.Proof)).ToList(),
            volume.Select(r => new ResultsVolumeRow(r.Id, r.SignedOn, r.Supplier, r.Note, r.Amount)).ToList());
    }
}
