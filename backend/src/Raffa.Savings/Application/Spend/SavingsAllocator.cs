using Raffa.SharedKernel.Savings;

namespace Raffa.Savings.Application.Spend;

/// <summary>
/// Spesa e risparmi, the savings layer: puts the recorded results and the cases' objectives in the
/// same fiscal quarters as the spend, in the reporting currency. Pure; never estimates.
/// <list type="bullet">
/// <item>A signed result counts its annual value over the days of its stated period (each contract
/// year by its own length, as the spend); a result without a period stays in the register, not in
/// time. A quantity reduction is valued the same way but kept apart: it is not a saving.</item>
/// <item>What invoices prove counts in the period of the invoiced month.</item>
/// <item>An open case's objective (the user's target) counts over the term the case negotiates: from
/// the day after the current term ends, for the renewal term (12 months when it was not read,
/// flagged). A percent target applies to the contract's current annual value.</item>
/// <item>A currency without the admin's budget rate is listed, never converted.</item>
/// </list>
/// </summary>
public static class SavingsAllocator
{
    private sealed class Buckets(int n)
    {
        public decimal[] Contractual { get; } = new decimal[n];
        public decimal[] Realized { get; } = new decimal[n];
        public decimal[] Quantity { get; } = new decimal[n];
        public decimal[] Objective { get; } = new decimal[n];
    }

    public static SpendReport Apply(
        SpendReport report,
        IReadOnlyList<SavingInput> savings,
        IReadOnlyList<CaseObjectiveInfo> objectives,
        WorkspaceFinanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(savings);
        ArgumentNullException.ThrowIfNull(objectives);
        ArgumentNullException.ThrowIfNull(settings);

        var periods = report.Periods;
        var n = periods.Count;
        var buckets = report.Contracts.ToDictionary(r => r.ContractId, _ => new Buckets(n));
        var other = new Buckets(n);

        void Accrue(decimal[] into, DateOnly anchor, decimal annual, DateOnly from, DateOnly to, decimal rate)
        {
            for (var i = 0; i < n; i++)
            {
                var lo = periods[i].From > from ? periods[i].From : from;
                var hi = periods[i].To < to ? periods[i].To : to;
                if (hi >= lo)
                {
                    into[i] += SpendCalculator.AnnualAccrual(anchor, annual, lo, hi) * rate;
                }
            }
        }

        var entries = new List<SpendSavingEntry>();
        foreach (var s in savings.OrderByDescending(x => x.SignedOn).ThenBy(x => x.Supplier, StringComparer.OrdinalIgnoreCase))
        {
            var rate = settings.RateFor(s.Currency);
            var period = s.PeriodFrom is { } pf && s.PeriodTo is { } pt && pt >= pf ? (From: pf, To: pt) : ((DateOnly From, DateOnly To)?)null;
            var target = s.ContractId is { } cid && buckets.TryGetValue(cid, out var b) ? b : other;
            if (rate is { } r)
            {
                if (period is { } p)
                {
                    Accrue(s.CountsAsNegotiation ? target.Contractual : target.Quantity, p.From, s.AmountAnnual, p.From, p.To, r);
                }

                if (s.CountsAsNegotiation)
                {
                    foreach (var z in s.Realizations)
                    {
                        var i = SpendCalculator.PeriodOf(periods, z.InvoiceMonth);
                        if (i >= 0)
                        {
                            target.Realized[i] += z.Amount * r;
                        }
                    }
                }
            }

            var last = s.Realizations.Count > 0 ? s.Realizations.Max(z => z.InvoiceMonth) : (DateOnly?)null;
            entries.Add(new SpendSavingEntry(
                s.Id,
                s.ContractId,
                s.Supplier,
                s.Contract,
                s.Kind,
                s.CountsAsNegotiation,
                s.AmountAnnual,
                Code(s.Currency),
                rate is { } ar ? Round(s.AmountAnnual * ar) : null,
                period?.From,
                period?.To,
                s.SignedOn,
                s.Note,
                s.Baseline,
                s.Period,
                s.Method,
                s.Evidence,
                rate is { } rr ? Round(s.Realizations.Sum(z => z.Amount) * rr) : 0m,
                last?.AddMonths(1).AddDays(-1),
                s.Realizations.Count,
                rate is null ? SavingExclusionReason.NoRate : period is null ? SavingExclusionReason.NoPeriod : null));
        }

        var rows = report.Contracts.ToDictionary(r => r.ContractId);
        var objectiveEntries = new List<SpendObjectiveEntry>();
        foreach (var o in objectives.Where(x => x.IsOpen).GroupBy(x => x.ContractId).Select(g => g.Last()))
        {
            if (!rows.TryGetValue(o.ContractId, out var row))
            {
                continue;
            }

            var pct = o.TargetPercent > 0 ? o.TargetPercent : o.Pct;
            var current = row.Segments.FirstOrDefault(g => g.From <= report.Today && g.To >= report.Today)?.AnnualValue
                ?? row.Segments.LastOrDefault(g => g.Kind == SpendSegmentKind.Documented)?.AnnualValue;
            // A custom amount is in the contract's currency (the case works on its facts); the
            // segment values are already in the reporting currency.
            decimal? annualTarget = o.Amount is > 0 ? Math.Round(o.Amount.Value * row.Rate, 2, MidpointRounding.AwayFromZero)
                : pct is > 0 && current is { } c ? Math.Round(c * pct.Value / 100m, 2, MidpointRounding.AwayFromZero)
                : null;
            var assumed = row.Decision.RenewalTermMonths is not > 0;
            DateOnly? from = row.Decision.EndsOn?.AddDays(1);
            DateOnly? to = row.Decision.EndsOn?.AddMonths(row.Decision.RenewalTermMonths is > 0 ? row.Decision.RenewalTermMonths.Value : 12);
            string? excluded = null;
            if (annualTarget is null && (pct is > 0 || o.Amount is > 0))
            {
                excluded = SavingExclusionReason.NoBase; // a percent of a value the contract does not state
            }
            else if (annualTarget is > 0 && from is null)
            {
                excluded = SavingExclusionReason.NoBase;
            }

            if (annualTarget is { } t && t > 0 && from is { } f && to is { } e)
            {
                Accrue(buckets[row.ContractId].Objective, f, t, f, e, 1m);
            }

            objectiveEntries.Add(new SpendObjectiveEntry(
                o.ContractId, o.CaseId, row.Supplier, o.Kind, pct, annualTarget, from, to, o.OwnerName, assumed && from is not null, excluded));
        }

        return report with
        {
            Contracts = report.Contracts.Select(r =>
            {
                var b = buckets[r.ContractId];
                return r with
                {
                    SavingsContractual = Round(b.Contractual),
                    SavingsRealized = Round(b.Realized),
                    QuantityReduction = Round(b.Quantity),
                    Objective = Round(b.Objective),
                };
            }).ToList(),
            Savings = entries,
            Objectives = objectiveEntries.OrderBy(x => x.From ?? DateOnly.MaxValue).ThenBy(x => x.Supplier, StringComparer.OrdinalIgnoreCase).ToList(),
            OtherSavings = new SpendOtherSavings(Round(other.Contractual), Round(other.Realized), Round(other.Quantity)),
        };
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static decimal[] Round(decimal[] values) => values.Select(Round).ToArray();

    private static string Code(string? currency) => (currency ?? "").Trim().ToUpperInvariant();
}
