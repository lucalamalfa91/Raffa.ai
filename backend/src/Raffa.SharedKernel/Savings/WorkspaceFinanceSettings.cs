namespace Raffa.SharedKernel.Savings;

/// <summary>
/// How a workspace reports money (Spesa e risparmi): the currency every total is in, the month its
/// fiscal year starts, and the rates to convert other currencies: the ECB reference rates downloaded
/// every morning, overridden by any rate the admin fixed. A currency without a rate is never
/// converted by Raffa: its contracts stay out of the totals and are listed.
/// </summary>
/// <param name="ReportingCurrency">ISO 4217 code, chosen by the admin (default: the workspace's currency).</param>
/// <param name="FiscalYearStartMonth">1 (January) to 12.</param>
/// <param name="BudgetRates">Units of <paramref name="ReportingCurrency"/> per one unit of the keyed
/// currency (ISO 4217, upper case).</param>
public sealed record WorkspaceFinanceSettings(
    string ReportingCurrency,
    int FiscalYearStartMonth,
    IReadOnlyDictionary<string, decimal> BudgetRates)
{
    public const string DefaultCurrency = "CHF";

    public static WorkspaceFinanceSettings Default { get; } = new(DefaultCurrency, 1, new Dictionary<string, decimal>());

    /// <summary>Units of the reporting currency per unit of <paramref name="currency"/>: 1 for the
    /// reporting currency itself, the admin's budget rate otherwise, null when there is none.</summary>
    public decimal? RateFor(string? currency)
    {
        var code = string.IsNullOrWhiteSpace(currency) ? ReportingCurrency : currency.Trim().ToUpperInvariant();
        if (string.Equals(code, ReportingCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return 1m;
        }

        return BudgetRates.TryGetValue(code, out var rate) && rate > 0 ? rate : null;
    }
}

/// <summary>The workspace's <see cref="WorkspaceFinanceSettings"/>. Implemented by the API host over the
/// Identity module (Savings may not read it).</summary>
public interface IWorkspaceFinanceSettingsReader
{
    Task<WorkspaceFinanceSettings> GetAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}
