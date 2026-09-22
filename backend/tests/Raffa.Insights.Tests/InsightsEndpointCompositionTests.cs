using System.Reflection;
using Raffa.Api;
using Raffa.Benchmark;
using Raffa.Benchmark.Contracts;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.Insights.Contracts;
using Raffa.Insights.Strategy;
using Raffa.Renewals.Application;
using Raffa.Renewals.Domain;
using Raffa.Savings.Application;
using Raffa.Savings.Domain;
using Raffa.SharedKernel;

namespace Raffa.Insights.Tests;

/// <summary>
/// Proves task E13/F07/US01/T01's <c>Raffa.Api.InsightsEndpointExtensions</c> composition/mapping
/// methods — parent story us-01-insights AC-5 ("<c>GET /api/insights/criticality</c> and
/// <c>GET /api/contracts/{id}/strategy</c> return the same numbers the Ask pack narrates"). This
/// task's own "Tests required" table pins "endpoint composition" here, in
/// <c>Raffa.Insights.Tests</c>, not <c>Raffa.Api.Tests</c> — see
/// <c>Raffa.Insights.Tests.csproj</c>'s own comment on why that reference is not an architecture
/// violation. No database, no HTTP host: every fake below is a hand-built DTO (a real
/// <c>Contract360Result</c>/<c>SavingsOpportunityResult</c> shape), and
/// <c>InsightsEndpointExtensions</c>'s composition/mapping methods are <see langword="public"/>
/// <see langword="static"/> specifically so this class can call them directly.
/// </summary>
public sealed class InsightsEndpointCompositionTests
{
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static Contract360Result FakeContract(
        EntityId? contractId = null,
        EntityId? supplierId = null,
        decimal? annualSpend = 100_000m,
        string currency = "USD",
        DateOnly? endDate = null,
        bool autoRenewal = true,
        RiskSeverity? risk = RiskSeverity.Medium,
        IReadOnlyList<Contract360Risk>? risks = null,
        IReadOnlyList<Contract360ProductLineItem>? products = null,
        int? renewalTermMonths = null)
    {
        var id = contractId ?? EntityId.New();
        var renewalDate = endDate is not null && autoRenewal ? endDate : null;

        var header = new Contract360Header(
            id,                        // ContractId
            supplierId,                // SupplierId
            ContractDocumentType.Msa,  // Type
            "Active",                  // Status
            annualSpend,               // AnnualSpend
            annualSpend,               // TotalContractValue
            null,                      // StartDate
            endDate,                   // EndDate
            renewalDate,               // RenewalDate
            null,                      // CancellationDeadline
            autoRenewal,               // AutoRenewal
            risk);                     // Risk

        var overview = new Contract360Overview(
            currency,               // Currency
            null,                   // EffectiveDate
            renewalTermMonths,      // RenewalTermMonths
            null,                   // PaymentTerms
            null,                   // GoverningLaw
            null,                   // ParentContractId
            1,                      // Version
            DateTimeOffset.UtcNow); // CreatedAt

        var commercials = new Contract360Commercials(
            annualSpend,              // AnnualSpend
            annualSpend,              // TotalContractValue
            currency,                 // Currency
            null,                     // PaymentTerms
            autoRenewal,              // AutoRenewal
            null,                     // RenewalTermMonths
            products?.Count ?? 0,     // LineItemCount
            null,                     // LineItemAnnualCostTotal
            null);                    // LineItemTotalCostTotal

        var renewal = new Contract360Renewal(endDate, renewalDate, null, autoRenewal, null);

        return new Contract360Result(
            id,
            header,
            overview,
            commercials,
            products ?? [],
            [], // Clauses
            [], // Obligations
            risks ?? [],
            [], // Documents
            [], // Benchmark
            renewal,
            []); // Activity
    }

    private static Contract360Risk FakeRisk(RiskSeverity severity = RiskSeverity.High, double? confidence = 0.6) =>
        new(
            EntityId.New(),        // RiskId
            "Liability",           // RiskType
            severity,               // Severity
            "Uncapped liability",   // Description
            null,                   // Status
            null,                   // ClauseId
            null,                   // SourceDocumentId
            null,                   // SourceSpan
            null,                   // SourcePage
            confidence);            // Confidence

