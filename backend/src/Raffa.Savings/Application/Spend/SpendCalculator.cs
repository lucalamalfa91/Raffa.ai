using System.Globalization;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Savings;

namespace Raffa.Savings.Application.Spend;

/// <summary>
/// Spesa e risparmi, the pure half: puts each validated contract's value in the fiscal quarters it
/// covers. Deterministic and total; it never estimates.
/// <list type="bullet">
/// <item>Documented terms (the contract, then recorded renewals) give the value of the days they
/// state; where two overlap, the one recorded later covers its days. A day with no documented term
/// counts nothing: history is never reconstructed.</item>
/// <item>An annual value is spread over each contract year by its own length (365 or 366 days), so a
/// contract year always sums to its annual value; a total value is spread over its term's days.</item>
/// <item>After the last documented term the renewal clause decides: a renewal whose notice deadline
/// passed is binding (<see cref="SpendSegmentKind.RolledOver"/>), a later one follows unless notice
/// is given (<see cref="SpendSegmentKind.AutoRenewal"/>); without automatic renewal the spend is
/// <see cref="SpendSegmentKind.ToDecide"/> while the contract still runs, and nothing once it ended.
/// Every such stretch is at the last documented price, flagged.</item>
/// <item>Another currency is converted only at the admin's budget rate; without one the contract is
/// left out and listed, as is one without dates, value or (on cash basis) billing period.</item>
/// </list>
/// </summary>
public static class SpendCalculator
{
    /// <summary>Widest report: ten fiscal years.</summary>
    public const int MaxFiscalYears = 10;

    /// <summary>Source of a stretch the renewal clause produced.</summary>
    public const string RenewalClauseSource = "renewalClause";

    /// <summary>Source of a stretch valued at the last term because nothing renews it.</summary>
    public const string LastTermSource = "lastTerm";

    /// <summary>The fiscal year <paramref name="day"/> falls in, named by the calendar year it starts in.</summary>
    public static int FiscalYearOf(DateOnly day, int startMonth) => day.Month >= startMonth ? day.Year : day.Year - 1;

    public static (DateOnly From, DateOnly To) FiscalYearRange(int fiscalYear, int startMonth)
    {
        var from = new DateOnly(fiscalYear, startMonth, 1);
        return (from, from.AddYears(1).AddDays(-1));
    }

    /// <summary>"FY 2026" for a calendar fiscal year, "FY 2026/27" for one that starts in another month.</summary>
    public static string FiscalYearLabel(int fiscalYear, int startMonth) => "FY " + YearLabel(fiscalYear, startMonth);

    /// <summary>The four fiscal quarters of each fiscal year from <paramref name="fromFiscalYear"/> to <paramref name="toFiscalYear"/>.</summary>
    public static IReadOnlyList<SpendPeriod> Quarters(int fromFiscalYear, int toFiscalYear, int startMonth)
    {
        var list = new List<SpendPeriod>();
        for (var fy = fromFiscalYear; fy <= toFiscalYear; fy++)
        {
            var (start, _) = FiscalYearRange(fy, startMonth);
            for (var q = 1; q <= 4; q++)
            {
                list.Add(new SpendPeriod(
                    string.Create(CultureInfo.InvariantCulture, $"{fy}-Q{q}"),
                    fy,
                    q,
                    string.Create(CultureInfo.InvariantCulture, $"Q{q} {YearLabel(fy, startMonth)}"),
                    start.AddMonths(3 * (q - 1)),
                    start.AddMonths(3 * q).AddDays(-1)));
            }
        }

        return list;
    }

    public static SpendReport Build(
        IReadOnlyList<SpendContractInput> contracts,
        WorkspaceFinanceSettings settings,
        DateOnly today,
        int fromFiscalYear,
        int toFiscalYear,
        string basis)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(settings);
        if (!SpendBasis.IsKnown(basis))
        {
            throw new ArgumentException("basis must be accrual or cash.", nameof(basis));
        }

