namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// One page of <see cref="PortfolioQueryService.GetPortfolioAsync"/> results (task
/// E02/F03/US01/T02, us-01-portfolio-list-filters: "Add filters + pagination"). <see cref="Page"/>
/// and <see cref="PageSize"/> echo back the <see cref="PortfolioPageRequest"/> the caller sent —
/// including when <see cref="Items"/> comes back short of <see cref="PageSize"/> or empty (past
/// the last page) — so a caller can compute "next page exists" as
/// <c>Page * PageSize &lt; TotalCount</c> without guessing what was actually requested.
/// <see cref="TotalCount"/> is the number of rows matching the caller's
/// <see cref="PortfolioFilter"/> across every page, not just <see cref="Items"/> on this one.
/// </summary>
public sealed record PortfolioPage(
    IReadOnlyList<PortfolioListItem> Items,
    int Page,
    int PageSize,
    int TotalCount);
