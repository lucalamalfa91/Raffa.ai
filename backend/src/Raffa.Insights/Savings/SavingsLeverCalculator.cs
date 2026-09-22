using System.Globalization;
using System.Text.RegularExpressions;
using Raffa.Insights.Contracts;

namespace Raffa.Insights.Savings;

/// <summary>
/// Pure, deterministic "where is the money" calculator for one contract: every lever it emits is
/// grounded in a contract fact, a priced line's band, a clause or a market deal, and carries the
/// citation keys of those sources. Nothing is estimated from thin air — a contract with no annual
/// spend, no priced lines and no market deals yields only the qualitative levers the facts
/// support (notice timing), never an invented amount. The model narrates; the numbers are these.
/// </summary>
public static class SavingsLeverCalculator
{
    /// <summary>Below this coverage of the target the plan is "not supported".</summary>
    public const decimal StretchCoverageFraction = 0.6m;

    private static readonly Regex UpliftClausePattern = new(
        @"uplift|price\s*increase|increase|aumento|incremento|indicizz\w*|indexation|index-linked|\bcpi\b|escalat\w*|adeguamento|rincar\w*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PercentInTextPattern = new(
        @"(?<num>\d{1,2}(?:[.,]\d{1,2})?)\s?%",
        RegexOptions.Compiled);

