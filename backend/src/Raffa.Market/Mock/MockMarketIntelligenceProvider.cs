using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Raffa.Market.Contracts;
using Raffa.SharedKernel;

namespace Raffa.Market.Mock;

/// <summary>
/// R-MKT-01/R-MKT-02's mock feed: the checked-in, labelled *representative* dataset (ADR-001's
/// amendment: "The Internal Dataset is now the mock market-intelligence feed") that stands in for
/// a live third-party market-intelligence API on the first V2 `demo` (ADR-024; brief §11 "no paid
/// external benchmark API dependency for the first demo"). Never a real provider's licensed data —
/// every record is hand-curated, labelled <c>source = "mock"</c> / <c>representative = true</c>
/// (R-MKT-02 AC), and the dataset itself is deliberately worldwide/illustrative (enterprise
/// software, insurance incl. Allianz, facilities, telco, logistics, professional services).
///
/// Reads <c>backend/fixtures/market-intelligence.mock.json</c> — <b>embedded as a resource</b> in
/// this assembly (see <c>Raffa.Market.csproj</c>'s <c>EmbeddedResource</c> item) rather than
/// opened from a runtime file path. A class library has no reliable notion of "current directory"
/// or "content root" independent of whatever host loads it (API, Worker, a future
/// `seed-market-intelligence` CI job/console tool, or this project's own tests) — embedding keeps
/// the checked-in JSON travelling with the compiled assembly, so every one of those hosts reads
/// the exact same bytes with zero path configuration and zero dependency on a Dockerfile `COPY`
/// step that does not exist yet (`reports/open-questions.md` OQ-impl-005: no backend Dockerfile at
/// all yet). The file on disk remains the single source of truth an operator edits; the embedded
/// copy is simply how this type reads it.
///
/// Parses the fixture once (<see cref="Fixture"/>, a lazily-initialized static) and reuses the
/// same immutable <see cref="MarketDeal"/> list for every call — the mock feed never changes at
/// runtime, so there is nothing to gain by re-reading/re-parsing the embedded resource on every
/// <see cref="GetDealsAsync"/> call.
/// </summary>
public sealed class MockMarketIntelligenceProvider : IMarketIntelligenceProvider
{
    private const string EmbeddedResourceName = "Raffa.Market.Fixtures.market-intelligence.mock.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly Lazy<FixtureDocument> Fixture = new(LoadFixture);

    /// <inheritdoc/>
    public Task<Result<MarketFeedSnapshot>> GetDealsAsync(
        string? feedVersion = null, CancellationToken cancellationToken = default)
    {
        var fixture = Fixture.Value;

        if (feedVersion is not null && !string.Equals(feedVersion, fixture.FeedVersion, StringComparison.Ordinal))
        {
            return Task.FromResult(Result<MarketFeedSnapshot>.Failure(
                $"Mock market feed has only version '{fixture.FeedVersion}'; '{feedVersion}' is " +
                "not available (the mock provider carries no historical versions — R-MKT-05's " +
                "live provider is expected to support real historical lookups)."));
        }

        return Task.FromResult(Result<MarketFeedSnapshot>.Success(
            new MarketFeedSnapshot(fixture.Deals, fixture.FeedVersion)));
    }

    private static FixtureDocument LoadFixture()
    {
        var assembly = typeof(MockMarketIntelligenceProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded market-intelligence fixture '{EmbeddedResourceName}' was not found in " +
                $"{assembly.GetName().Name}. Check the EmbeddedResource item in Raffa.Market.csproj.");

        var document = JsonSerializer.Deserialize<FixtureDocument>(stream, JsonOptions)
            ?? throw new InvalidOperationException(
                "backend/fixtures/market-intelligence.mock.json deserialized to null.");

        if (document.Deals.Count == 0)
        {
            throw new InvalidOperationException(
                "backend/fixtures/market-intelligence.mock.json has zero deals — R-MKT-02 requires " +
                "at least 60 checked-in records.");
        }

        return document;
    }

    /// <summary>Wire shape of the checked-in fixture file — a feed version plus every deal.</summary>
    private sealed record FixtureDocument(
        [property: JsonPropertyName("feedVersion")] string FeedVersion,
        [property: JsonPropertyName("deals")] IReadOnlyList<MarketDeal> Deals);
}