        if (toFiscalYear < fromFiscalYear || toFiscalYear - fromFiscalYear >= MaxFiscalYears)
        {
            throw new ArgumentException($"The report covers 1 to {MaxFiscalYears} fiscal years.", nameof(toFiscalYear));
        }

        var startMonth = settings.FiscalYearStartMonth is >= 1 and <= 12 ? settings.FiscalYearStartMonth : 1;
        var periods = Quarters(fromFiscalYear, toFiscalYear, startMonth);
        var horizon = periods[^1].To;
        var rows = new List<SpendContractRow>();
        var excluded = new List<SpendExclusion>();
        foreach (var c in contracts
                     .OrderBy(c => c.Supplier, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(c => c.ContractId))
        {
            var (row, exclusion) = Contract(c, settings, today, horizon, periods, basis);
            if (exclusion is not null)
            {
                excluded.Add(exclusion);
            }
            else
            {
                rows.Add(row!);
            }
        }

        var fiscalYears = Enumerable.Range(fromFiscalYear, toFiscalYear - fromFiscalYear + 1)
            .Select(fy =>
            {
                var (from, to) = FiscalYearRange(fy, startMonth);
                var documented = rows.Count(r => r.Segments.Any(s => s.Kind == SpendSegmentKind.Documented && s.From <= to && s.To >= from));
                return new SpendFiscalYear(fy, FiscalYearLabel(fy, startMonth), from, to, documented, rows.Count);
            })
            .ToList();

        return new SpendReport(
            settings.ReportingCurrency.Trim().ToUpperInvariant(),
            basis,
            startMonth,
            FiscalYearOf(today, startMonth),
            today,
            periods,
            fiscalYears,
            rows,
            excluded);
    }

    private static string YearLabel(int fiscalYear, int startMonth) =>
        startMonth == 1
            ? fiscalYear.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{fiscalYear}/{(fiscalYear + 1) % 100:00}");

    /// <summary>How a stretch is valued: by contract year from <see cref="Anchor"/> (an annual value),
    /// or by day (a total value); in the reporting currency at <see cref="Rate"/>.</summary>
    private sealed record Price(DateOnly Anchor, decimal? Annual, decimal? Daily, decimal Rate);

    private sealed record Stretch(DateOnly From, DateOnly To, string Kind, string Source, Price? Price, bool PreviousTermPrice);

