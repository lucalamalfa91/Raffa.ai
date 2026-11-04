using Microsoft.EntityFrameworkCore;
using Raffa.Savings.Domain.Results;
using Raffa.Savings.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Savings;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Savings.Application.Results;

/// <summary>
/// Reads what Risultati is built from: signed results with their invoice-proven part
/// (<c>saving_result</c> + <c>saving_realization</c> + <c>invoice</c>), the validated contracts
/// (<see cref="IContractFactsReader"/>: analysed spend, notice deadlines) and the cases' objectives
/// (<see cref="ICaseObjectiveSource"/>). Builds <c>GET /api/savings/results</c>.
/// </summary>
public sealed class ResultsQueryService(
    SavingsDbContext dbContext,
    ITenantContext tenantContext,
    IContractFactsReader contractFacts,
    ICaseObjectiveSource caseObjectives,
    IClock clock,
    IWorkspaceFinanceSettingsReader? financeSettings = null)
{
    public DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    /// <summary>The workspace's reporting currency, fiscal year and budget rates.</summary>
    public async Task<WorkspaceFinanceSettings> SettingsAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        financeSettings is null
            ? WorkspaceFinanceSettings.Default
            : await financeSettings.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);

    /// <summary>The report in the reporting currency, on the workspace's fiscal year. A result or a
    /// contract in a currency without a budget rate is left out of every sum (never converted at a
    /// guessed rate); its currency is listed in <see cref="ResultsReport.ExcludedCurrencies"/>.</summary>
    public async Task<ResultsReport> GetReportAsync(TenantId tenantId, string? period, CancellationToken cancellationToken = default)
    {
        var settings = await SettingsAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var facts = await contractFacts.ListValidatedAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var validated = Validated(facts, settings);
        var (results, excluded) = await LoadResultsAsync(tenantId, settings, cancellationToken).ConfigureAwait(false);
        var objectives = await caseObjectives.ListAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var report = ResultsReportBuilder.Build(Today, period, results, validated, objectives, settings.ReportingCurrency, settings.FiscalYearStartMonth);
        return report with
        {
            FiscalYearStartMonth = settings.FiscalYearStartMonth,
            ExcludedCurrencies = excluded
                .Concat(facts.Where(f => settings.RateFor(f.Currency) is null).Select(f => Code(f.Currency)))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList(),
        };
    }

    public async Task<IReadOnlyList<ResultsContract>> ListValidatedAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        var settings = await SettingsAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var facts = await contractFacts.ListValidatedAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return Validated(facts, settings);
    }

    /// <summary>The validated contracts, their annual value in the reporting currency (0, with
    /// <see cref="ResultsContract.HasRate"/> false, when the admin set no rate for their currency).</summary>
    private IReadOnlyList<ResultsContract> Validated(IReadOnlyList<ContractFacts> facts, WorkspaceFinanceSettings settings)
    {
        var today = Today;
        // Validation order (the prototype's `validated` list): ties in the charts keep this order.
        return facts
            .OrderBy(f => f.ValidatedAt)
            .ThenBy(f => f.FixtureId ?? int.MaxValue)
            .Select(f =>
            {
                // The effective term: a contract that renewed by itself is read on its next notice deadline.
                var term = ContractTermCalculator.Compute(f, today);
                var deadline = term.RolledOver ? term.NoticeDeadline : f.NoticeDeadline;
                var rate = settings.RateFor(f.Currency);
                return new ResultsContract(
                    f.ContractId,
                    f.Supplier,
                    f.Category,
                    rate is { } r ? Math.Round(f.Spend * r, 2, MidpointRounding.AwayFromZero) : 0m,
                    deadline,
                    ResultsReportBuilder.DaysTo(deadline, today, f.CancelDays),
                    f.Uplift,
                    string.Equals(f.Auto, "Yes", StringComparison.OrdinalIgnoreCase),
                    term.Status,
                    rate is not null);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<ResultsReportItem>> ListResultsAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        var settings = await SettingsAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return (await LoadResultsAsync(tenantId, settings, cancellationToken).ConfigureAwait(false)).Items;
    }

    private static string Code(string? currency) => (currency ?? "").Trim().ToUpperInvariant();

    /// <summary>The signed results in the reporting currency, and the currencies of those left out (no rate).</summary>
    private async Task<(IReadOnlyList<ResultsReportItem> Items, IReadOnlyList<string> Excluded)> LoadResultsAsync(
        TenantId tenantId, WorkspaceFinanceSettings settings, CancellationToken cancellationToken)
    {
        using var _ = tenantContext.BeginScope(tenantId);
        var rows = await dbContext.SavingResults.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var proofs = await (
                from z in dbContext.SavingRealizations.AsNoTracking()
                join i in dbContext.Invoices.AsNoTracking() on z.InvoiceId equals i.Id
                where z.TenantId == tenantId
                select new { z.SavingResultId, z.RealizedAmount, i.Period })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var byResult = proofs.GroupBy(p => p.SavingResultId).ToDictionary(g => g.Key, g => g.ToList());

        var excluded = rows.Where(r => settings.RateFor(r.Currency) is null).Select(r => Code(r.Currency)).Distinct(StringComparer.Ordinal).ToList();
        var items = rows.Where(r => settings.RateFor(r.Currency) is not null).Select(r =>
        {
            var rate = settings.RateFor(r.Currency)!.Value;
            byResult.TryGetValue(r.Id, out var ps);
            ResultsRealizationProof? proof = ps is { Count: > 0 }
                ? new ResultsRealizationProof(ps.Min(p => p.Period), ps.Max(p => p.Period), ps.Count, PeriodMonths(r))
                : null;
            return new ResultsReportItem(
                r.Id.Value,
                r.SupplierName,
                r.ContractName,
                r.Category,
                r.Kind,
                Math.Round(r.AmountAnnual * rate, 2, MidpointRounding.AwayFromZero),
                r.SignedOn,
                r.Note,
                r.BaselineDesc,
                r.PeriodDesc,
                r.Method,
                r.EvidenceFileName,
                r.OwnerName,
                Math.Round((ps?.Sum(p => p.RealizedAmount) ?? 0m) * rate, 2, MidpointRounding.AwayFromZero),
                proof);
        }).ToList();
        return (items, excluded);
    }

    /// <summary>How many months the result's period covers (12 for "12 months from 01 Apr 2026").</summary>
    private static int? PeriodMonths(SavingResult r) =>
        r.PeriodFrom is { } from && r.PeriodTo is { } to
            ? MonthsBetween(from, to.AddDays(1))
            : null;

    private static int MonthsBetween(DateOnly from, DateOnly endExclusive) =>
        ((endExclusive.Year - from.Year) * 12) + endExclusive.Month - from.Month - (endExclusive.Day < from.Day ? 1 : 0);
}
