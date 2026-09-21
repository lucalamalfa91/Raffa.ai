using System.Globalization;
using Raffa.SharedKernel;

namespace Raffa.Insights.Savings;

public sealed record PortfolioSavingsCandidateInputs(
    EntityId ContractId,
    string SupplierName,
    string Currency,
    decimal? AnnualSpend,
    DateOnly? CancellationDeadline,
    DateOnly? EndDate,
    bool AutoRenewal,
    SavingsLeverPlan Plan);

public sealed record PortfolioSavingsCandidate(
    EntityId ContractId,
    string SupplierName,
    string Currency,
    decimal? AnnualSpend,
    decimal CoverageLow,
    decimal CoverageHigh,
    bool InWindow,
    DateOnly? ActionDate,
    int? DaysToAction,
    decimal RunningCumulativeHigh,
    string WhyNow,
    IReadOnlyList<string> TopLeverKeys);

public sealed record PortfolioSavingsTargetPlan(
    decimal? TargetAmount,
    int? WindowDays,
    string Currency,
    IReadOnlyList<PortfolioSavingsCandidate> InWindow,
    IReadOnlyList<PortfolioSavingsCandidate> BeyondWindow,
    decimal CoverageLow,
    decimal CoverageHigh,
    SavingsFeasibility Feasibility,
    string Explanation);

/// <summary>
/// Pure, deterministic portfolio ranking for "save X in the next N days across my active
/// contracts": which contracts have a grounded lever plan, which of them can actually be acted on
/// inside the window (notice deadline or end date inside it, or no fixed date at all), how much
/// they add up to, and whether that covers the target. Contracts whose lever plan quantified
/// nothing are listed but never summed.
/// </summary>
public static class PortfolioSavingsTargetCalculator
{
    public const int DefaultWindowDays = 90;

    public static PortfolioSavingsTargetPlan Compute(
        IReadOnlyList<PortfolioSavingsCandidateInputs> candidates,
        decimal? targetAmount,
        int? windowDays,
        DateOnly asOfDate)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var window = windowDays is > 0 ? windowDays.Value : DefaultWindowDays;
        var windowEnd = asOfDate.AddDays(window);

        // Sum in the portfolio's dominant currency only; a mixed portfolio is named, never FX-summed.
        var currency = candidates
            .Where(c => c.Plan.CoverageHigh > 0)
            .GroupBy(c => c.Currency, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(c => c.Plan.CoverageHigh))
            .Select(g => g.Key)
            .FirstOrDefault() ?? candidates.FirstOrDefault()?.Currency ?? "EUR";

        var inWindow = new List<PortfolioSavingsCandidate>();
        var beyond = new List<PortfolioSavingsCandidate>();

        foreach (var c in candidates.Where(c => c.Plan.Levers.Count > 0))
        {
            var actionDate = c.CancellationDeadline ?? c.EndDate;
            int? days = actionDate is { } d ? d.DayNumber - asOfDate.DayNumber : null;
            var isInWindow = actionDate is null || (actionDate.Value >= asOfDate && actionDate.Value <= windowEnd);
            var sameCurrency = string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase);

            var whyNow = actionDate is null
                ? "No fixed notice deadline: the round can be opened now."
                : days < 0
                    ? $"The notice deadline {Iso(actionDate.Value)} has passed; next cycle."
                    : isInWindow
                        ? $"Notice deadline {Iso(actionDate.Value)}, {days} days away: inside the window, the supplier still has to compete."
                        : $"Notice deadline {Iso(actionDate.Value)}, {days} days away: beyond the window, prepare now and close next period.";

            if (!sameCurrency)
            {
                whyNow += $" Amounts in {c.Currency}, not summed with the {currency} candidates.";
            }

            var candidate = new PortfolioSavingsCandidate(
                c.ContractId,
                c.SupplierName,
                c.Currency,
                c.AnnualSpend,
                c.Plan.CoverageLow,
                c.Plan.CoverageHigh,
                isInWindow && days is null or >= 0,
                actionDate,
                days,
                0m,
                whyNow,
                c.Plan.Levers.Where(l => l.EstimatedHigh is not null).OrderByDescending(l => l.EstimatedHigh).Take(2).Select(l => l.Key).ToList());

            if (candidate.InWindow)
            {
                inWindow.Add(candidate);
            }
            else
            {
                beyond.Add(candidate);
            }
        }

        inWindow = inWindow
            .OrderByDescending(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(c => c.CoverageHigh)
            .ThenBy(c => c.DaysToAction ?? int.MaxValue)
            .ToList();

        beyond = beyond
            .OrderBy(c => c.DaysToAction is { } d && d >= 0 ? d : int.MaxValue)
            .ThenByDescending(c => c.CoverageHigh)
            .ToList();

        var running = 0m;
        for (var i = 0; i < inWindow.Count; i++)
        {
            if (string.Equals(inWindow[i].Currency, currency, StringComparison.OrdinalIgnoreCase))
            {
                running += inWindow[i].CoverageHigh;
            }

            inWindow[i] = inWindow[i] with { RunningCumulativeHigh = running };
        }

        var summed = inWindow.Where(c => string.Equals(c.Currency, currency, StringComparison.OrdinalIgnoreCase)).ToList();
        var coverageLow = summed.Sum(c => c.CoverageLow);
        var coverageHigh = summed.Sum(c => c.CoverageHigh);

        var feasibility = targetAmount is null
            ? SavingsFeasibility.NoTarget
            : coverageHigh >= targetAmount.Value
                ? SavingsFeasibility.Reachable
                : coverageHigh >= targetAmount.Value * SavingsLeverCalculator.StretchCoverageFraction
                    ? SavingsFeasibility.Stretch
                    : SavingsFeasibility.NotSupported;

        var explanation = BuildExplanation(targetAmount, window, currency, inWindow, beyond, coverageLow, coverageHigh, feasibility);

        return new PortfolioSavingsTargetPlan(
            targetAmount, window, currency, inWindow, beyond, coverageLow, coverageHigh, feasibility, explanation);
    }

    private static string BuildExplanation(
        decimal? targetAmount,
        int window,
        string currency,
        IReadOnlyList<PortfolioSavingsCandidate> inWindow,
        IReadOnlyList<PortfolioSavingsCandidate> beyond,
        decimal coverageLow,
        decimal coverageHigh,
        SavingsFeasibility feasibility)
    {
        var parts = new List<string>();
        var f = SavingsLeverCalculator.Fmt;

        parts.Add(targetAmount is { } t
            ? $"Target {currency} {f(t)} within {window} days."
            : $"Window {window} days, no amount named.");

        parts.Add(inWindow.Count > 0
            ? $"{inWindow.Count} contracts can be acted on inside the window; their grounded levers add up to {currency} {f(coverageLow)} to {currency} {f(coverageHigh)}."
            : "No contract with a grounded lever can be acted on inside the window.");

        if (beyond.Count > 0)
        {
            parts.Add($"{beyond.Count} more have levers but their notice deadline falls outside the window.");
        }

        parts.Add(feasibility switch
        {
            SavingsFeasibility.Reachable => "The target is reachable inside the window on the grounded levers alone.",
            SavingsFeasibility.Stretch => "The target is a stretch inside the window: most of it is covered, the rest needs a contract from the next period or usage data.",
            SavingsFeasibility.NotSupported => "The target is not supported inside the window by the grounded levers; say so and name the gap.",
            _ => string.Empty,
        });

        return string.Join(" ", parts.Where(p => p.Length > 0));
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
