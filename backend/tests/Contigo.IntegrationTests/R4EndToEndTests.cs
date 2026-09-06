using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Contigo.Quotes.Application.Outcome;
using Contigo.Quotes.Application.Strategy;
using Contigo.Quotes.Domain;
using Contigo.Quotes.Infrastructure;
using Contigo.Savings.Application;
using Contigo.Savings.Domain;
using Contigo.Savings.Infrastructure;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Contigo.IntegrationTests;

/// <summary>
/// Proves the Definition of Done for task E05/F04/US01/T01 (r4-integration) and its parent story
/// us-01-final-integration: AC-1 ("Upload quote -> line items -> benchmark match -> market
/// assessment -> target range -> negotiation strategy"), AC-2 ("User can correct SKU matching before
/// accepting assessment") and AC-3 ("Record final outcome -> realized savings tracked") — driven
/// against the real, composed <c>Contigo.Api</c> host (see <see cref="R4IntegrationFixture"/>) and a
/// real, migrated Postgres+pgvector+RLS database, the same "one real host" shape
/// <see cref="R0EndToEndTests"/>/<see cref="R1EndToEndTests"/>/<see cref="R2EndToEndTests"/>/
/// <see cref="R3EndToEndTests"/> already established for R0-R3. Reuses
/// <see cref="R1EndToEndTests"/>'s <c>GetAsync</c>/<c>PostAsync</c>/<c>ParseAsync</c> helpers rather
/// than duplicating them (generic HTTP plumbing, not R1-specific — the same cross-class reuse every
/// later R*EndToEndTests already established).
///
/// <para>
/// <b>What is genuinely new here, versus every segment already proved in isolation</b>: this is the
/// first test to run quote upload (<c>Contigo.IntegrationTests.QuoteEndToEndTests</c>), market
/// assessment (<c>Contigo.Quotes.Tests.MarketAssessmentServiceTests</c>), negotiation strategy
/// (<c>Contigo.Quotes.Tests.NegotiationStrategyServiceTests</c>) and outcome capture/propagation
/// (<c>Contigo.IntegrationTests.NegotiationOutcomePropagationEndToEndTests</c>) as <b>one continuous
/// chain against the same quote</b>, through the real host, the way a procurement user actually
/// experiences the Day-1 promise (spec §20: "a new proposal can be assessed in minutes"). Doing so
/// surfaced — and this task fixes — two real gaps no prior, narrower test could see:
/// </para>
///
/// <list type="bullet">
/// <item>
/// <b><c>MarketAssessmentService</c>/<c>NegotiationStrategyService</c> never opened their own tenant
/// scope.</b> Every existing persistence test for both types (<c>MarketAssessmentServiceTests</c>,
/// <c>NegotiationStrategyServiceTests</c>) calls them from inside a test-provided
/// <c>ITenantContext.BeginScope</c> block, masking the gap — and <c>GET /api/quotes/{id}/assessment</c>
/// itself never opened one either. Against this fixture's real, unprivileged-role Postgres connection
/// (every prior R*IntegrationFixture's own posture — a superuser would have bypassed row security and
/// hidden this too), the endpoint would 404 for every real quote, always. Fixed in both services the
/// same way every other tenant-scoped application service in this codebase already does it ("owns its
/// own tenant scope rather than trusting one is already active") — see those types' own doc comments.
/// The same class of bug backend/README.md's own "R2 demo smoke test" section already names for
/// <c>RenewalThresholdScheduler.EvaluateThresholdsAsync</c>, now found and fixed for R4.
/// </item>
/// <item>
/// <b><c>GET /api/quotes/{id}/assessment</c> never actually serialized <c>quantity</c></b>, despite
/// <c>LineMarketAssessment.Quantity</c> existing exactly to be echoed here (its own doc comment:
/// "caller never has to re-fetch the line", the same treatment <c>unitPrice</c> already gets) and
/// despite backend/README.md's own HTTP surface table documenting <c>quantity</c> as part of this
/// response's shape since task E05/F02/US01/T02 (target-saving). See
/// <c>Contigo.Api.QuotesEndpointExtensions.GetAssessmentAsync</c>'s own inline comment.
/// </item>
/// </list>
///
/// <para>
/// <b>AC-2 ("User can correct SKU matching") update</b>: this task's own wave-spec <c>depends_on</c>
/// names <c>sku-recalculate</c> (task E05/F01/US02/T02, "Manual product mapping + recalculate
/// trigger"), which at the time this class was first written had not yet landed any code — like task
/// E04/F04/US01/T01 (r3-integration)'s own analogous, honestly documented gap for a missing
/// <c>Contract</c> -&gt; <c>BenchmarkQuery</c> mapping, this test used to stand in for the missing
/// endpoint with a direct <see cref="SkuProductMapping"/> insert + a direct
/// <c>Contigo.Quotes.Application.Normalization.SkuNormalizationService.NormalizeAsync</c> call
/// resolved from the host's own container.
/// Task E05/F01/US02/T02 has since landed <c>POST /api/quotes/{id}/assessment/recalculate</c>
/// (<c>Contigo.Api.QuotesEndpointExtensions.RecalculateAssessmentAsync</c> /
/// <c>Contigo.Quotes.Application.Normalization.SkuMappingService</c>) — this test now drives that
/// real endpoint over real HTTP instead, the same "one real host" posture every other step in this
/// chain already takes; <c>Contigo.Quotes.Tests.SkuMappingServiceTests</c> owns the endpoint's own,
/// narrower persistence-level proof (mapping upsert, cross-quote reuse, validation).
/// </para>
///
/// <para>
/// <b><c>NegotiationStrategyService</c> has no HTTP endpoint</b> (backend/README.md's own "Negotiation
/// Strategy" section: "not yet wired to an <c>AddQuotesModule</c>-registered HTTP endpoint...
/// <c>AddQuotesModule</c> registers the service so a future task/feature-04 (r4-integration) can call
/// it" — this is that task) — resolved directly from the real host's own container, the same "no
/// dedicated route exists yet, exercise the service the host resolves" convention
/// <see cref="R2EndToEndTests"/>/<see cref="R3EndToEndTests"/> already established for
/// <c>RenewalActionService</c>/<c>SavingsOpportunityService</c>/<c>IBenchmarkService</c>. Likewise
/// <see cref="SavingsOpportunityService.CreateAsync"/> ("identify") has no route either (same reason
/// <see cref="R3EndToEndTests"/> already resolves it directly).
/// </para>
/// </summary>
public sealed class R4EndToEndTests : IClassFixture<R4IntegrationFixture>
{
    private readonly R4IntegrationFixture _fixture;

