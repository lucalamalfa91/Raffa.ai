using System.Diagnostics;
using Raffa.AiFlows.DocumentExtraction.Orchestration;
using Raffa.AiFlows.DocumentExtraction.Stages;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.DocumentExtraction.Orchestration;

/// <summary>F5-T10: the TPM token bucket and its use by the stage scheduler. Fixtures only, no model.</summary>
public sealed class TpmRateLimiterTests
{
    /// <summary>Time that moves only when told to (enough for bucket arithmetic; no timers).</summary>
    private sealed class ManualTime : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => Interlocked.Read(ref _ticks);

        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, (long)(by.TotalSeconds * TimestampFrequency));
    }

    [Fact]
    public async Task A_full_bucket_grants_immediately_and_is_debited()
    {
        var limiter = new TpmRateLimiter(6000, new ManualTime());

        var reserved = await limiter.AcquireAsync(1500, CancellationToken.None);

        Assert.Equal(1500, reserved);
        Assert.Equal(4500, limiter.Available, 3);
    }

    [Fact]
    public async Task The_bucket_refills_at_the_per_minute_rate_up_to_its_capacity()
    {
        var time = new ManualTime();
        var limiter = new TpmRateLimiter(6000, time); // 100 tokens / second
        await limiter.AcquireAsync(6000, CancellationToken.None);
        Assert.Equal(0, limiter.Available, 3);

        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(1000, limiter.Available, 3);

        time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(6000, limiter.Available, 3);
    }

    [Fact]
    public async Task An_estimate_above_the_capacity_is_clamped_so_a_huge_call_is_delayed_never_starved()
    {
        var limiter = new TpmRateLimiter(6000, new ManualTime());

        var reserved = await limiter.AcquireAsync(500_000, CancellationToken.None);

        Assert.Equal(6000, reserved);
    }

    [Fact]
    public async Task Settle_refunds_what_was_not_used_and_debits_an_overrun()
    {
        var limiter = new TpmRateLimiter(6000, new ManualTime());
        var reserved = await limiter.AcquireAsync(3000, CancellationToken.None);

        limiter.Settle(reserved, 1000);
        Assert.Equal(5000, limiter.Available, 3);

        var second = await limiter.AcquireAsync(1000, CancellationToken.None);
        limiter.Settle(second, 3000);
        Assert.Equal(2000, limiter.Available, 3);

        var third = await limiter.AcquireAsync(500, CancellationToken.None);
        limiter.Settle(third, 0);
        Assert.Equal(2000, limiter.Available, 3);
    }

    [Fact]
    public async Task An_empty_bucket_makes_the_caller_wait_for_the_refill()
    {
        var limiter = new TpmRateLimiter(60_000, TimeProvider.System); // 1000 tokens / second
        await limiter.AcquireAsync(60_000, CancellationToken.None);

        var clock = Stopwatch.StartNew();
        await limiter.AcquireAsync(300, CancellationToken.None);

        Assert.True(clock.ElapsedMilliseconds >= 200, $"waited only {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task A_cancelled_wait_throws_and_does_not_hold_the_queue()
    {
        var limiter = new TpmRateLimiter(60, TimeProvider.System); // 1 token / second
        await limiter.AcquireAsync(60, CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => limiter.AcquireAsync(30, cts.Token));

        using var cts2 = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => limiter.AcquireAsync(30, cts2.Token));
    }

    // ---- scheduler integration -------------------------------------------------------------------------

    private sealed class CountingGateway(int promptTokens) : IAiGateway
    {
        private int _inFlight;
        private int _calls;

        public int Calls => _calls;

        public int MaxConcurrent { get; private set; }

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            lock (this)
            {
                MaxConcurrent = Math.Max(MaxConcurrent, ++_inFlight);
            }

            lock (this)
            {
                _inFlight--;
            }

            return Task.FromResult(Result<AiExtractionResult>.Success(new AiExtractionResult(
                """{"facts":[]}""",
                new AiCallMetadata("m", "1", "p", DateTimeOffset.UnixEpoch, "h", new AiTokenUsage(promptTokens, 10)))));
        }

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static StageScheduler Scheduler(IAiGateway gateway, ExtractionOrchestrationOptions options, TpmRateLimiter? limiter) =>
        new(gateway, ExtractionStages.Default.ToDictionary(s => s.Stage), options, limiter);

    [Fact]
    public async Task With_the_limiter_off_the_seven_calls_are_not_paced()
    {
        var gateway = new CountingGateway(100);
        var scheduler = Scheduler(gateway, new ExtractionOrchestrationOptions(), limiter: null);

        var clock = Stopwatch.StartNew();
        var pending = scheduler.Start(
            ExtractionPipeline.Stages, new string('x', 30_000), 1, new Dictionary<ExtractionStage, StageFacts>(), CancellationToken.None);
        await Task.WhenAll(pending.Values);

        Assert.Equal(7, gateway.Calls);
        Assert.True(clock.ElapsedMilliseconds < 1000);
    }

    [Fact]
    public async Task With_the_limiter_on_all_seven_calls_still_complete_without_failure_and_are_paced()
    {
        // ~10K chars => ~3.3K input + 2K output reserve ≈ 5.4K per call; capacity 12K, 12K/s refill:
        // 7 calls need ~37K => the later ones must wait for the refill.
        var gateway = new CountingGateway(130_000);
        var options = new ExtractionOrchestrationOptions { TokensPerMinuteLimit = 720_000, MaxConcurrentStages = 2 };
        var scheduler = Scheduler(gateway, options, new TpmRateLimiter(options.TokensPerMinuteLimit));

        var clock = Stopwatch.StartNew();
        var pending = scheduler.Start(
            ExtractionPipeline.Stages, new string('x', 400_000), 1, new Dictionary<ExtractionStage, StageFacts>(), CancellationToken.None);
        var calls = await Task.WhenAll(pending.Values);

        Assert.All(calls, c => Assert.True(c.Result.IsSuccess));
        Assert.Equal(7, gateway.Calls);
        // 7 x (133K input + 2K out) clamped to the 720K bucket: ~945K needed vs 720K available => > 0 wait.
        Assert.True(clock.ElapsedMilliseconds >= 100, $"not paced: {clock.ElapsedMilliseconds} ms");
    }
}
