using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// Optional server-side filters for <see cref="PortfolioQueryService.GetPortfolioAsync"/>
/// (task E02/F03/US01/T01, us-01-portfolio-list-filters AC-2: "Filters by supplier/category/
/// renewal period/spend/status/risk/auto-renewal", product spec §8.1 "Filters" column).
///
/// "Category" is deliberately not a member here. No Category concept exists anywhere in the
/// currently-implemented schema this task depends on (wave-spec `contract-schema`): the
/// Suppliers/Products bounded context that would own it (ADR-002 module map) is still an empty
/// scaffold project with no domain types (see `backend/README.md`'s solution layout), and
/// <c>Raffa.Documents.Contracts</c> is architecturally forbidden from referencing it directly
/// (<c>Raffa.ArchitectureTests.DependencyDirectionTests</c>'s allow-list for this module is
/// exactly <c>[SharedKernel, AiGateway]</c>). A follow-up task adds the Category filter once
/// Suppliers/Products exists and defines what a contract's category is.
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
    DateOnly? RenewalTo = null)
{
    /// <summary>No filters applied — the full tenant-scoped portfolio.</summary>
    public static readonly PortfolioFilter None = new();
}
