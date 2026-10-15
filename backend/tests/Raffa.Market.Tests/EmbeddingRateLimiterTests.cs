using Raffa.Market.Ingestion;

namespace Raffa.Market.Tests;

/// <summary>T-05: the TPM-aware limiter in front of the embed calls of the ingestion job.</summary>
public sealed class EmbeddingRateLimiterTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_budget_of_zero_disables_the_limiter()
    {
        var limiter = new EmbeddingRateLimiter(0, new ManualTimeProvider(Start));

        await limiter.WaitAsync(10_000_000);

        Assert.False(limiter.Enabled);
    }

    [Fact]
    public async Task Requests_that_fit_the_window_pass_immediately()
    {
        var time = new ManualTimeProvider(Start);
        var limiter = new EmbeddingRateLimiter(100, time);

        await limiter.WaitAsync(60);
        await limiter.WaitAsync(40);

        Assert.Equal(0, time.PendingTimers);
    }

    [Fact]
    public async Task A_request_over_the_budget_waits_until_the_oldest_reservation_ages_out()
    {
        var time = new ManualTimeProvider(Start);
        var limiter = new EmbeddingRateLimiter(100, time);
        await limiter.WaitAsync(60);

        var second = limiter.WaitAsync(60);
        await Task.Delay(50);
        Assert.False(second.IsCompleted);
        Assert.Equal(1, time.PendingTimers);

        time.Advance(TimeSpan.FromSeconds(59));
        await Task.Delay(50);
        Assert.False(second.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(1));
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(second.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_request_larger_than_the_whole_budget_still_goes_through_on_an_empty_window()
    {
        var limiter = new EmbeddingRateLimiter(10, new ManualTimeProvider(Start));

        await limiter.WaitAsync(500).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Waiting_honours_cancellation()
    {
        var time = new ManualTimeProvider(Start);
        var limiter = new EmbeddingRateLimiter(100, time);
        await limiter.WaitAsync(100);
        using var cts = new CancellationTokenSource();

        var blocked = limiter.WaitAsync(50, cts.Token);
        await Task.Delay(50);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocked);
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("abcd", 1)]
    [InlineData("abcde", 2)]
    public void Token_estimate_is_about_four_characters_per_token(string text, int expected) =>
        Assert.Equal(expected, EmbeddingRateLimiter.EstimateTokens(text));
}
