using Raffa.Documents.Contracts.Application;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// Boundary table for the single extraction auto-accept bar: the raw stored double, no rounding
/// before the compare, and the five critical fields use the same bar as every other field.
/// </summary>
public sealed class ExtractionConfidencePolicyTests
{
    [Theory]
    [InlineData(0.90)]
    [InlineData(0.9000001)]
    [InlineData(1.0)]
    public void Confidence_at_or_above_the_bar_is_auto_accepted(double confidence)
    {
        Assert.Equal(ExtractionConfidencePolicy.AutoAccepted, ExtractionConfidencePolicy.Decide(confidence));
        Assert.True(ExtractionConfidencePolicy.IsAutoAccepted(confidence));
        Assert.False(ExtractionConfidencePolicy.RequiresReview(confidence));
    }

    [Theory]
    [InlineData(0.895)]
    [InlineData(0.899)]
    [InlineData(0.0)]
    public void Confidence_below_the_bar_is_review_required(double confidence)
    {
        Assert.Equal(ExtractionConfidencePolicy.ReviewRequired, ExtractionConfidencePolicy.Decide(confidence));
        Assert.False(ExtractionConfidencePolicy.IsAutoAccepted(confidence));
        Assert.True(ExtractionConfidencePolicy.RequiresReview(confidence));
    }

    [Fact]
    public void Null_confidence_is_review_required_never_accepted()
    {
        Assert.Equal(ExtractionConfidencePolicy.ReviewRequired, ExtractionConfidencePolicy.Decide(null));
        Assert.True(ExtractionConfidencePolicy.RequiresReview(null));
        Assert.False(ExtractionConfidencePolicy.IsAutoAccepted(null));
    }

    [Fact]
    public void Decide_never_emits_human_accepted()
    {
        Assert.NotEqual(ExtractionConfidencePolicy.HumanAccepted, ExtractionConfidencePolicy.Decide(0.90));
        Assert.NotEqual(ExtractionConfidencePolicy.HumanAccepted, ExtractionConfidencePolicy.Decide(0.895));
        Assert.NotEqual(ExtractionConfidencePolicy.HumanAccepted, ExtractionConfidencePolicy.Decide(null));
    }

    [Theory]
    [InlineData("annualSpend")]
    [InlineData("totalContractValue")]
    [InlineData("cancellationDeadline")]
    [InlineData("endDate")]
    [InlineData("renewalTermMonths")]
    [InlineData("currency")]
    [InlineData("supplier")]
    public void Every_field_including_the_five_critical_ones_uses_the_same_bar(string fieldName)
    {
        // Decide does not take a field name: the bar cannot differ per field.
        Assert.Equal(ExtractionConfidencePolicy.AutoAccepted, ExtractionConfidencePolicy.Decide(0.90));
        Assert.Equal(ExtractionConfidencePolicy.ReviewRequired, ExtractionConfidencePolicy.Decide(0.895));
        Assert.Contains(fieldName, ExtractionConfidencePolicy.CriticalFieldNames.Concat(["currency", "supplier"]));
    }

    [Fact]
    public void The_five_critical_fields_are_the_spec_set()
    {
        Assert.Equal(
            ["annualSpend", "totalContractValue", "cancellationDeadline", "endDate", "renewalTermMonths"],
            ExtractionConfidencePolicy.CriticalFieldNames);
    }

    [Fact]
    public void The_bar_is_the_raw_stored_double_with_no_rounding()
    {
        Assert.Equal(0.90, ExtractionConfidencePolicy.AutoAcceptThreshold);
        // 0.895 rounded to one decimal would cross the bar; the policy must not.
        Assert.True(Math.Round(0.895, 1) >= ExtractionConfidencePolicy.AutoAcceptThreshold);
        Assert.Equal(ExtractionConfidencePolicy.ReviewRequired, ExtractionConfidencePolicy.Decide(0.895));
    }
}
