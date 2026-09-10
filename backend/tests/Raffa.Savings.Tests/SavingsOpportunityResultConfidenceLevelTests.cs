using Raffa.Savings.Application;
using Raffa.Savings.Domain;
using Raffa.SharedKernel;

namespace Raffa.Savings.Tests;

/// <summary>
/// Proves task E04/F03/US01/T02 (savings-list, the wave-spec's own artifact of that name) — parent
/// story us-01-savings-kpis AC-3 ("Returns provenance + confidence, never fabricated precision"):
/// <see cref="SavingsOpportunityResult.ConfidenceLevel"/> always agrees with
/// <see cref="SavingsProvenanceClassifier.Classify"/> applied directly to the same raw
/// <see cref="SavingsOpportunityResult.Confidence"/> score, at the exact thresholds
/// <see cref="SavingsProvenanceClassifierTests"/> already proves for that classifier, and can never
/// disagree with <see cref="SavingsOpportunityResult.Confidence"/> because it is computed fresh on
/// every access rather than stored or passed to the constructor (mirrors
/// <see cref="PriceComparisonResultProvenanceTests"/>'s own "computed, never drifts" proof for
/// <see cref="PriceComparisonResult.Provenance"/>).
/// </summary>
public sealed class SavingsOpportunityResultConfidenceLevelTests
{
    private static SavingsOpportunityResult ResultWith(double confidence) => new(
        Id: EntityId.New(),
        SupplierId: null,
        ContractId: null,
        Type: "price-renegotiation",
        CurrentSpend: 10_000m,
        Currency: "USD",
        EstimatedSavingsLow: 500m,
        EstimatedSavingsHigh: 1_000m,
        Confidence: confidence,
        Status: SavingsOpportunityStatus.Identified,
        Owner: null,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);

    // ----- Boundary values -- same thresholds SavingsProvenanceClassifierTests already proves -----

    [Theory]
    [InlineData(1.0, SavingsConfidenceLevel.High)]
    [InlineData(0.7, SavingsConfidenceLevel.High)]   // exactly the High boundary
    [InlineData(0.69, SavingsConfidenceLevel.Medium)]
    [InlineData(0.4, SavingsConfidenceLevel.Medium)] // exactly the Medium boundary
    [InlineData(0.39, SavingsConfidenceLevel.Low)]
    [InlineData(0.0, SavingsConfidenceLevel.Low)]
    public void ConfidenceLevel_matches_the_classifier_applied_to_the_same_raw_score(
        double confidence, SavingsConfidenceLevel expected)
    {
        var result = ResultWith(confidence);

        Assert.Equal(expected, result.ConfidenceLevel);
        Assert.Equal(SavingsProvenanceClassifier.Classify(confidence), result.ConfidenceLevel);
    }

    [Fact]
    public void ConfidenceLevel_never_throws_for_a_score_outside_the_documented_zero_to_one_range()
    {
        Assert.Equal(SavingsConfidenceLevel.High, ResultWith(1.5).ConfidenceLevel);
        Assert.Equal(SavingsConfidenceLevel.Low, ResultWith(-0.5).ConfidenceLevel);
    }

    // ----- Computed, not stored: cannot drift from Confidence (Appendix C rule 10) -----

    [Fact]
    public void ConfidenceLevel_always_reflects_the_records_own_Confidence_never_a_stale_copy()
    {
        var low = ResultWith(0.1);
        var high = low with { Confidence = 0.95 };

        Assert.Equal(SavingsConfidenceLevel.Low, low.ConfidenceLevel);
        Assert.Equal(SavingsConfidenceLevel.High, high.ConfidenceLevel);
    }
}