    public R4EndToEndTests(R4IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Quote_check_day_one_path_runs_upload_correct_sku_assess_negotiate_and_capture_realized_outcome_end_to_end()
    {
        var client = _fixture.CreateClient();
        var tenantGuid = Guid.NewGuid();
        var tenantId = new TenantId(tenantGuid);

        // ----- AC-1 "Upload quote -> line items": POST /api/quotes -----
        var uploadBody = await UploadQuoteAsync(client, tenantGuid);
        Assert.Equal("Completed", uploadBody.GetProperty("processingStatus").GetString());
        Assert.Equal(1, uploadBody.GetProperty("lineItemCount").GetInt32());
        Assert.Equal("Salesforce", uploadBody.GetProperty("supplier").GetString());
        Assert.Equal("USD", uploadBody.GetProperty("currency").GetString());
        Assert.Equal("US", uploadBody.GetProperty("geography").GetString());
        // AC-2's "before" state: every tenant starts with zero SkuProductMapping rows, so the
        // auto-normalization QuoteExtractionPipeline already ran (quote-normalization,
        // sku-normalization) leaves this freshly-uploaded line honestly Unmatched.
        Assert.Equal(1, uploadBody.GetProperty("unmatchedSkuCount").GetInt32());
        // Honest, expected quote-normalization gap: "12 months" (the literal term text the fixture
        // benchmark catalog matches on) is outside QuoteBillingCadence's own small annualization
        // vocabulary (monthly/quarterly/semi-annual/annual) — never blocks assessment (see
        // MarketAssessmentQueryBuilder's own doc comment: raw Term/UnitPrice are used, not the
        // normalized figures), but is honestly reported as unresolved rather than silently ignored.
        Assert.Equal(0, uploadBody.GetProperty("normalizedLineItemCount").GetInt32());
        Assert.Equal(1, uploadBody.GetProperty("unresolvedNormalizationCount").GetInt32());
        var quoteId = new EntityId(uploadBody.GetProperty("id").GetGuid());

        EntityId quoteLineId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var tenantScope = tenantContext.BeginScope(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

            var line = await db.QuoteLines.SingleAsync(l => l.QuoteId == quoteId);
            quoteLineId = line.Id;

            Assert.Equal(SkuMatchStatus.Unmatched, line.MatchStatus);
            Assert.Equal(R4ExtractionFixtures.RawSku, line.NormalizedSku);
        }

        // ----- AC-2 "User can correct SKU matching before accepting assessment" -----
        // Task E05/F01/US02/T02 (sku-recalculate) — drives the real endpoint over real HTTP; see
        // this class's own doc comment for what this replaced.
        var recalculateResponse = await R1EndToEndTests.PostAsync(
            client, $"/api/quotes/{quoteId.Value}/assessment/recalculate", tenantGuid,
            new
            {
                mappings = new[]
                {
                    new
                    {
                        sku = R4ExtractionFixtures.RawSku,
                        canonicalSku = R4ExtractionFixtures.RawSku,
                        canonicalEdition = R4ExtractionFixtures.ExpectedEdition,
                        canonicalProductName = R4ExtractionFixtures.ProductDescription,
                    },
                },
            });
        Assert.Equal(HttpStatusCode.OK, recalculateResponse.StatusCode);
        var recalculateBody = await R1EndToEndTests.ParseAsync(recalculateResponse);

        Assert.Equal(1, recalculateBody.GetProperty("mappingsAppliedCount").GetInt32());
        var recalculateNormalization = recalculateBody.GetProperty("normalization");
        Assert.Equal(1, recalculateNormalization.GetProperty("matchedCount").GetInt32());
        Assert.Equal(0, recalculateNormalization.GetProperty("unmatchedCount").GetInt32());
        // AC-2 "Show unmatched SKUs": none left, now that the one line's SKU is mapped.
        Assert.Empty(recalculateBody.GetProperty("unmatchedLines").EnumerateArray());
        // AC-3 "Re-run assessment after mapping correction": the same assessment shape
        // GET .../assessment returns, already fresh in this same response.
        var recalculatedLine = Assert.Single(recalculateBody.GetProperty("assessment").GetProperty("lines").EnumerateArray());
        Assert.Equal("Assessed", recalculatedLine.GetProperty("status").GetString());

        using (var scope = _fixture.Services.CreateScope())
        {
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var tenantScope = tenantContext.BeginScope(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

            var line = await db.QuoteLines.SingleAsync(l => l.Id == quoteLineId);
            Assert.Equal(SkuMatchStatus.Matched, line.MatchStatus);
        }

        // ----- AC-1 continued: "-> benchmark match -> market assessment -> target range" -----
        // GET /api/quotes/{id}/assessment, over real HTTP — exactly the endpoint this task's own
        // real-Postgres proof surfaced as always-404 before the MarketAssessmentService tenant-scope
        // fix (see that type's own doc comment).
        var assessmentResponse = await R1EndToEndTests.GetAsync(
            client, $"/api/quotes/{quoteId.Value}/assessment", tenantGuid);
        Assert.Equal(HttpStatusCode.OK, assessmentResponse.StatusCode);
        var assessmentBody = await R1EndToEndTests.ParseAsync(assessmentResponse);

        var assessedLine = Assert.Single(assessmentBody.GetProperty("lines").EnumerateArray());
        Assert.Equal(quoteLineId.Value, assessedLine.GetProperty("quoteLineId").GetGuid());
        Assert.Equal("Assessed", assessedLine.GetProperty("status").GetString());
        Assert.Equal("AboveMarket", assessedLine.GetProperty("position").GetString());
        Assert.Equal(R4ExtractionFixtures.QuotedUnitPrice, assessedLine.GetProperty("unitPrice").GetDecimal());
        // This task's own fix: `quantity` was documented in backend/README.md's HTTP surface table
        // but never actually serialized — see QuotesEndpointExtensions.GetAssessmentAsync's own
        // inline comment.
        Assert.Equal(R4ExtractionFixtures.Quantity, assessedLine.GetProperty("quantity").GetDecimal());

        var benchmark = assessedLine.GetProperty("benchmark");
        Assert.True(benchmark.GetProperty("hasSufficientData").GetBoolean());
        var distribution = benchmark.GetProperty("distribution");
        Assert.Equal(1500m, distribution.GetProperty("p25").GetDecimal());
        Assert.Equal(1800m, distribution.GetProperty("p50").GetDecimal());
        Assert.Equal(2100m, distribution.GetProperty("p75").GetDecimal());
        Assert.Equal("per seat / year", benchmark.GetProperty("metric").GetString());
        Assert.Equal("USD", benchmark.GetProperty("currency").GetString());

        var confidence = assessedLine.GetProperty("confidence");
        Assert.Equal("High", confidence.GetProperty("level").GetString());
        var confidenceScore = confidence.GetProperty("score").GetDouble();
        // Seven of the seven baseline dimensions matched (Sku itself is present on the query — this
        // line carries the now-corrected SKU — but the matched fixture carries no Sku at all, so it
        // never counts as an eighth matched dimension) at full sample-size confidence: 7/8 * 1.0.
        Assert.InRange(confidenceScore, 0.85, 0.90);
        Assert.Equal("fixture", confidence.GetProperty("source").GetString());
        Assert.Equal(512, confidence.GetProperty("sampleSize").GetInt32());
        var comparisonDimensions = confidence.GetProperty("comparisonDimensions")
            .EnumerateArray().Select(d => d.GetString()).ToList();
        Assert.Contains("Supplier", comparisonDimensions);
        Assert.Contains("Product", comparisonDimensions);
        Assert.Contains("Geography", comparisonDimensions);
        Assert.Contains("Currency", comparisonDimensions);
        Assert.Contains("ContractTerm", comparisonDimensions);
        Assert.Contains("QuantityTier", comparisonDimensions);
        Assert.Contains("PurchaseDate", comparisonDimensions);
        // AC-1 (us-01-benchmark-interface) "never matches on supplier name alone" — always more than
        // one dimension, and the fixture's own Sku-less catalog row never contributes an eighth.
        Assert.True(comparisonDimensions.Count > 1);
        Assert.DoesNotContain("Sku", comparisonDimensions);

        var targetSaving = assessedLine.GetProperty("targetSaving");
        Assert.Equal(1500m, targetSaving.GetProperty("recommendedTargetLow").GetDecimal());
        Assert.Equal(1800m, targetSaving.GetProperty("recommendedTargetHigh").GetDecimal());
        Assert.Equal(400m, targetSaving.GetProperty("savingsRangeLow").GetDecimal());
        Assert.Equal(700m, targetSaving.GetProperty("savingsRangeHigh").GetDecimal());
        Assert.Equal(40_000m, targetSaving.GetProperty("totalSavingsRangeLow").GetDecimal());
        Assert.Equal(70_000m, targetSaving.GetProperty("totalSavingsRangeHigh").GetDecimal());

        // ----- AC-1 continued: "-> negotiation strategy" -----
        QuoteNegotiationStrategy strategy;
        using (var scope = _fixture.Services.CreateScope())
        {
            var negotiationStrategyService = scope.ServiceProvider.GetRequiredService<NegotiationStrategyService>();
            var strategyResult = await negotiationStrategyService.GenerateAsync(tenantId, quoteId);
            Assert.True(strategyResult.IsSuccess);
            strategy = strategyResult.Value;
        }

        Assert.Equal(quoteId, strategy.QuoteId);
        var lineStrategy = Assert.Single(strategy.Lines);
        Assert.Equal(quoteLineId, lineStrategy.QuoteLineId);
        Assert.Equal(1200m, lineStrategy.OpeningTarget);
        Assert.Equal(1500m, lineStrategy.AcceptableRangeLow);
        Assert.Equal(1800m, lineStrategy.AcceptableRangeHigh);
        Assert.Equal(2100m, lineStrategy.WalkAwayThreshold);
        Assert.Equal(7, lineStrategy.Levers.Count);

        // strategy-evidence (task E05/F03/US01/T02, AC-2 "Rationale cites explicit evidence per
        // lever"): the four grounded levers carry real, structured citations back onto this same
        // line's own extraction; the three ungrounded ones stay honestly evidence-empty.
        var volumeLever = Assert.Single(lineStrategy.Levers, l => l.LeverType == NegotiationLeverType.Volume);
        Assert.Contains(volumeLever.Evidence, e => e.FieldName == "QuoteLine.Quantity" && e.Value == "100");
        Assert.Contains(volumeLever.Evidence, e => e.FieldName == "QuoteLine.Unit" && e.Value == "seat");

        var termLever = Assert.Single(lineStrategy.Levers, l => l.LeverType == NegotiationLeverType.Term);
        var termEvidence = Assert.Single(termLever.Evidence);
        Assert.Equal("QuoteLine.Term", termEvidence.FieldName);
        Assert.Equal("12 months", termEvidence.Value);

        var bundleLever = Assert.Single(lineStrategy.Levers, l => l.LeverType == NegotiationLeverType.Bundle);
        var bundleEvidence = Assert.Single(bundleLever.Evidence);
        Assert.Equal("Quote.LineCount", bundleEvidence.FieldName);
        Assert.Equal("1", bundleEvidence.Value);

        Assert.Empty(Assert.Single(lineStrategy.Levers, l => l.LeverType == NegotiationLeverType.Utilization).Evidence);
        Assert.Empty(Assert.Single(lineStrategy.Levers, l => l.LeverType == NegotiationLeverType.Alternatives).Evidence);
        Assert.Empty(Assert.Single(lineStrategy.Levers, l => l.LeverType == NegotiationLeverType.PaymentTerms).Evidence);

        // ----- AC-3 "Record final outcome -> realized savings tracked" -----
        // Identify a real, trackable SavingsOpportunity from this same line's own just-computed
        // target-saving numbers (SavingsOpportunityService.CreateAsync has no HTTP route yet — see
        // this class's own doc comment).
        var currentTotalCost = R4ExtractionFixtures.QuotedUnitPrice * R4ExtractionFixtures.Quantity;
        Assert.Equal(220_000m, currentTotalCost);

        EntityId savingsOpportunityId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var savingsOpportunityService = scope.ServiceProvider.GetRequiredService<SavingsOpportunityService>();
            var created = await savingsOpportunityService.CreateAsync(
                tenantId,
                new CreateSavingsOpportunityRequest(
                    SupplierId: EntityId.New(),
                    ContractId: EntityId.New(),
                    Type: "quote-benchmark-comparison",
                    CurrentSpend: currentTotalCost,
                    Currency: "USD",
                    EstimatedSavingsLow: 40_000m,
                    EstimatedSavingsHigh: 70_000m,
                    Confidence: confidenceScore));

            Assert.True(created.IsSuccess);
            Assert.Equal(SavingsOpportunityStatus.Identified, created.Value.Status);
            savingsOpportunityId = created.Value.Id;
        }

        // Negotiate the quote down from its original 220,000 total to 190,000 — a real, non-trivial
        // saving inside the acceptable range this same strategy just recommended (150,000-180,000
        // total) — then capture the outcome over real HTTP.
        const decimal finalTotal = 190_000m;
        var expectedCalculation = NegotiationOutcomeCalculator.Compute(currentTotalCost, finalTotal);

        var captureResponse = await R1EndToEndTests.PostAsync(
            client, "/api/negotiations/outcomes", tenantGuid,
            new
            {
                quoteId = quoteId.Value,
                originalQuoteTotal = currentTotalCost,
                targetPrice = lineStrategy.AcceptableRangeHigh!.Value * R4ExtractionFixtures.Quantity,
                finalPrice = finalTotal,
                negotiationDurationDays = 10,
                leversUsed = new[] { "Volume", "Term" },
                savingsOpportunityId = savingsOpportunityId.Value,
            });
        Assert.Equal(HttpStatusCode.Created, captureResponse.StatusCode);
        var captureBody = await R1EndToEndTests.ParseAsync(captureResponse);

        Assert.Equal(180_000m, captureBody.GetProperty("targetPrice").GetDecimal());
        Assert.Equal(expectedCalculation.RealizedSaving, captureBody.GetProperty("realizedSaving").GetDecimal());
        Assert.Equal(expectedCalculation.DiscountPercent, captureBody.GetProperty("discountPercent").GetDecimal());
        Assert.Equal(savingsOpportunityId.Value, captureBody.GetProperty("savingsOpportunityId").GetGuid());
        Assert.True(captureBody.GetProperty("savingsPropagated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, captureBody.GetProperty("savingsPropagationError").ValueKind);

        // "Realized savings tracked": the SavingsOpportunity this task's own quote -> assessment ->
        // strategy chain identified is now Realized, with a real, audit-tracked RealizedSavings row —
        // proven against the real database, the same verification shape
        // NegotiationOutcomePropagationEndToEndTests already established for a hand-seeded quote.
        using (var scope = _fixture.Services.CreateScope())
        {
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var tenantScope = tenantContext.BeginScope(tenantId);
            var savingsDb = scope.ServiceProvider.GetRequiredService<SavingsDbContext>();

            var opportunity = await savingsDb.SavingsOpportunities.SingleAsync(o => o.Id == savingsOpportunityId);
            Assert.Equal(SavingsOpportunityStatus.Realized, opportunity.Status);

            var realized = await savingsDb.RealizedSavingsRecords
                .SingleAsync(r => r.SavingsOpportunityId == savingsOpportunityId);
            Assert.Equal(expectedCalculation.RealizedSaving, realized.Amount);
            Assert.Equal("USD", realized.Currency);
        }

        // The savings dashboard KPI rollup reflects the realized opportunity too (spec §20's own
        // "and where we can save money" made checkable on the procurement homepage).
        var kpisResponse = await R1EndToEndTests.GetAsync(client, "/api/savings/kpis", tenantGuid);
        Assert.Equal(HttpStatusCode.OK, kpisResponse.StatusCode);
        var kpisBody = await R1EndToEndTests.ParseAsync(kpisResponse);
        var realizedBucket = Assert.Single(kpisBody.GetProperty("savingsRealized").EnumerateArray());
        Assert.Equal("USD", realizedBucket.GetProperty("currency").GetString());
        Assert.Equal(40_000m, realizedBucket.GetProperty("low").GetDecimal());
        Assert.Equal(70_000m, realizedBucket.GetProperty("high").GetDecimal());
        Assert.Equal(1, realizedBucket.GetProperty("count").GetInt32());
    }

    /// <summary>
    /// Uploads <see cref="R4ExtractionFixtures.BuildBornDigitalQuoteBytes"/> through the real `POST
    /// /api/quotes` endpoint, with the four optional <c>supplier</c>/<c>currency</c>/<c>geography</c>/
    /// <c>purchaseDate</c> form fields task E05/F02/US01/T01 (market-assessment) added so the
    /// resulting <c>Quote</c> is actually matchable — <see cref="QuoteEndToEndTests.UploadQuoteAsync"/>
    /// never sets any of the four (its own scope is upload/extraction only), so this is its own,
    /// deliberately R4-specific variant rather than a reuse. Same explicit
    /// <see cref="MediaTypeHeaderValue"/> requirement as that method's own doc comment: without it,
    /// the whole native-text-extraction path this test relies on (never routing through the `ocr`
    /// gateway role) would silently take the wrong branch. <see langword="internal"/>, not
    /// <see langword="private"/> — the same "generic-enough-to-reuse" visibility
    /// <see cref="R1EndToEndTests"/>'s own HTTP helpers already get — so
    /// <see cref="R4CrossTenantIsolationTests"/> can seed a real, uploaded quote for tenant A without
    /// duplicating this multipart-construction logic.
    /// </summary>
    internal static async Task<JsonElement> UploadQuoteAsync(HttpClient client, Guid tenantId)
    {
        var fileContent = new ByteArrayContent(R4ExtractionFixtures.BuildBornDigitalQuoteBytes());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(R4ExtractionFixtures.BornDigitalMimeType);

        using var multipart = new MultipartFormDataContent
        {
            { fileContent, "file", R4ExtractionFixtures.BornDigitalFileName },
            { new StringContent("Salesforce"), "supplier" },
            { new StringContent("USD"), "currency" },
            { new StringContent("US"), "geography" },
            // Well within FixtureBenchmarkAdapter.PurchaseDateWindowDays (400) of the matched
            // catalog row's own UpdatedAt (2026-06-15).
            { new StringContent("2026-07-01"), "purchaseDate" },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/quotes") { Content = multipart };
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
