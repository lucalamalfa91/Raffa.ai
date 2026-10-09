using Raffa.Quotes.Application.Extraction;

namespace Raffa.Quotes.Tests;

/// <summary>
/// Task F6-T02 (typed failure text): <see cref="QuoteExtractionFailure"/> round-trips its kind
/// through the <c>ErrorDetail</c> column text. (The F6-T04 payload range checks moved with
/// <c>QuoteLineExtractionService</c> to <c>Raffa.AiFlows.Tests</c>.)
/// </summary>
public sealed class QuoteExtractionRobustnessTests
{
    // ----- typed failure text (F6-T02) -----

    [Theory]
    [InlineData(QuoteExtractionFailureKind.ParseFailed)]
    [InlineData(QuoteExtractionFailureKind.NoReadableText)]
    [InlineData(QuoteExtractionFailureKind.ExtractionFailed)]
    [InlineData(QuoteExtractionFailureKind.MalformedPayload)]
    [InlineData(QuoteExtractionFailureKind.PersistenceFailed)]
    [InlineData(QuoteExtractionFailureKind.Cancelled)]
    [InlineData(QuoteExtractionFailureKind.Unexpected)]
    public void A_formatted_failure_round_trips_its_kind(QuoteExtractionFailureKind kind)
    {
        var text = QuoteExtractionFailure.Format(kind, "something went wrong");

        Assert.Equal($"[{kind}] something went wrong", text);
        Assert.True(QuoteExtractionFailure.TryParseKind(text, out var parsed));
        Assert.Equal(kind, parsed);
    }

    [Fact]
    public void A_failure_is_truncated_to_the_column_budget_and_keeps_its_kind()
    {
        var text = QuoteExtractionFailure.Format(
            QuoteExtractionFailureKind.ExtractionFailed, new string('x', 5000));

        Assert.Equal(QuoteExtractionFailure.MaxErrorDetailLength, text.Length);
        Assert.True(QuoteExtractionFailure.TryParseKind(text, out var kind));
        Assert.Equal(QuoteExtractionFailureKind.ExtractionFailed, kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plain text written before the typing existed")]
    [InlineData("[NotAKind] x")]
    [InlineData("[] x")]
    [InlineData("[7] x")]
    [InlineData("[1] x")]
    public void An_untyped_or_unknown_error_detail_does_not_parse(string? errorDetail)
    {
        Assert.False(QuoteExtractionFailure.TryParseKind(errorDetail, out _));
    }
}
