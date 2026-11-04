using System.Globalization;
using System.Text.Json;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Raffa.Identity.Workspace.Domain;

namespace Raffa.Identity.Workspace.Infrastructure;

/// <summary>
/// Reads and writes a workspace's settings row: ADR-030 gate 2 (<c>web_research_enabled</c>) and
/// Spesa e risparmi (reporting currency, fiscal year start, budget rates). Tenant-scoped like every
/// sibling service here (<see cref="ITenantContext.BeginScope"/> around each read/write, RLS
/// underneath); each changed setting writes one audit row naming the actor and the new value, and
/// a no-op write writes none. The Admin gate is the endpoint's job, not this service's -- the same
/// split <see cref="WorkspaceMembershipService.RemoveMemberAsync"/> uses.
/// </summary>
public sealed class WorkspaceSettingsService(
    IdentityWorkspaceDbContext db, ITenantContext tenantContext, IClock clock, IAuditWriter auditWriter)
{
    public const string AuditWebResearchEnabledAction = "workspace.settings.web_research_enabled";
    public const string AuditReportingCurrencyAction = "workspace.settings.reporting_currency";
    public const string AuditFiscalYearStartMonthAction = "workspace.settings.fiscal_year_start_month";
    public const string AuditBudgetRatesAction = "workspace.settings.budget_rates";

    /// <summary>Most budget rates one workspace keeps.</summary>
    public const int MaxBudgetRates = 30;

    /// <summary>Largest budget rate accepted (units of the reporting currency per unit).</summary>
    public const decimal MaxBudgetRate = 1_000_000m;

    public const string DefaultCurrency = "CHF";

    private const string AuditResourceType = "Workspace";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary><see langword="null"/> when no workspace row exists for the tenant.</summary>
    public async Task<WorkspaceSettingsResult?> GetAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var workspace = await db.Workspaces
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);

        return workspace is null ? null : Result(workspace);
    }

    /// <summary>Flips the web-research opt-in; <see langword="null"/> when no workspace row exists.</summary>
    public async Task<WorkspaceSettingsResult?> SetWebResearchEnabledAsync(
        TenantId tenantId, bool enabled, string actor, CancellationToken cancellationToken = default)
    {
        var (result, _) = await UpdateAsync(tenantId, new UpdateWorkspaceSettingsRequest(WebResearchEnabled: enabled), actor, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Why <paramref name="request"/> cannot be applied, or null when it can: a currency outside the
    /// closed list (<see cref="WorkspaceProvisioningService.SupportedCurrencies"/>), a month outside
    /// 1-12, a non-positive or absurd rate, a rate for the reporting currency itself, too many rates.
    /// </summary>
    public static string? Validate(UpdateWorkspaceSettingsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ReportingCurrency is { } currency && !WorkspaceProvisioningService.SupportedCurrencies.Contains(Code(currency)))
        {
            return $"reportingCurrency '{currency}' is not a supported ISO 4217 currency.";
        }

        if (request.FiscalYearStartMonth is { } month && month is < 1 or > 12)
        {
            return "fiscalYearStartMonth must be between 1 and 12.";
        }

        if (request.BudgetRates is { } rates)
        {
            if (rates.Count > MaxBudgetRates)
            {
                return $"budgetRates holds at most {MaxBudgetRates} currencies.";
            }

            foreach (var (key, rate) in rates)
            {
                if (!WorkspaceProvisioningService.SupportedCurrencies.Contains(Code(key)))
                {
                    return $"budgetRates names '{key}', which is not a supported ISO 4217 currency.";
                }

                if (rate <= 0 || rate > MaxBudgetRate)
                {
                    return $"The budget rate for {Code(key)} must be greater than 0 and at most {MaxBudgetRate.ToString(CultureInfo.InvariantCulture)}.";
                }
            }

            if (rates.Keys.Select(Code).Distinct(StringComparer.Ordinal).Count() != rates.Count)
            {
                return "budgetRates names a currency twice.";
            }
        }

        return null;
    }

    /// <summary>
    /// Applies every setting <paramref name="request"/> names. Changing the reporting currency
    /// clears the budget rates unless the same request sets new ones: the old rates were relative to
    /// the old currency and would otherwise convert silently wrong. Returns the error of
    /// <see cref="Validate"/> (nothing written), or the settings (null result when no workspace row exists).
    /// </summary>
    public async Task<(WorkspaceSettingsResult? Result, string? Error)> UpdateAsync(
        TenantId tenantId, UpdateWorkspaceSettingsRequest request, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (Validate(request) is { } error)
        {
            return (null, error);
        }

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var workspace = await db.Workspaces
            .FirstOrDefaultAsync(w => w.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (workspace is null)
        {
            return (null, null);
        }

        var audits = new List<(string Action, string Detail)>();
        if (request.WebResearchEnabled is { } enabled && workspace.WebResearchEnabled != enabled)
        {
            workspace.WebResearchEnabled = enabled;
            audits.Add((AuditWebResearchEnabledAction, $"enabled={enabled}"));
        }

        var ratesBefore = RatesOf(workspace);
        if (request.ReportingCurrency is { } currency)
        {
            var code = Code(currency);
            if (!string.Equals(EffectiveCurrency(workspace), code, StringComparison.Ordinal) || workspace.ReportingCurrency is null)
            {
                var changed = !string.Equals(EffectiveCurrency(workspace), code, StringComparison.Ordinal);
                workspace.ReportingCurrency = code;
                audits.Add((AuditReportingCurrencyAction, $"currency={code}"));
                if (changed && request.BudgetRates is null && ratesBefore.Count > 0)
                {
                    workspace.BudgetRatesJson = null;
                    audits.Add((AuditBudgetRatesAction, "rates=cleared (reporting currency changed)"));
                }
            }
        }

        if (request.FiscalYearStartMonth is { } month && workspace.FiscalYearStartMonth != month)
        {
            workspace.FiscalYearStartMonth = month;
            audits.Add((AuditFiscalYearStartMonthAction, string.Create(CultureInfo.InvariantCulture, $"month={month}")));
        }

        if (request.BudgetRates is { } rates)
        {
            var reporting = EffectiveCurrency(workspace);
            var next = rates
                .Select(kv => (Code: Code(kv.Key), kv.Value))
                .Where(r => !string.Equals(r.Code, reporting, StringComparison.Ordinal))
                .OrderBy(r => r.Code, StringComparer.Ordinal)
                .ToDictionary(r => r.Code, r => r.Value, StringComparer.Ordinal);
            if (!SameRates(next, RatesOf(workspace)))
            {
                workspace.BudgetRatesJson = next.Count == 0 ? null : JsonSerializer.Serialize(next, Json);
                audits.Add((AuditBudgetRatesAction, "rates=" + (next.Count == 0
                    ? "none"
                    : string.Join(",", next.Select(r => r.Key + ":" + r.Value.ToString(CultureInfo.InvariantCulture))))));
            }
        }

        if (audits.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            foreach (var (action, detail) in audits)
            {
                await auditWriter.WriteAsync(
                        new AuditEntry(tenantId, actor, action, AuditResourceType, workspace.Id.Value.ToString(), clock.UtcNow, detail),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return (Result(workspace), null);
    }

    /// <summary>The reporting currency in force: the Admin's choice, else the workspace's own, else CHF.</summary>
    public static string EffectiveCurrency(WorkspaceTenant workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return !string.IsNullOrWhiteSpace(workspace.ReportingCurrency) ? Code(workspace.ReportingCurrency)
            : !string.IsNullOrWhiteSpace(workspace.Currency) ? Code(workspace.Currency)
            : DefaultCurrency;
    }

    /// <summary>The budget rates stored on <paramref name="workspace"/>; empty when none (or unreadable).</summary>
    public static IReadOnlyDictionary<string, decimal> RatesOf(WorkspaceTenant workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (string.IsNullOrWhiteSpace(workspace.BudgetRatesJson))
        {
            return new Dictionary<string, decimal>(StringComparer.Ordinal);
        }

        try
        {
            var rates = JsonSerializer.Deserialize<Dictionary<string, decimal>>(workspace.BudgetRatesJson, Json) ?? [];
            return rates
                .Where(r => r.Value > 0)
                .ToDictionary(r => Code(r.Key), r => r.Value, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            // A malformed value converts nothing: better no rate than a wrong one.
            return new Dictionary<string, decimal>(StringComparer.Ordinal);
        }
    }

    private static WorkspaceSettingsResult Result(WorkspaceTenant workspace) => new(
        workspace.WebResearchEnabled,
        EffectiveCurrency(workspace),
        !string.IsNullOrWhiteSpace(workspace.ReportingCurrency),
        workspace.FiscalYearStartMonth,
        RatesOf(workspace));

    private static bool SameRates(IReadOnlyDictionary<string, decimal> a, IReadOnlyDictionary<string, decimal> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);

    private static string Code(string currency) => currency.Trim().ToUpperInvariant();
}
