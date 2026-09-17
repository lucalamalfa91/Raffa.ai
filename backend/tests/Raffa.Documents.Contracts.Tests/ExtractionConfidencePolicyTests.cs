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
    [InlineData(null)]
    [InlineData("review_required")]
    [InlineData("unknown")]
    public void Null_and_review_required_decisions_still_need_a_human(string? decision)
    {
        Assert.True(ExtractionConfidencePolicy.StillRequiresHumanDecision(decision));
    }

    [Theory]
    [InlineData("auto_accepted")]
    [InlineData("human_accepted")]
    public void Accepted_decisions_do_not_still_need_a_human(string decision)
    {
        Assert.False(ExtractionConfidencePolicy.StillRequiresHumanDecision(decision));
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

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.55)]
    [InlineData(0.89)]
    [InlineData(null)]
    public void An_extracted_start_date_is_always_auto_accepted(double? confidence)
    {
        Assert.Equal(
            ExtractionConfidencePolicy.AutoAccepted,
            ExtractionConfidencePolicy.Decide("startDate", confidence));
        Assert.Equal(
            ExtractionConfidencePolicy.ReviewRequired,
            ExtractionConfidencePolicy.Decide("endDate", confidence));
    }

    [Fact]
    public void Status_is_active_when_today_falls_inside_the_start_end_window()
    {
        var today = new DateOnly(2026, 9, 17);
        Assert.Equal(
            ExtractionConfidencePolicy.StatusActive,
            ExtractionConfidencePolicy.DeriveStatus(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), today));
    }

    [Fact]
    public void Status_is_expired_when_the_end_date_is_before_today()
    {
        var today = new DateOnly(2026, 9, 17);
        Assert.Equal(
            ExtractionConfidencePolicy.StatusExpired,
            ExtractionConfidencePolicy.DeriveStatus(new DateOnly(2024, 1, 1), new DateOnly(2025, 12, 31), today));
    }

    [Fact]
    public void Status_is_expired_when_the_start_date_is_still_in_the_future()
    {
        var today = new DateOnly(2026, 9, 17);
        Assert.Equal(
            ExtractionConfidencePolicy.StatusExpired,
            ExtractionConfidencePolicy.DeriveStatus(new DateOnly(2027, 1, 1), new DateOnly(2028, 1, 1), today));
    }

    [Fact]
    public void An_open_ended_start_in_the_past_is_active()
    {
        var today = new DateOnly(2026, 9, 17);
        Assert.Equal(
            ExtractionConfidencePolicy.StatusActive,
            ExtractionConfidencePolicy.DeriveStatus(new DateOnly(2026, 1, 1), endDate: null, today));
    }

    [Fact]
    public void Neither_date_yields_no_derived_status()
    {
        Assert.Null(ExtractionConfidencePolicy.DeriveStatus(null, null, new DateOnly(2026, 9, 17)));
    }

    [Fact]
    public void Inclusive_bounds_treat_today_as_in_force()
    {
        var today = new DateOnly(2026, 9, 17);
        Assert.Equal(
            ExtractionConfidencePolicy.StatusActive,
            ExtractionConfidencePolicy.DeriveStatus(today, today, today));
    }
}