    private static readonly Regex TrueDownClausePattern = new(
        @"true[-\s]?down|reduce\s+(the\s+)?(number\s+of\s+)?(seats|licen[cs]es|users|quantit\w*)|ridurre\s+(le\s+)?(licenze|utenti|quantit\w*)|riduzione\s+(delle\s+)?licenze|flex(ibility)?\s+down",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NetDaysPattern = new(
        @"net\s?(?<days>\d{2,3})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static SavingsLeverPlan Compute(SavingsLeverInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var spend = inputs.AnnualSpend is > 0 ? inputs.AnnualSpend : null;
        var targetAmount = inputs.TargetAmount;
        if (targetAmount is null && inputs.TargetPercent is { } pct && spend is { } spendForPct)
        {
            targetAmount = Money(spendForPct * pct / 100m);
        }

        decimal? targetPercent = inputs.TargetPercent;
        if (targetPercent is null && targetAmount is { } t && spend is { } s && s > 0)
        {
            targetPercent = Percent(t / s * 100m);
        }

        var levers = new List<SavingsLever>();
        var sameCurrencyDeals = inputs.MarketDeals.Where(d => string.Equals(d.Currency, inputs.Currency, StringComparison.OrdinalIgnoreCase)).ToList();
        var anyDeals = inputs.MarketDeals;

        AddMarketDiscount(levers, inputs, spend, anyDeals);
        AddAboveBandRepricing(levers, inputs);
        AddMultiYearTerm(levers, inputs, spend, sameCurrencyDeals);
        AddUpliftCap(levers, inputs, spend, anyDeals);
        AddNoticeTiming(levers, inputs);
        AddPaymentTerms(levers, inputs, anyDeals);
        AddVolumeFlexibility(levers, inputs);

        var coverageLow = levers.Sum(l => l.EstimatedLow ?? 0m);
        var coverageHigh = levers.Sum(l => l.EstimatedHigh ?? 0m);

        var feasibility = targetAmount is null
            ? SavingsFeasibility.NoTarget
            : coverageHigh >= targetAmount.Value
                ? SavingsFeasibility.Reachable
                : coverageHigh >= targetAmount.Value * StretchCoverageFraction
                    ? SavingsFeasibility.Stretch
                    : SavingsFeasibility.NotSupported;

        int? daysToDeadline = inputs.CancellationDeadline is { } deadline
            ? deadline.DayNumber - inputs.AsOfDate.DayNumber
            : null;

        var explanation = BuildExplanation(inputs, spend, targetAmount, targetPercent, levers, coverageLow, coverageHigh, feasibility, daysToDeadline);

        return new SavingsLeverPlan(
            inputs.ContractId,
            inputs.SupplierName,
            inputs.Currency,
            spend,
            targetAmount,
            targetPercent,
            inputs.CancellationDeadline,
            daysToDeadline,
            levers,
            coverageLow,
            coverageHigh,
            feasibility,
            explanation);
    }

    private static void AddMarketDiscount(
        List<SavingsLever> levers, SavingsLeverInputs inputs, decimal? spend, IReadOnlyList<MarketDealSnapshot> deals)
    {
        // The representative deal, not the luckiest one: same term as the contract when the corpus
        // has it, then the largest sample -- a wide corpus must not turn every lever into its best
        // outlier.
        var termMonths = inputs.RenewalTermMonths ?? 12;
        var best = deals
            .Where(d => d.DiscountAchievedPct is > 0)
            .OrderByDescending(d => d.TermMonths == termMonths)
            .ThenByDescending(d => d.SampleSize)
            .ThenByDescending(d => d.DiscountAchievedPct)
            .FirstOrDefault();

        if (best is null)
        {
            return;
        }

        var pct = Percent((decimal)best.DiscountAchievedPct!.Value);
        decimal? high = spend is { } s ? Money(s * pct / 100m) : null;
        decimal? low = high is { } h ? Money(h / 2m) : null;

        levers.Add(new SavingsLever(
            "market-discount",
            SavingsLeverType.MarketDiscount,
            "Market discount on the renewal baseline",
            low,
            high,
            pct,
            $"Companies closing {inputs.SupplierName} {best.Product} in {best.ClosingPeriod} achieved a {Fmt(pct)}% discount " +
            $"(sample {best.SampleSize}). " +
            (spend is { } sp ? $"Applied to the annual spend of {inputs.Currency} {Fmt(sp)} that is up to {inputs.Currency} {Fmt(high!.Value)} a year; half of it ({inputs.Currency} {Fmt(low!.Value)}) is the conservative floor." : "The contract has no validated annual spend yet, so the amount cannot be computed."),
            $"Ask for a {Fmt(pct)}% reduction on the renewal price, citing what comparable customers obtained this year.",
            [InsightsCitationKeys.Market(best.RecordId), InsightsCitationKeys.Fact(inputs.ContractId, "renewal")]));
    }

    private static void AddAboveBandRepricing(List<SavingsLever> levers, SavingsLeverInputs inputs)
    {
        for (var i = 0; i < inputs.PricedLines.Count; i++)
        {
            var line = inputs.PricedLines[i];
            if (line.UnitPrice is not { } unitPrice || line.Benchmark is not { } band || band.P50 <= 0 || unitPrice <= band.P50)
            {
                continue;
            }

            var quantity = line.Quantity is > 0 ? line.Quantity.Value : 1m;
            var low = Money((unitPrice - band.P50) * quantity);
            var high = Money((unitPrice - band.P25) * quantity);
            var pct = Percent((unitPrice - band.P50) / unitPrice * 100m);
            var currency = line.Currency ?? inputs.Currency;

            levers.Add(new SavingsLever(
                $"above-band-line-{i}",
                SavingsLeverType.AboveBandRepricing,
                $"Re-price {line.Description} to the market band",
                low,
                high,
                pct,
                $"{line.Description} is paid {currency} {Fmt(unitPrice)} per unit against a market P50 of {currency} {Fmt(band.P50)} " +
                $"(P25 {currency} {Fmt(band.P25)}, P75 {currency} {Fmt(band.P75)}); that is {Fmt(pct)}% above the median. " +
                $"On {Fmt(quantity)} units, moving to P50 is worth {currency} {Fmt(low)}, to P25 {currency} {Fmt(high)}.",
                $"Ask for {line.Description} at {currency} {Fmt(band.P50)} per unit, the market median; open at {currency} {Fmt(band.P25)}.",
                [
                    InsightsCitationKeys.Fact(inputs.ContractId, $"priced-line[{i}].unitPrice"),
                    InsightsCitationKeys.Calc($"priced-line[{i}].above-band"),
                ]));
        }
    }

    private static void AddMultiYearTerm(
        List<SavingsLever> levers, SavingsLeverInputs inputs, decimal? spend, IReadOnlyList<MarketDealSnapshot> deals)
    {
        var byProduct = deals.GroupBy(d => d.Product, StringComparer.OrdinalIgnoreCase);
        foreach (var group in byProduct)
        {
            var shortTerm = group.Where(d => d.TermMonths <= 12).OrderByDescending(d => d.SampleSize).FirstOrDefault();
            var longTerm = group.Where(d => d.TermMonths >= 24).OrderBy(d => d.UnitPriceP50).FirstOrDefault();
            if (shortTerm is null || longTerm is null || shortTerm.UnitPriceP50 <= 0 || longTerm.UnitPriceP50 >= shortTerm.UnitPriceP50)
            {
                continue;
            }

            var pct = Percent((1m - longTerm.UnitPriceP50 / shortTerm.UnitPriceP50) * 100m);
            decimal? high = spend is { } s ? Money(s * pct / 100m) : null;
            decimal? low = high is { } h ? Money(h / 2m) : null;

            levers.Add(new SavingsLever(
                "multi-year-term",
                SavingsLeverType.MultiYearTerm,
                "Trade a longer term for a lower price",
                low,
                high,
                pct,
                $"On the market, {longTerm.Product} at {longTerm.TermMonths} months is priced at P50 {longTerm.Currency} {Fmt(longTerm.UnitPriceP50)} " +
                $"against {shortTerm.Currency} {Fmt(shortTerm.UnitPriceP50)} at {shortTerm.TermMonths} months: {Fmt(pct)}% lower. " +
                (spend is { } sp ? $"On {inputs.Currency} {Fmt(sp)} a year that is up to {inputs.Currency} {Fmt(high!.Value)}." : "No validated annual spend, so no amount."),
                $"Offer a {longTerm.TermMonths}-month commitment only in exchange for the {Fmt(pct)}% lower unit price and a price lock for the whole term.",
                [InsightsCitationKeys.Market(longTerm.RecordId), InsightsCitationKeys.Market(shortTerm.RecordId)]));
            return;
        }
    }

    private static void AddUpliftCap(
        List<SavingsLever> levers, SavingsLeverInputs inputs, decimal? spend, IReadOnlyList<MarketDealSnapshot> deals)
    {
        var marketCap = deals.Where(d => d.UpliftCapPct is > 0).Select(d => (Deal: d, Cap: (decimal)d.UpliftCapPct!.Value)).OrderBy(x => x.Cap).FirstOrDefault();

        for (var i = 0; i < inputs.Clauses.Count; i++)
        {
            var clause = inputs.Clauses[i];
            if (!UpliftClausePattern.IsMatch(clause.ClauseType) && !UpliftClausePattern.IsMatch(clause.RawText))
            {
                continue;
            }

            decimal? clausePct = null;
            var m = PercentInTextPattern.Match(clause.RawText);
            if (m.Success && decimal.TryParse(m.Groups["num"].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
            {
                clausePct = Percent(parsed);
            }

            var citations = new List<string> { InsightsCitationKeys.Fact(inputs.ContractId, $"clause[{i}]") };
            if (marketCap.Deal is not null)
            {
                citations.Add(InsightsCitationKeys.Market(marketCap.Deal.RecordId));
            }

            decimal? high = null;
            decimal? low = null;
            string rationale;
            string ask;
            if (clausePct is { } cp && spend is { } s)
            {
                high = Money(s * cp / 100m);
                var avoidedPct = marketCap.Deal is not null ? Math.Max(0m, cp - marketCap.Cap) : cp / 2m;
                low = Money(s * avoidedPct / 100m);
                rationale = $"The contract allows a {Fmt(cp)}% price increase ({clause.ClauseType}); on {inputs.Currency} {Fmt(s)} that is {inputs.Currency} {Fmt(high.Value)} a year at the next renewal. " +
                            (marketCap.Deal is not null
                                ? $"Comparable customers capped the uplift at {Fmt(marketCap.Cap)}%, so the floor of this lever is {inputs.Currency} {Fmt(low.Value)}."
                                : $"Striking it entirely is worth the full amount; half of it ({inputs.Currency} {Fmt(low.Value)}) is the floor.");
                ask = marketCap.Deal is not null
                    ? $"Ask to cap the annual increase at {Fmt(marketCap.Cap)}% or to freeze the price for the renewal term."
                    : "Ask to freeze the price for the renewal term, or to cap any increase below the current clause.";
            }
            else
            {
                rationale = $"The contract carries a price increase / indexation clause ({clause.ClauseType})" +
                            (marketCap.Deal is not null ? $"; comparable customers capped it at {Fmt(marketCap.Cap)}%." : ".") +
                            (spend is null ? " No validated annual spend, so no amount." : " The clause names no percentage, so no amount.");
                ask = marketCap.Deal is not null
                    ? $"Ask for an explicit cap at {Fmt(marketCap.Cap)}% and a price freeze for the first year."
                    : "Ask for an explicit cap on any increase and a price freeze for the first year.";
            }

            levers.Add(new SavingsLever(
                "uplift-cap",
                SavingsLeverType.UpliftCap,
                "Cap or strike the price increase clause",
                low,
                high,
                clausePct ?? (marketCap.Deal is not null ? marketCap.Cap : null),
                rationale,
                ask,
                citations));
            return;
        }

        if (marketCap.Deal is not null && inputs.AutoRenewal)
        {
            levers.Add(new SavingsLever(
                "uplift-cap",
                SavingsLeverType.UpliftCap,
                "Lock an uplift cap before the auto-renewal",
                null,
                null,
                marketCap.Cap,
                $"The contract auto-renews; comparable customers wrote a {Fmt(marketCap.Cap)}% annual uplift cap into their renewal. No increase clause was extracted here, so no amount is computed.",
                $"Ask for a written {Fmt(marketCap.Cap)}% cap on any annual increase as a condition of renewing.",
                [InsightsCitationKeys.Market(marketCap.Deal.RecordId), InsightsCitationKeys.Fact(inputs.ContractId, "renewal")]));
        }
    }

    private static void AddNoticeTiming(List<SavingsLever> levers, SavingsLeverInputs inputs)
    {
        if (inputs.CancellationDeadline is not { } deadline)
        {
            return;
        }

        var days = deadline.DayNumber - inputs.AsOfDate.DayNumber;
        var iso = deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var rationale = days >= 0
            ? $"Notice must be given by {iso}: {days} days from today. Until then the supplier still has to compete for the renewal" +
              (inputs.AutoRenewal ? "; after it the contract auto-renews and the leverage is gone." : ".")
            : $"The notice deadline {iso} has passed" + (inputs.AutoRenewal ? "; the contract has auto-renewed, so the next window is the following cycle." : ".");

        levers.Add(new SavingsLever(
            "notice-timing",
            SavingsLeverType.NoticeTiming,
            "Open the negotiation before the notice deadline",
            null,
            null,
            null,
            rationale,
            days >= 0
                ? $"Send a written non-renewal reservation before {iso} and open the commercial round now, so the deadline works for you, not for the supplier."
                : "Diary the next notice window now and open the renewal round at least ninety days before it.",
            [InsightsCitationKeys.Fact(inputs.ContractId, "renewal")]));
    }

    private static void AddPaymentTerms(List<SavingsLever> levers, SavingsLeverInputs inputs, IReadOnlyList<MarketDealSnapshot> deals)
    {
        var contractNet = ParseNetDays(inputs.PaymentTerms);
        var marketBest = deals
            .Select(d => (Deal: d, Net: ParseNetDays(d.PaymentTerms)))
            .Where(x => x.Net is not null)
            .OrderByDescending(x => x.Net)
            .FirstOrDefault();

        if (marketBest.Deal is null || contractNet is null || marketBest.Net <= contractNet)
        {
            return;
        }

        levers.Add(new SavingsLever(
            "payment-terms",
            SavingsLeverType.PaymentTerms,
            "Longer payment terms",
            null,
            null,
            null,
            $"The contract pays at net {contractNet} days; comparable customers obtained net {marketBest.Net} days. Cash, not price, but it is a concession the supplier gives cheaply.",
            $"Ask for net {marketBest.Net} days, or keep net {contractNet} in exchange for an early-payment discount.",
            [InsightsCitationKeys.Fact(inputs.ContractId, "renewal"), InsightsCitationKeys.Market(marketBest.Deal.RecordId)]));
    }

    private static void AddVolumeFlexibility(List<SavingsLever> levers, SavingsLeverInputs inputs)
    {
        var hasCommittedQuantities = inputs.PricedLines.Any(l => l.Quantity is > 0);
        if (!hasCommittedQuantities)
        {
            return;
        }

        var hasTrueDown = inputs.Clauses.Any(c => TrueDownClausePattern.IsMatch(c.RawText) || TrueDownClausePattern.IsMatch(c.ClauseType));
        if (hasTrueDown)
        {
            return;
        }

        levers.Add(new SavingsLever(
            "volume-flexibility",
            SavingsLeverType.VolumeFlexibility,
            "Right to reduce committed volumes",
            null,
            null,
            null,
            "Quantities are committed and no true-down clause was found: every unused unit is paid for the whole term. The amount depends on actual usage, which the contract does not state.",
            "Ask for a true-down right at renewal (reduce quantities without penalty) and pay only for units in use; bring the usage report to the table.",
            [InsightsCitationKeys.Fact(inputs.ContractId, "renewal")]));
    }

    private static string BuildExplanation(
        SavingsLeverInputs inputs,
        decimal? spend,
        decimal? targetAmount,
        decimal? targetPercent,
        IReadOnlyList<SavingsLever> levers,
        decimal coverageLow,
        decimal coverageHigh,
        SavingsFeasibility feasibility,
        int? daysToDeadline)
    {
        var parts = new List<string>();

        if (targetAmount is { } t)
        {
            parts.Add(spend is { } s
                ? $"Target {inputs.Currency} {Fmt(t)} is {Fmt(targetPercent!.Value)}% of the annual spend of {inputs.Currency} {Fmt(s)}."
                : $"Target {inputs.Currency} {Fmt(t)}; the contract has no validated annual spend.");
        }
        else if (spend is { } s)
        {
            parts.Add($"Annual spend {inputs.Currency} {Fmt(s)}.");
        }

        var quantified = levers.Count(l => l.EstimatedHigh is not null);
        parts.Add(quantified > 0
            ? $"{levers.Count} grounded levers, {quantified} quantified: together {inputs.Currency} {Fmt(coverageLow)} to {inputs.Currency} {Fmt(coverageHigh)} a year."
            : levers.Count > 0
                ? $"{levers.Count} grounded levers, none quantifiable from the validated facts."
                : "No lever is grounded in the validated facts or the market corpus.");

        parts.Add(feasibility switch
        {
            SavingsFeasibility.Reachable => "The target is reachable on the grounded levers alone.",
            SavingsFeasibility.Stretch => "The target is a stretch: the grounded levers cover most of it, the rest needs usage data or a competitive alternative.",
            SavingsFeasibility.NotSupported => "The target is not supported by the grounded levers; say so and name what is missing.",
            _ => string.Empty,
        });

        if (daysToDeadline is { } d && d >= 0)
        {
            parts.Add($"{d} days to the notice deadline.");
        }

        return string.Join(" ", parts.Where(p => p.Length > 0));
    }

    private static int? ParseNetDays(string? terms)
    {
        if (string.IsNullOrWhiteSpace(terms))
        {
            return null;
        }

        var m = NetDaysPattern.Match(terms);
        return m.Success && int.TryParse(m.Groups["days"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var days) ? days : null;
    }

    internal static decimal Money(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);

    internal static decimal Percent(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Invariant, separator-free rendering — the exact token the numeric guard parses.</summary>
    internal static string Fmt(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
