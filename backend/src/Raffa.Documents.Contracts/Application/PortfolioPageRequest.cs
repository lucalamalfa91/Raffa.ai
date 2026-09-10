namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// Caller-supplied paging for <see cref="PortfolioQueryService.GetPortfolioAsync"/> (task
/// E02/F03/US01/T02, us-01-portfolio-list-filters: "Add filters + pagination"). <see cref="Page"/>
/// is 1-based. <c>Raffa.Api.PortfolioEndpointExtensions.TryParsePage</c> rejects an
/// out-of-range <see cref="Page"/>/<see cref="PageSize"/> with a 400 before this type is ever
/// constructed from a request — same "reject, don't clamp" convention as
/// <see cref="PortfolioFilter"/>, which is likewise not self-validating — so
/// <see cref="PortfolioQueryService"/> trusts both values here.
/// </summary>
public sealed record PortfolioPageRequest(int Page = 1, int PageSize = PortfolioPageRequest.DefaultPageSize)
{
    /// <summary>Page size when the caller supplies no <c>pageSize</c> query parameter.</summary>
    public const int DefaultPageSize = 25;

    /// <summary>
    /// Largest <see cref="PageSize"/> the endpoint accepts. <see cref="PortfolioQueryService"/>
    /// still materializes its whole filtered result set before slicing a page off it (see that
    /// class's doc comment on why paging can't be pushed to SQL while a
    /// <see cref="PortfolioFilter.Risk"/> filter is active) — this cap bounds how much of that
    /// already-materialized set one request can ask to have serialized back out.
    /// </summary>
    public const int MaxPageSize = 100;

    /// <summary>First page at the default size — what a caller gets with no paging query parameters.</summary>
    public static readonly PortfolioPageRequest Default = new();
}