    private static Contract360ProductLineItem FakeProduct(
        decimal? unitPrice = 2300m, double? confidence = 0.9, string sku = "SKU-1") =>
        new(
            EntityId.New(),           // LineItemId
            null,                     // ProductId
            sku,                      // Sku
            "Sales Cloud Enterprise", // Description
            100m,                     // Quantity
            "seats",                  // Unit
            unitPrice,                // UnitPrice
            null,                     // ListPrice
            null,                     // Discount
            "Annual",                 // BillingPeriod
            null,                     // AnnualCost
            null,                     // TotalCost
            null,                     // SourceDocumentId
            null,                     // SourceSpan
            null,                     // SourcePage
            confidence);              // Confidence

    private static SavingsOpportunityResult FakeSavingsOpportunity(
        EntityId contractId, decimal low, decimal high) =>
        new(
            EntityId.New(),                       // Id
            null,                                  // SupplierId
            contractId,                             // ContractId
            "Renegotiation",                        // Type
            100_000m,                               // CurrentSpend
            "USD",                                  // Currency
            low,                                     // EstimatedSavingsLow
            high,                                    // EstimatedSavingsHigh
            0.7,                                     // Confidence
            SavingsOpportunityStatus.Identified,     // Status
            null,                                    // Owner
            DateTimeOffset.UtcNow,                   // CreatedAt
            DateTimeOffset.UtcNow);                  // UpdatedAt

    private static PriorityScoreResult MinimalPriority(EntityId contractId) => new(
        contractId,
        0m,
        new PriorityScoreComponent(0m, "s"),
        new PriorityScoreComponent(0m, "t"),
        new PriorityScoreComponent(0m, "b"),
        new PriorityScoreComponent(0m, "p"),
        new PriorityScoreComponent(0m, "c"));

    // ----- ToCriticalityInputs -----

    [Fact]
    public void ToCriticalityInputs_echoes_renewal_urgency_from_the_priority_result()
    {
        var contract = FakeContract();
        var priority = new PriorityScoreResult(
            contract.ContractId,
            42m,
            new PriorityScoreComponent(10m, "s"),
            new PriorityScoreComponent(10m, "t"),
            new PriorityScoreComponent(10m, "b"),
            new PriorityScoreComponent(6m, "p"),
            new PriorityScoreComponent(6m, "c"));

        var inputs = InsightsEndpointExtensions.ToCriticalityInputs(
            contract, priority, new Dictionary<string, decimal>(), []);

        Assert.Equal(42m, inputs.RenewalUrgency.TotalScore);
        Assert.Equal(contract.ContractId, inputs.ContractId);
    }

    [Fact]
    public void ToCriticalityInputs_maps_the_highest_risk_severity()
    {
        var contract = FakeContract(risk: RiskSeverity.Critical);
        var priority = MinimalPriority(contract.ContractId);

        var inputs = InsightsEndpointExtensions.ToCriticalityInputs(
            contract, priority, new Dictionary<string, decimal>(), []);

        Assert.Equal(CriticalityRiskSeverity.Critical, inputs.HighestRiskSeverity);
    }

    [Fact]
    public void ToCriticalityInputs_reads_the_portfolio_spend_for_this_contracts_own_currency()
    {
        var contract = FakeContract(currency: "CHF");
        var priority = MinimalPriority(contract.ContractId);
        var portfolioSpend = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["CHF"] = 5_000_000m,
            ["USD"] = 9_999m,
        };

        var inputs = InsightsEndpointExtensions.ToCriticalityInputs(contract, priority, portfolioSpend, []);