    private static (SpendContractRow? Row, SpendExclusion? Exclusion) Contract(
        SpendContractInput c, WorkspaceFinanceSettings settings, DateOnly today, DateOnly horizon, IReadOnlyList<SpendPeriod> periods, string basis)
    {
        var pieces = Pieces(c.Terms);
        var currency = Code(pieces.Count > 0 ? pieces[^1].Term.Currency : c.Terms.LastOrDefault()?.Currency ?? settings.ReportingCurrency);
        SpendExclusion Exclude(string reason, string code) => new(c.ContractId, c.Supplier, c.Name, reason, code);
        if (pieces.Count == 0)
        {
            return (null, Exclude(SpendExclusionReason.NoDates, currency));
        }

        if (pieces.All(p => p.Term.AnnualValue is null && p.Term.TotalValue is null))
        {
            return (null, Exclude(SpendExclusionReason.NoValue, currency));
        }

        foreach (var code in pieces.Select(p => Code(p.Term.Currency)).Distinct(StringComparer.Ordinal))
        {
            if (settings.RateFor(code) is null)
            {
                return (null, Exclude(SpendExclusionReason.NoRate, code));
            }
        }

        int? step = null;
        if (basis == SpendBasis.Cash)
        {
            step = BillingPeriods.Months(c.BillingPeriod);
            if (step is null)
            {
                return (null, Exclude(SpendExclusionReason.NoBillingPeriod, currency));
            }
        }

        var notes = new List<string>();
        void Note(string note)
        {
            if (!notes.Contains(note))
            {
                notes.Add(note);
            }
        }

        var stretches = new List<Stretch>();
        foreach (var (term, from, to) in pieces)
        {
            var rate = settings.RateFor(term.Currency)!.Value;
            Price? price = term.AnnualValue is { } annual
                ? new Price(term.From, annual, null, rate)
                : term.TotalValue is { } total ? new Price(term.From, null, total / Days(term.From, term.To), rate) : null;
            if (price is null)
            {
                Note(SpendNote.PartialValue);
            }

            stretches.Add(new Stretch(from, to, SpendSegmentKind.Documented, term.Source, price, false));
        }

        var last = stretches[^1];
        var lastPrice = stretches.Last(s => s.Price is not null).Price!;
        var gap = Math.Max(0, c.NoticeGapDays);
        DateOnly Deadline(DateOnly end) => end.AddDays(-gap);
        if (last.To < horizon)
        {
            var start = last.To.AddDays(1);
            if (c.AutoRenew && c.RenewalTermMonths is { } months && months > 0)
            {
                var previousEnd = last.To;
                for (var cycle = 0; previousEnd < horizon && cycle < ContractTermCalculator.MaxCycles; cycle++)
                {
                    var from = previousEnd.AddDays(1);
                    var to = previousEnd.AddMonths(months);
                    var kind = Deadline(previousEnd) < today ? SpendSegmentKind.RolledOver : SpendSegmentKind.AutoRenewal;
                    stretches.Add(new Stretch(from, to, kind, RenewalClauseSource, lastPrice with { Anchor = from }, true));
                    previousEnd = to;
                }

                Note(SpendNote.PreviousTermPrice);
            }
            else if (c.AutoRenew)
            {
                // It renews by itself, but for how long was not read: not claimed as binding.
                stretches.Add(new Stretch(start, horizon, SpendSegmentKind.AutoRenewal, RenewalClauseSource, lastPrice with { Anchor = start }, true));
                Note(SpendNote.RenewalTermUnknown);
                Note(SpendNote.PreviousTermPrice);
            }
            else if (last.To >= today)
            {
                stretches.Add(new Stretch(start, horizon, SpendSegmentKind.ToDecide, LastTermSource, lastPrice with { Anchor = start }, true));
                Note(SpendNote.PreviousTermPrice);
            }
            else
            {
                Note(SpendNote.Expired);
            }
        }

        var n = periods.Count;
        var actual = new decimal[n];
        var committed = new decimal[n];
        var autoRenewal = new decimal[n];
        var toDecide = new decimal[n];
        void Put(Stretch s, int i, DateOnly when, decimal value)
        {
            switch (s.Kind)
            {
                case SpendSegmentKind.Documented or SpendSegmentKind.RolledOver:
                    (when <= today ? actual : committed)[i] += value;
                    break;
                case SpendSegmentKind.AutoRenewal:
                    autoRenewal[i] += value;
                    break;
                default:
                    toDecide[i] += value;
                    break;
            }
        }

        foreach (var s in stretches)
        {
            if (s.Price is not { } price)
            {
                continue;
            }

            if (step is { } months)
            {
                // Cash: each billing period's value on the day it starts.
                for (var k = 0; ; k++)
                {
                    var invoice = s.From.AddMonths(k * months);
                    if (invoice > s.To || invoice > horizon)
                    {
                        break;
                    }

                    var coversTo = Min(s.From.AddMonths((k + 1) * months).AddDays(-1), s.To);
                    var i = PeriodIndex(periods, invoice);
                    if (i >= 0)
                    {
                        Put(s, i, invoice, Amount(price, invoice, coversTo));
                    }
                }

                continue;
            }

            for (var i = 0; i < n; i++)
            {
                var lo = Max(periods[i].From, s.From);
                var hi = Min(periods[i].To, s.To);
                if (hi < lo)
                {
                    continue;
                }

                if (s.Kind is SpendSegmentKind.Documented or SpendSegmentKind.RolledOver)
                {
                    actual[i] += Amount(price, lo, Min(hi, today));
                    committed[i] += Amount(price, Max(lo, today.AddDays(1)), hi);
                }
                else
                {
                    Put(s, i, lo, Amount(price, lo, hi));
                }
            }
        }

        // The next decision: on the last documented term, or on the renewal that is binding now.
        var binding = new List<Stretch> { last };
        binding.AddRange(stretches.Where(s => s.Kind == SpendSegmentKind.RolledOver));
        DateOnly? decideBy = null;
        DateOnly? endsOn = null;
        foreach (var s in binding)
        {
            if (Deadline(s.To) >= today)
            {
                decideBy = Deadline(s.To);
                endsOn = s.To;
                break;
            }
        }

        if (decideBy is null && binding[^1].To >= today)
        {
            endsOn = binding[^1].To;
        }

        var row = new SpendContractRow(
            c.ContractId,
            c.Supplier,
            c.Name,
            c.Category ?? "",
            currency,
            settings.RateFor(currency)!.Value,
            c.BillingPeriod,
            Round(actual),
            Round(committed),
            Round(autoRenewal),
            Round(toDecide),
            stretches.Select(s => new SpendSegment(
                s.From,
                s.To,
                s.Kind,
                s.Source,
                s.Price?.Annual is { } a ? Math.Round(a * s.Price.Rate, 2, MidpointRounding.AwayFromZero) : null,
                s.PreviousTermPrice)).ToList(),
            new SpendDecision(decideBy, endsOn, c.AutoRenew, c.RenewalTermMonths, c.Uplift ?? ""),
            notes);
        return (row, null);
    }

