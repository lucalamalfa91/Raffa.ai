using Raffa.AiFlows.WebResearch.Configuration;
using Raffa.Api.Infrastructure;
using Raffa.Chat.Application.WebResearch;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Api;

/// <summary>
/// `GET`/`PATCH /api/workspaces/{tenantId}/settings` (ADR-030 gate 2): the workspace switches Ask
/// Raffa's web research hangs off. Same ladder as the members route group
/// (<see cref="WorkspaceMembersEndpointExtensions"/>): no identity -> 401; the tenant is always
/// the route value, never a header; a non-member -> 404 (never 403, a tenant-existence oracle);
/// any live member reads; only an Admin writes (403 otherwise). Verify, then scope, then
/// read/write (ADR-009 w14 footer clause 7) -- the membership check opens its own narrow tenant
/// scope and closes it before <see cref="WorkspaceSettingsService"/> opens the next. The body also
/// carries <c>webResearchAvailable</c> — the environment's kill switch
/// (<see cref="WebResearchOptions.Enabled"/>) — so Ask's web-search toggle (ADR-032) is shown only
/// where the feature exists at all.
/// </summary>
public static class WorkspaceSettingsEndpointExtensions
{
    public static IEndpointRouteBuilder MapWorkspaceSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/workspaces/{tenantId}/settings", GetSettingsAsync);
        endpoints.MapPatch("/api/workspaces/{tenantId}/settings", PatchSettingsAsync);
        return endpoints;
    }

    private static async Task<IResult> GetSettingsAsync(
        string tenantId,
        ICallerIdentity callerIdentity,
        IdentityWorkspaceDbContext dbContext,
        ITenantContext tenantContext,
        WorkspaceSettingsService settingsService,
        FxReferenceRateStore fxRates,
        WebResearchOptions webResearchOptions,
        CancellationToken cancellationToken)
    {
        var identity = callerIdentity.Resolve();
        if (identity is null)
        {
            return Results.Unauthorized();
        }

        if (!Guid.TryParse(tenantId, out var tenantGuid))
        {
            return Results.BadRequest("The tenant id in the route must be a GUID.");
        }

        var routeTenantId = new TenantId(tenantGuid);

        var callerRole = await ResolveMembershipRoleAsync(dbContext, tenantContext, routeTenantId, identity, cancellationToken)
            .ConfigureAwait(false);
        if (callerRole is null)
        {
            return Results.NotFound();
        }

        var settings = await settingsService.GetAsync(routeTenantId, cancellationToken).ConfigureAwait(false);
        if (settings is null)
        {
            return Results.NotFound();
        }

        var market = await fxRates.GetLatestAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(ToJson(settings, market, callerRole.Value, webResearchOptions));
    }

    private static async Task<IResult> PatchSettingsAsync(
        string tenantId,
        UpdateWorkspaceSettingsRequest? request,
        ICallerIdentity callerIdentity,
        IdentityWorkspaceDbContext dbContext,
        ITenantContext tenantContext,
        WorkspaceSettingsService settingsService,
        FxReferenceRateStore fxRates,
        WebResearchOptions webResearchOptions,
        CancellationToken cancellationToken)
    {
        var identity = callerIdentity.Resolve();
        if (identity is null)
        {
            return Results.Unauthorized();
        }

        if (!Guid.TryParse(tenantId, out var tenantGuid))
        {
            return Results.BadRequest("The tenant id in the route must be a GUID.");
        }

        var routeTenantId = new TenantId(tenantGuid);

        var callerRole = await ResolveMembershipRoleAsync(dbContext, tenantContext, routeTenantId, identity, cancellationToken)
            .ConfigureAwait(false);
        if (callerRole is null)
        {
            return Results.NotFound();
        }

        if (callerRole != WorkspaceRoleName.Admin)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (request is null || (request.WebResearchEnabled is null && request.ReportingCurrency is null
                && request.FiscalYearStartMonth is null && request.BudgetRates is null))
        {
            return Results.BadRequest(
                "The body must name at least one setting ('webResearchEnabled', 'reportingCurrency', 'fiscalYearStartMonth', 'budgetRates').");
        }

        var (settings, error) = await settingsService
            .UpdateAsync(routeTenantId, request, identity, cancellationToken)
            .ConfigureAwait(false);
        if (error is not null)
        {
            return Results.BadRequest(error);
        }

        if (settings is null)
        {
            return Results.NotFound();
        }

        var market = await fxRates.GetLatestAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(ToJson(settings, market, callerRole.Value, webResearchOptions));
    }

    private static object ToJson(
        WorkspaceSettingsResult settings, FxReferenceRates market, WorkspaceRoleName callerRole, WebResearchOptions webResearchOptions) => new
    {
        webResearchEnabled = settings.WebResearchEnabled,
        webResearchAvailable = webResearchOptions.Enabled,
        // The client shows the toggle only to a caller who can flip it; the role is the caller's
        // own membership, never a header.
        canEdit = callerRole == WorkspaceRoleName.Admin,
        // Spesa e risparmi: what every total is in, when the fiscal year starts (null: not set,
        // reported as a calendar year) and the Admin's budget rates (units of the reporting currency
        // per unit of each key); the currencies offered are the product's closed list.
        reportingCurrency = settings.ReportingCurrency,
        reportingCurrencySet = settings.ReportingCurrencySet,
        fiscalYearStartMonth = settings.FiscalYearStartMonth,
        budgetRates = settings.BudgetRates ?? new Dictionary<string, decimal>(),
        // The official reference rates (ECB) downloaded every morning, crossed into the reporting
        // currency: the only rates Spend and savings converts with.
        marketRates = market.RatesInto(settings.ReportingCurrency)
            .Where(kv => WorkspaceProvisioningService.SupportedCurrencies.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value),
        marketRatesDate = market.RateDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        marketRatesSource = market.RateDate is null ? null : "ECB",
        supportedCurrencies = WorkspaceProvisioningService.SupportedCurrencies.Order(StringComparer.Ordinal).ToList(),
    };

    private static async Task<WorkspaceRoleName?> ResolveMembershipRoleAsync(
        IdentityWorkspaceDbContext dbContext,
        ITenantContext tenantContext,
        TenantId tenantId,
        string callerIdentity,
        CancellationToken cancellationToken)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var roleNames = await (
            from user in dbContext.WorkspaceUsers
            join membership in dbContext.WorkspaceMemberships on user.Id equals membership.WorkspaceUserId
            join role in dbContext.WorkspaceRoles on membership.WorkspaceRoleId equals role.Id
            where user.TenantId == tenantId
                && membership.TenantId == tenantId
                && role.TenantId == tenantId
                && (user.Email == callerIdentity || user.ExternalSubjectId == callerIdentity)
            select role.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return WorkspaceRoleClaimResolver.TryResolve(roleNames.Select(name => name.ToString()), out var resolved)
            ? resolved
            : null;
    }
}
