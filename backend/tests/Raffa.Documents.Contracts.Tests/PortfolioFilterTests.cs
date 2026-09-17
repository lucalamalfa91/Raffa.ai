using Raffa.Documents.Contracts.Application;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// Unit coverage for task E24/F01/US01/T01 (story us-01-portfolio-category-backend AC-2), closing
/// NW-23/OQ-w17-007: <see cref="PortfolioFilter.Category"/> defaults to <see langword="null"/> on
/// <see cref="PortfolioFilter.None"/> -- the full, unfiltered tenant-scoped portfolio -- and
/// round-trips whatever value a caller supplies. This record has no parsing logic of its own;
/// <c>Raffa.Api.PortfolioEndpointExtensions.TryParseFilter</c> owns turning `?category=` into this
/// value (proven by <c>Raffa.Api.Tests.PortfolioEndpointTests</c>'s own category tests over real
/// HTTP), so this class is deliberately scoped to the member's default/round-trip contract alone.
/// </summary>
public sealed class PortfolioFilterTests
{
    [Fact]
    public void None_leaves_category_null()
    {
        Assert.Null(PortfolioFilter.None.Category);
    }

    [Fact]
    public void Default_constructor_leaves_category_null_like_every_other_optional_filter()
    {
        Assert.Null(new PortfolioFilter().Category);
    }

    [Fact]
    public void Category_round_trips_the_supplied_value()
    {
        var filter = new PortfolioFilter(Category: "SaaS");

        Assert.Equal("SaaS", filter.Category);
    }
}
