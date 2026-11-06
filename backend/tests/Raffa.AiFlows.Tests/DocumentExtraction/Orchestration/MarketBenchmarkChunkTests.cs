using Raffa.AiFlows.DocumentExtraction.Orchestration;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Market;

namespace Raffa.AiFlows.Tests.DocumentExtraction.Orchestration;

/// <summary>
/// The knowledge the upload pipeline writes to the tenant RAG for a contract's market comparison:
/// each priced line next to its representative band (or its estimate, labelled as such), with the
/// delta vs the market median, so a retrieval over the contract finds the benchmark too.
/// </summary>
public sealed class MarketBenchmarkChunkTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());

    private static ContractLineItem Line(string description, decimal? unitPrice) => new()
    {
        TenantId = Tenant,
        ContractId = EntityId.New(),
        Description = description,
        UnitPrice = unitPrice,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void A_matched_line_and_an_estimated_line_are_both_written_with_their_provenance()
    {
        var matched = Line("Microsoft 365 E5 named users / licenses", 411m);
        var estimated = Line("Premier support", 1000m);
        var unpriced = Line("Onboarding fee", 18000m);
        var now = DateTimeOffset.UtcNow;
        var prices = new Dictionary<EntityId, LineItemMarketPrice>
        {
            [matched.Id] = new(matched.Id, true, "MKT-1", "Microsoft 365 E5", "CH", "CHF", 12, 340m, 400m, 430m, 300, "mock", now, now),
            [estimated.Id] = new(estimated.Id, false, null, null, null, null, null, null, null, null, null, null, null, now,
                Estimate: new MarketPriceEstimate(default, "CHF", 800m, 900m, 1000m, "similar support plans", null)),
            [unpriced.Id] = new(unpriced.Id, false, null, null, null, null, null, null, null, null, null, null, null, now),
        };

        var text = DocumentProcessingOrchestrator.MarketBenchmarkChunkText("CHF", [matched, estimated, unpriced], prices);

        Assert.NotNull(text);
        Assert.Contains("Microsoft 365 E5 named users / licenses: contract unit price 411 CHF; market P25 340 · P50 400 · P75 430 CHF", text);
        Assert.Contains("+2.8% vs market median", text);
        Assert.Contains("estimated market P25 800 · P50 900 · P75 1000 CHF", text);
        Assert.Contains("estimate, not market data", text);
        Assert.DoesNotContain("Onboarding fee", text);
    }

    [Fact]
    public void No_band_on_any_line_writes_no_chunk()
    {
        var line = Line("Onboarding fee", 18000m);
        var prices = new Dictionary<EntityId, LineItemMarketPrice>
        {
            [line.Id] = new(line.Id, false, null, null, null, null, null, null, null, null, null, null, null, DateTimeOffset.UtcNow),
        };

        Assert.Null(DocumentProcessingOrchestrator.MarketBenchmarkChunkText("CHF", [line], prices));
    }
}