        Assert.Equal(5_000_000m, inputs.PortfolioAnnualSpend);
    }

    [Fact]
    public void ToCriticalityInputs_aggregates_only_this_contracts_own_savings_opportunities()
    {
        var contract = FakeContract();
        var otherContractId = EntityId.New();
        var priority = MinimalPriority(contract.ContractId);
        var opportunities = new List<SavingsOpportunityResult>
        {
            FakeSavingsOpportunity(contract.ContractId, 10_000m, 20_000m),
            FakeSavingsOpportunity(contract.ContractId, 5_000m, 8_000m),
            FakeSavingsOpportunity(otherContractId, 999_999m, 999_999m),
        };

        var inputs = InsightsEndpointExtensions.ToCriticalityInputs(
            contract, priority, new Dictionary<string, decimal>(), opportunities);

        Assert.Equal(15_000m, inputs.SavingsPotential.SavingsOpportunityRangeLow);
        Assert.Equal(28_000m, inputs.SavingsPotential.SavingsOpportunityRangeHigh);
    }

    [Fact]
    public void ToCriticalityInputs_leaves_savings_range_null_when_none_exist_for_this_contract()
    {
        var contract = FakeContract();
        var priority = MinimalPriority(contract.ContractId);

        var inputs = InsightsEndpointExtensions.ToCriticalityInputs(
            contract, priority, new Dictionary<string, decimal>(), []);

        Assert.Null(inputs.SavingsPotential.SavingsOpportunityRangeLow);
        Assert.Null(inputs.SavingsPotential.AboveBandLineFraction);
    }

    // ----- ComputePortfolioAnnualSpendByCurrency -----

    [Fact]
    public void ComputePortfolioAnnualSpendByCurrency_groups_by_currency_never_summing_across_currencies()
    {
        var contracts = new List<Contract360Result>
        {
            FakeContract(annualSpend: 100_000m, currency: "USD"),
            FakeContract(annualSpend: 250_000m, currency: "USD"),
            FakeContract(annualSpend: 80_000m, currency: "CHF"),
            FakeContract(annualSpend: null, currency: "USD"), // excluded: no known spend
        };

        var byCurrency = InsightsEndpointExtensions.ComputePortfolioAnnualSpendByCurrency(contracts);

        Assert.Equal(350_000m, byCurrency["USD"]);
        Assert.Equal(80_000m, byCurrency["CHF"]);
    }

    // ----- ToCriticalFacts -----

    [Fact]
    public void ToCriticalFacts_includes_risks_and_priced_lines_with_a_unit_price_and_confidence()
    {
        var contract = FakeContract(
            risks: [FakeRisk(confidence: 0.4), FakeRisk(confidence: 0.9)],
            products: [FakeProduct(unitPrice: 2300m, confidence: 0.5), FakeProduct(unitPrice: null, confidence: 0.99)]);

        var facts = InsightsEndpointExtensions.ToCriticalFacts(contract);

        Assert.Equal(3, facts.Count); // 2 risks + 1 priced line (the null-unit-price one is excluded)
        Assert.Contains(facts, f => f.FieldKey == "risk[0]" && f.Confidence == 0.4);
        Assert.Contains(facts, f => f.FieldKey == "risk[1]" && f.Confidence == 0.9);
        Assert.Contains(facts, f => f.FieldKey == "priced-line[0].unitPrice" && f.Confidence == 0.5);
    }

    [Fact]
    public void ToCriticalFacts_omits_facts_with_no_recorded_confidence()
    {
        var contract = FakeContract(risks: [FakeRisk(confidence: null)]);

        var facts = InsightsEndpointExtensions.ToCriticalFacts(contract);

        Assert.Empty(facts);
    }

    // ----- ToPricedLines -----

    [Fact]
    public void ToPricedLines_maps_products_with_the_contracts_own_currency_and_no_benchmark()
    {
        var contract = FakeContract(currency: "EUR", products: [FakeProduct(sku: "SKU-9", unitPrice: 1234m)]);

        var lines = InsightsEndpointExtensions.ToPricedLines(contract);

        var line = Assert.Single(lines);
        Assert.Equal("SKU-9", line.Sku);
        Assert.Equal(1234m, line.UnitPrice);
        Assert.Equal("EUR", line.Currency);
        Assert.Null(line.TermMonths);
        Assert.Null(line.Benchmark);
        Assert.Null(line.SampleSize);
    }

    [Fact]
    public async Task ToPricedLines_calls_the_adapter_when_the_host_resolves_a_complete_key()
    {
        var contract = FakeContract(
            currency: "EUR",
            products: [FakeProduct(sku: "SKU-9", unitPrice: 1234m)],
            renewalTermMonths: 12);
        var distribution = new BenchmarkDistribution(1000m, 1100m, 1200m);
        var stub = new RecordingBenchmarkService(new BenchmarkResult(
            Distribution: distribution,
            Metric: "per seat / year",
            Currency: "EUR",
            Confidence: 0.9,
            Source: "stub-fixture",
            UpdatedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ComparisonDimensions: [BenchmarkComparisonDimension.Supplier, BenchmarkComparisonDimension.Geography],
            SampleSize: 15));

        var lines = await InsightsEndpointExtensions.ToPricedLines(
            contract, stub, "Salesforce", "GB", new DateOnly(2026, 9, 9), CancellationToken.None);

        var line = Assert.Single(lines);
        Assert.Equal(distribution, line.Benchmark);
        Assert.Equal(15, line.SampleSize);
        Assert.Equal(12, line.TermMonths);
        Assert.Equal("stub-fixture", line.AdapterName);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), line.AsOf);
        Assert.Equal(1, stub.CallCount);
        Assert.Equal("Salesforce", stub.LastQuery!.Supplier);
        Assert.Equal("GB", stub.LastQuery.Geography);
        Assert.Equal("Sales Cloud Enterprise", stub.LastQuery.Product);
        Assert.Equal("SKU-9", stub.LastQuery.Sku);
        Assert.Equal("12 months", stub.LastQuery.Term);
    }

    [Fact]
    public async Task ToPricedLines_does_not_call_the_adapter_when_the_key_is_incomplete()
    {
        var contract = FakeContract(products: [FakeProduct()]);
        var stub = new RecordingBenchmarkService(result: null);

        var lines = await InsightsEndpointExtensions.ToPricedLines(
            contract, stub, supplierName: null, geography: "GB", new DateOnly(2026, 9, 9), CancellationToken.None);

        var line = Assert.Single(lines);
        Assert.Null(line.Benchmark);
        Assert.Null(line.SampleSize);
        Assert.Equal(0, stub.CallCount);
    }

    [Fact]
    public async Task ToPricedLines_prices_a_line_from_its_stored_market_comparison_and_falls_back_to_the_adapter_for_the_rest()
    {
        var matchedLine = FakeProduct(sku: "SKU-UNL", unitPrice: 398m);
        var unmatchedLine = FakeProduct(sku: "SKU-SUP", unitPrice: 31_000m);
        var contract = FakeContract(currency: "GBP", products: [matchedLine, unmatchedLine], renewalTermMonths: 12);
        var marketUpdatedAt = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
        var checkedAt = new DateTimeOffset(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);
        var stored = new Dictionary<EntityId, LineItemMarketPrice>
        {
            [matchedLine.LineItemId] = new(
                matchedLine.LineItemId, true, "MKT-ZZ-0053", "Sales Cloud Unlimited", "UK", "GBP", 12,
                2718m, 3027m, 3360m, 14, "representative market data · mock feed · updated 2026-09-10", marketUpdatedAt, checkedAt),
            [unmatchedLine.LineItemId] = new(
                unmatchedLine.LineItemId, false, null, null, null, null, null, null, null, null, null, null, null, checkedAt),
        };
        var stub = new RecordingBenchmarkService(result: null);

        var lines = await InsightsEndpointExtensions.ToPricedLines(
            contract, stub, "Salesforce", "GB", new DateOnly(2026, 9, 22), CancellationToken.None, stored);

        Assert.Equal(2, lines.Count);
        Assert.Equal(new BenchmarkDistribution(2718m, 3027m, 3360m), lines[0].Benchmark);
        Assert.Equal(14, lines[0].SampleSize);
        Assert.Equal("market-feed (representative, mock)", lines[0].AdapterName);
        Assert.Equal(marketUpdatedAt, lines[0].AsOf);

        // The stored no-match is not a band: that line still goes to the adapter, which abstains here.
        Assert.Null(lines[1].Benchmark);
        Assert.Equal(1, stub.CallCount);
        Assert.Equal("SKU-SUP", stub.LastQuery!.Sku);
    }

    [Fact]
    public async Task ToPricedLines_leaves_the_band_unset_when_the_adapter_abstains()
    {
        var contract = FakeContract(products: [FakeProduct()]);
        var stub = new RecordingBenchmarkService(new BenchmarkResult(
            Distribution: null,
            Metric: "n/a",
            Currency: "USD",
            Confidence: 0d,
            Source: "stub-fixture",
            UpdatedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ComparisonDimensions: [],
            SampleSize: 3));

        var lines = await InsightsEndpointExtensions.ToPricedLines(
            contract, stub, "Salesforce", "GB", new DateOnly(2026, 9, 9), CancellationToken.None);

        var line = Assert.Single(lines);
        Assert.Null(line.Benchmark);
        Assert.Null(line.AdapterName);
        Assert.Equal(1, stub.CallCount);
    }

    // ----- Host resolves; module stays fenced -----

    [Fact]
    public void GetContractStrategyAsync_takes_the_shared_key_resolver_and_the_benchmark_port()
    {
        var method = typeof(InsightsEndpointExtensions)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name == "GetContractStrategyAsync");
        var parameterTypeNames = method.GetParameters().Select(p => p.ParameterType.Name).ToArray();

        Assert.Contains("BenchmarkKeyResolution", parameterTypeNames);
        Assert.Contains(nameof(IBenchmarkService), parameterTypeNames);
    }

    [Fact]
    public void Raffa_Insights_references_no_workspace_type_and_keeps_the_allow_list()
    {
        var raffaRefs = typeof(StrategyPackBuilder).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => n is not null && n.StartsWith("Raffa.", StringComparison.Ordinal))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "Raffa.Benchmark", "Raffa.SharedKernel" }, raffaRefs);
        Assert.DoesNotContain(raffaRefs, n => n.Contains("Identity", StringComparison.Ordinal));
        Assert.DoesNotContain(raffaRefs, n => n.Contains("Workspace", StringComparison.Ordinal));
    }

    // ----- ToStrategyInputs / ComputeRenewal -----

    [Fact]
    public void ToStrategyInputs_echoes_the_renewal_calculation_and_the_host_resolved_supplier_name()
    {
        var contract = FakeContract(endDate: new DateOnly(2027, 1, 1), autoRenewal: true);
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero));
        var renewalEngine = new RenewalEngine(clock);

        var renewal = InsightsEndpointExtensions.ComputeRenewal(contract.Header, renewalEngine);
        var strategyInputs = InsightsEndpointExtensions.ToStrategyInputs(
            contract, renewal, [], [], DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), "Salesforce");

        Assert.Equal("Salesforce", strategyInputs.SupplierName);
        Assert.Equal(renewal.RenewalDate, strategyInputs.RenewalDate);
        Assert.Equal(renewal.DaysUntilRenewal, strategyInputs.DaysUntilRenewal);
        Assert.Equal(contract.Header.AutoRenewal, strategyInputs.AutoRenewal);
    }

    [Fact]
    public void ToStrategyInputs_leaves_supplier_name_null_when_the_host_did_not_resolve_one()
    {
        var contract = FakeContract(endDate: new DateOnly(2027, 1, 1), autoRenewal: true);
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero));
        var renewalEngine = new RenewalEngine(clock);

        var renewal = InsightsEndpointExtensions.ComputeRenewal(contract.Header, renewalEngine);
        var strategyInputs = InsightsEndpointExtensions.ToStrategyInputs(
            contract, renewal, [], [], DateOnly.FromDateTime(clock.UtcNow.UtcDateTime));

        Assert.Null(strategyInputs.SupplierName);
    }

    private sealed class RecordingBenchmarkService(BenchmarkResult? result) : IBenchmarkService
    {
        public BenchmarkQuery? LastQuery { get; private set; }
        public int CallCount { get; private set; }

        public Task<Result<BenchmarkResult>> GetBenchmarkAsync(
            BenchmarkQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            CallCount++;
            return Task.FromResult(
                result is null
                    ? Result<BenchmarkResult>.Failure("no result")
                    : Result<BenchmarkResult>.Success(result));
        }
    }

    // ----- ComputePriority -----

    [Fact]
    public void ComputePriority_runs_the_renewal_engine_then_the_priority_calculator_consistently()
    {
        var contract = FakeContract(endDate: new DateOnly(2026, 10, 9), annualSpend: 600_000m, risk: RiskSeverity.High);
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero));
        var renewalEngine = new RenewalEngine(clock);
        var priorityScoreCalculator = new PriorityScoreCalculator();

        var priority = InsightsEndpointExtensions.ComputePriority(contract.Header, renewalEngine, priorityScoreCalculator);

        Assert.Equal(contract.ContractId, priority.ContractId);
        Assert.True(priority.TotalScore > 0m); // high spend + near-term renewal + high risk all contribute
    }

    // ----- Risk mapping -----

    [Theory]
    [InlineData(null, null)]
    [InlineData(RiskSeverity.Low, CriticalityRiskSeverity.Low)]
    [InlineData(RiskSeverity.Medium, CriticalityRiskSeverity.Medium)]
    [InlineData(RiskSeverity.High, CriticalityRiskSeverity.High)]
    [InlineData(RiskSeverity.Critical, CriticalityRiskSeverity.Critical)]
    public void ToCriticalityRiskSeverity_maps_1_to_1_by_name(RiskSeverity? risk, CriticalityRiskSeverity? expected)
    {
        Assert.Equal(expected, InsightsEndpointExtensions.ToCriticalityRiskSeverity(risk));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(RiskSeverity.Low, ContractRiskLevel.Low)]
    [InlineData(RiskSeverity.Medium, ContractRiskLevel.Medium)]
    [InlineData(RiskSeverity.High, ContractRiskLevel.High)]
    [InlineData(RiskSeverity.Critical, ContractRiskLevel.Critical)]
    public void ToContractRiskLevel_maps_1_to_1_by_name(RiskSeverity? risk, ContractRiskLevel? expected)
    {
        Assert.Equal(expected, InsightsEndpointExtensions.ToContractRiskLevel(risk));
    }
}
