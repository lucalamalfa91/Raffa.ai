using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Savings;

namespace Raffa.Savings.Application.Spend;

/// <summary>
/// Spesa e risparmi, the reading half: the validated contracts (<see cref="IContractFactsReader"/>,
/// the same facts every screen reads), their documented term history
/// (<see cref="IContractTermHistoryReader"/>) and the workspace's reporting settings, handed to
/// <see cref="SpendCalculator"/>. Builds <c>GET /api/savings/spend</c>.
/// </summary>
public sealed class SpendReportQueryService(
    IContractFactsReader contractFacts,
    IContractTermHistoryReader termHistory,
    IWorkspaceFinanceSettingsReader financeSettings,
    IClock clock)
{
    /// <summary>Default span: two fiscal years back, the current one, two ahead.</summary>
    public const int DefaultYearsAround = 2;

    public async Task<SpendReport> GetAsync(
        TenantId tenantId, int? fromFiscalYear, int? toFiscalYear, string basis, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var settings = await financeSettings.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var current = SpendCalculator.FiscalYearOf(today, settings.FiscalYearStartMonth is >= 1 and <= 12 ? settings.FiscalYearStartMonth : 1);
        var from = fromFiscalYear ?? current - DefaultYearsAround;
        var to = toFiscalYear ?? current + DefaultYearsAround;

        var facts = await contractFacts.ListValidatedAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var histories = await termHistory.ListAsync(tenantId, facts.Select(f => f.ContractId).ToList(), cancellationToken).ConfigureAwait(false);
        var inputs = facts.Select(f =>
        {
            histories.TryGetValue(f.ContractId, out var history);
            var end = f.EndDate ?? ContractTermCalculator.ParseDate(f.End);
            var deadline = f.NoticeDeadline ?? ContractTermCalculator.ParseDate(f.Cancel);
            // The contract's own gap between end and last day of notice (an explicit cancellation
            // deadline may differ from End - notice days), as ContractTermCalculator keeps it.
            var gap = end is { } e && deadline is { } d ? e.DayNumber - d.DayNumber : f.Notice;
            return new SpendContractInput(
                f.ContractId,
                f.Supplier,
                f.Name,
                f.Category,
                history?.Terms ?? [],
                ContractTermCalculator.IsAuto(f.Auto),
                ContractTermCalculator.RenewalMonths(f),
                gap,
                f.Uplift,
                history?.BillingPeriod);
        }).ToList();

        return SpendCalculator.Build(inputs, settings, today, from, to, basis);
    }
}