    /// <summary>The documented terms as non-overlapping stretches: each day belongs to the term recorded
    /// last among those that cover it; a day no term covers belongs to none.</summary>
    private static List<(DocumentedContractTerm Term, DateOnly From, DateOnly To)> Pieces(IReadOnlyList<DocumentedContractTerm> terms)
    {
        var valid = terms.Where(t => t.To >= t.From).ToList();
        var bounds = valid.SelectMany(t => new[] { t.From, t.To.AddDays(1) }).Distinct().Order().ToList();
        var pieces = new List<(DocumentedContractTerm Term, DateOnly From, DateOnly To)>();
        for (var i = 0; i < bounds.Count - 1; i++)
        {
            var from = bounds[i];
            var to = bounds[i + 1].AddDays(-1);
            var cover = valid
                .Where(t => t.From <= from && t.To >= to)
                .OrderByDescending(t => t.RecordedAt)
                .ThenByDescending(t => t.From)
                .FirstOrDefault();
            if (cover is null)
            {
                continue;
            }

            if (pieces.Count > 0 && ReferenceEquals(pieces[^1].Term, cover) && pieces[^1].To.AddDays(1) == from)
            {
                pieces[^1] = (cover, pieces[^1].From, to);
            }
            else
            {
                pieces.Add((cover, from, to));
            }
        }

        return pieces;
    }

    /// <summary>The value of the days from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static decimal Amount(Price price, DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            return 0m;
        }

        if (price.Annual is { } annual)
        {
            var sum = 0m;
            for (var k = Math.Max(0, from.Year - price.Anchor.Year - 1); ; k++)
            {
                var yearStart = price.Anchor.AddYears(k);
                if (yearStart > to)
                {
                    break;
                }

                var yearEnd = price.Anchor.AddYears(k + 1).AddDays(-1);
                var lo = Max(from, yearStart);
                var hi = Min(to, yearEnd);
                if (hi >= lo)
                {
                    sum += annual * Days(lo, hi) / Days(yearStart, yearEnd);
                }
            }

            return sum * price.Rate;
        }

        return price.Daily is { } daily ? daily * Days(from, to) * price.Rate : 0m;
    }

    private static int PeriodIndex(IReadOnlyList<SpendPeriod> periods, DateOnly day)
    {
        for (var i = 0; i < periods.Count; i++)
        {
            if (periods[i].From <= day && day <= periods[i].To)
            {
                return i;
            }
        }

        return -1;
    }

    private static decimal Days(DateOnly from, DateOnly to) => to.DayNumber - from.DayNumber + 1;

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static decimal[] Round(decimal[] values) =>
        values.Select(v => Math.Round(v, 2, MidpointRounding.AwayFromZero)).ToArray();

    private static string Code(string? currency) => (currency ?? "").Trim().ToUpperInvariant();
}
