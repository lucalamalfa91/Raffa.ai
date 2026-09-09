using System.Globalization;
using Contigo.Documents.Contracts.Application;
using Contigo.Documents.Contracts.Domain;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Suppliers;
using Contigo.SharedKernel.Tenancy;

namespace Contigo.Api;

/// <summary>
/// Maps `GET /api/contracts` (product spec API table row "Portfolio list/filter"; story
/// us-01-portfolio-list-filters AC-1/AC-2/AC-3, task E02/F03/US01/T01; pagination added by task
/// E02/F03/US01/T02). Thin composition per ADR-002 — the actual decisions are made by
/// <see cref="PortfolioQueryService"/>; this file only parses the query string into a
/// <see cref="PortfolioFilter"/>/<see cref="PortfolioPageRequest"/> and maps each outcome to an
/// HTTP status code.
///
/// Same interim-authentication placeholder as <c>Program</c>'s document endpoints: ADR-010 is not
/// in this task's "architecture decisions in force" list, so there is still no validated caller
/// principal to take the tenant from. The tenant is taken from an explicit <c>X-Tenant-Id</c>
/// header instead of a token claim — see <c>Program.cs</c>'s own comment on why this gap is not
/// promoted to reports/open-questions.md by this task (a mid-wave append there has previously
/// broken a phase-barrier merge).
///
/// <para>
/// Task E13/F03/US01/T02 (requirements R-SUP-04, ADR-024 "the name is used, never a bare
/// SupplierId guid") adds <c>supplierName</c> to every row, resolved through
/// <see cref="ISupplierNameLookup"/>. The composition can only happen here: ADR-002 forbids
/// <c>Contigo.Documents.Contracts</c> from referencing <c>Contigo.Suppliers.Products</c>, so
/// <see cref="PortfolioListItem"/> carries the id and <c>Contigo.Api</c> — "the one project allowed
/// to reference every module" — joins the name on. One batched call per page, never one query per
/// row (the port is batched by design); <c>supplierId</c> stays in the response so a caller can
/// still filter by it (<c>?supplierId=</c>) and so a contract whose supplier row has since
/// disappeared reads as a present id with a <see langword="null"/> name rather than silently losing
/// both.
/// </para>
/// </summary>
public static class PortfolioEndpointExtensions
{
    public static IEndpointRouteBuilder MapPortfolioEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/contracts", GetPortfolioAsync);
        return endpoints;
    }

    private static async Task<IResult> GetPortfolioAsync(
        HttpRequest request,
        PortfolioQueryService portfolioQueryService,
        ISupplierNameLookup supplierNameLookup,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeaderValues)
            || !Guid.TryParse(tenantHeaderValues.ToString(), out var tenantGuid))
        {
            return Results.BadRequest("A valid 'X-Tenant-Id' header (a GUID) is required.");
        }

        if (!TryParseFilter(request.Query, out var filter, out var error))
        {
            return Results.BadRequest(error);
        }

        if (!TryParsePage(request.Query, out var page, out var pageError))
        {
            return Results.BadRequest(pageError);
        }

        var tenantId = new TenantId(tenantGuid);

        var result = await portfolioQueryService
            .GetPortfolioAsync(tenantId, filter, page, cancellationToken)
            .ConfigureAwait(false);

        var supplierNames = await ResolveSupplierNamesAsync(
            tenantId, result.Items, supplierNameLookup, tenantContext, cancellationToken).ConfigureAwait(false);

        // Enum members are projected to their string names for the wire contract — the same
        // convention Program.cs already uses for DocumentType/ProcessingStatus on
        // GET /api/documents/{id} — rather than the JSON serializer's numeric default. Items are
        // wrapped with paging metadata (task E02/F03/US01/T02) instead of returned as a bare
        // array, so a caller can render "showing X of Y" / know whether another page exists.
        return Results.Ok(new
        {
            items = result.Items.Select(item => new
            {
                contractId = item.ContractId,
                supplierId = item.SupplierId,
                supplierName = LookupSupplierName(supplierNames, item.SupplierId),
                type = item.Type.ToString(),
                annualSpend = item.AnnualSpend,
                startDate = item.StartDate,
                endDate = item.EndDate,
                renewalDate = item.RenewalDate,
                cancellationDeadline = item.CancellationDeadline,
                autoRenewal = item.AutoRenewal,
                status = item.Status,
                risk = item.Risk?.ToString(),
            }),
            page = result.Page,
            pageSize = result.PageSize,
            totalCount = result.TotalCount,
        });
    }

    /// <summary>
    /// Batch-resolves every distinct supplier id on a portfolio page to its display name (task
    /// E13/F03/US01/T02, R-SUP-04). <see langword="internal"/> rather than private because
    /// <see cref="RenewalsEndpointExtensions"/> composes the very same
    /// <see cref="PortfolioQueryService"/> page into its own response and needs the identical join
    /// — one shared helper beats two copies drifting apart, and both live in
    /// <c>Contigo.Api</c>, the only project allowed to see both modules at once.
    /// </summary>
    internal static Task<IReadOnlyDictionary<EntityId, string>> ResolveSupplierNamesAsync(
        TenantId tenantId,
        IReadOnlyList<PortfolioListItem> items,
        ISupplierNameLookup supplierNameLookup,
        ITenantContext tenantContext,
        CancellationToken cancellationToken) =>
        ResolveSupplierNamesAsync(
            tenantId,
            [.. items.Where(item => item.SupplierId is not null)
                .Select(item => new EntityId(item.SupplierId!.Value))
                .Distinct()],
            supplierNameLookup,
            tenantContext,
            cancellationToken);

    /// <summary>
    /// The one place any endpoint in this host calls <see cref="ISupplierNameLookup"/>, because it
    /// is the one place the ambient tenant claim is opened around that call.
    /// <c>Contigo.Suppliers.Products</c>'s <c>SuppliersDbContext</c> is RLS-scoped through
    /// <see cref="ITenantContext"/> (ADR-009), and — unlike <see cref="PortfolioQueryService"/> and
    /// its siblings, which each own their scope internally — the lookup itself does not open one:
    /// called outside a scope it returns an <em>empty</em> map, so a missing scope would surface as
    /// "this contract has no supplier name" rather than an error. Same
    /// <see cref="ITenantContext.BeginScope"/>-around-the-call shape
    /// <see cref="AskCopilotService"/> already uses for this exact port; nesting inside a scope a
    /// query service opens for itself is harmless.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<EntityId, string>> ResolveSupplierNamesAsync(
        TenantId tenantId,
        IReadOnlyCollection<EntityId> supplierIds,
        ISupplierNameLookup supplierNameLookup,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        // Skips the round trip entirely for a page with no linked suppliers, matching
        // AskCopilotService's own use of this port.
        if (supplierIds.Count == 0)
        {
            return new Dictionary<EntityId, string>();
        }

        using var tenantScope = tenantContext.BeginScope(tenantId);

        return await supplierNameLookup.GetNamesAsync(tenantId, supplierIds, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The display name for one row's supplier, or <see langword="null"/> when the row has
    /// no supplier at all or its id no longer resolves for this tenant (see
    /// <see cref="ISupplierNameLookup.GetNamesAsync"/>'s own "never a fabricated placeholder name"
    /// contract).</summary>
    internal static string? LookupSupplierName(IReadOnlyDictionary<EntityId, string> supplierNames, Guid? supplierId) =>
        supplierId is { } id && supplierNames.TryGetValue(new EntityId(id), out var name) ? name : null;

    /// <summary>
    /// Parses the AC-2 filter query parameters (supplierId, status, risk, autoRenewal,
    /// minAnnualSpend, maxAnnualSpend, renewalFrom, renewalTo). No "category" parameter exists —
    /// see <see cref="PortfolioFilter"/>'s own doc comment for why. Every parameter is optional;
    /// an absent one leaves the corresponding <see cref="PortfolioFilter"/> member null (not
    /// filtered). Returns false with a caller-facing <paramref name="error"/> on the first
    /// malformed value found.
    /// </summary>
    private static bool TryParseFilter(
        IQueryCollection query, out PortfolioFilter filter, out string error)
    {
        filter = PortfolioFilter.None;
        error = string.Empty;

        EntityId? supplierId = null;
        if (query.TryGetValue("supplierId", out var supplierIdValues))
        {
            if (!Guid.TryParse(supplierIdValues.ToString(), out var supplierGuid))
            {
                error = "'supplierId' must be a GUID.";
                return false;
            }

            supplierId = new EntityId(supplierGuid);
        }

        string? status = query.TryGetValue("status", out var statusValues)
            ? statusValues.ToString()
            : null;

        RiskSeverity? risk = null;
        if (query.TryGetValue("risk", out var riskValues))
        {
            if (!Enum.TryParse<RiskSeverity>(riskValues.ToString(), ignoreCase: true, out var parsedRisk))
            {
                error = "'risk' must be one of Low, Medium, High, Critical.";
                return false;
            }

            risk = parsedRisk;
        }

        bool? autoRenewal = null;
        if (query.TryGetValue("autoRenewal", out var autoRenewalValues))
        {
            if (!bool.TryParse(autoRenewalValues.ToString(), out var parsedAutoRenewal))
            {
                error = "'autoRenewal' must be 'true' or 'false'.";
                return false;
            }

            autoRenewal = parsedAutoRenewal;
        }

        if (!TryParseOptionalDecimal(query, "minAnnualSpend", out var minAnnualSpend, out error)
            || !TryParseOptionalDecimal(query, "maxAnnualSpend", out var maxAnnualSpend, out error))
        {
            return false;
        }

        if (!TryParseOptionalDate(query, "renewalFrom", out var renewalFrom, out error)
            || !TryParseOptionalDate(query, "renewalTo", out var renewalTo, out error))
        {
            return false;
        }

        filter = new PortfolioFilter(
            supplierId, status, risk, autoRenewal, minAnnualSpend, maxAnnualSpend, renewalFrom, renewalTo);
        return true;
    }

    /// <summary>
    /// Parses the task E02/F03/US01/T02 paging query parameters (<c>page</c>, <c>pageSize</c>).
    /// Both are optional; an absent one takes <see cref="PortfolioPageRequest.Default"/>'s value.
    /// Returns false with a caller-facing <paramref name="error"/> for a non-positive <c>page</c>
    /// or a <c>pageSize</c> outside <c>[1, <see cref="PortfolioPageRequest.MaxPageSize"/>]</c> —
    /// same "reject, don't clamp" convention as <see cref="TryParseFilter"/>.
    /// </summary>
    private static bool TryParsePage(IQueryCollection query, out PortfolioPageRequest page, out string error)
    {
        page = PortfolioPageRequest.Default;
        error = string.Empty;

        var pageNumber = PortfolioPageRequest.Default.Page;
        if (query.TryGetValue("page", out var pageValues))
        {
            if (!int.TryParse(pageValues.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out pageNumber)
                || pageNumber < 1)
            {
                error = "'page' must be a positive integer.";
                return false;
            }
        }

        var pageSize = PortfolioPageRequest.DefaultPageSize;
        if (query.TryGetValue("pageSize", out var pageSizeValues))
        {
            if (!int.TryParse(pageSizeValues.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out pageSize)
                || pageSize < 1
                || pageSize > PortfolioPageRequest.MaxPageSize)
            {
                error = $"'pageSize' must be an integer between 1 and {PortfolioPageRequest.MaxPageSize}.";
                return false;
            }
        }

        page = new PortfolioPageRequest(pageNumber, pageSize);
        return true;
    }

    private static bool TryParseOptionalDecimal(
        IQueryCollection query, string name, out decimal? value, out string error)
    {
        value = null;
        error = string.Empty;

        if (!query.TryGetValue(name, out var values))
        {
            return true;
        }

        if (!decimal.TryParse(values.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            error = $"'{name}' must be a number.";
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryParseOptionalDate(
        IQueryCollection query, string name, out DateOnly? value, out string error)
    {
        value = null;
        error = string.Empty;

        if (!query.TryGetValue(name, out var values))
        {
            return true;
        }

        if (!DateOnly.TryParse(values.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            error = $"'{name}' must be a date (yyyy-MM-dd).";
            return false;
        }

        value = parsed;
        return true;
    }
}
