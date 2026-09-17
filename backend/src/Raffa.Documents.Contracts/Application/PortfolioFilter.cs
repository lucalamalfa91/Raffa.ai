using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// Optional server-side filters for <see cref="PortfolioQueryService.GetPortfolioAsync"/>
/// (task E02/F03/US01/T01, us-01-portfolio-list-filters AC-2: "Filters by supplier/category/
/// renewal period/spend/status/risk/auto-renewal", product spec §8.1 "Filters" column).
///
/// <see cref="Category"/> (task E24/F01/US01/T01, story us-01-portfolio-category-backend, closes
/// NW-23/OQ-w17-007): the comment this replaces said Category was "deliberately not a member"
/// because Suppliers/Products was "still an empty scaffold project with no domain types". That is
/// now false — <c>Raffa.Suppliers.Products.Domain.Supplier.Category</c> exists and is persisted
/// (<c>Infrastructure.Configurations.SupplierConfiguration</c>). What still holds, unchanged, is
/// the architecture boundary that made this filter unusual in the first place:
/// <c>Raffa.ArchitectureTests.DependencyDirectionTests</c>'s allow-list for this module is still
/// exactly <c>[SharedKernel, AiGateway]</c>, so neither this type nor
/// <see cref="PortfolioQueryService"/> (which consumes it) can ever resolve a supplier's category
/// itself. This member only *carries* the requested value through; <c>Raffa.Api</c>'s
/// <c>PortfolioEndpointExtensions</c> — "the one project allowed to reference every module"
/// (ADR-002) — is the one place that can actually join it to a supplier and apply it, the same way
/// it already joins <c>supplierName</c> on (<c>ResolveSupplierNamesAsync</c>).
///
/// Every member is optional (null = not filtered), so a caller with no query parameters gets the
/// full tenant-scoped portfolio — <see cref="None"/> is that default.
/// </summary>
public sealed record PortfolioFilter(
    EntityId? SupplierId = null,
    string? Status = null,
    RiskSeverity? Risk = null,
    bool? AutoRenewal = null,
    decimal? MinAnnualSpend = null,
    decimal? MaxAnnualSpend = null,
    DateOnly? RenewalFrom = null,
    DateOnly? RenewalTo = null,
    string? Category = null)
{
    /// <summary>No filters applied — the full tenant-scoped portfolio.</summary>
    public static readonly PortfolioFilter None = new();
}
