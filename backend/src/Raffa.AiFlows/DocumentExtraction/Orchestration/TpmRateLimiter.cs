using System.Diagnostics.Metrics;

namespace Raffa.AiFlows.DocumentExtraction.Orchestration;

/// <summary>
/// F5-T10: a token bucket over estimated tokens per minute, shared by the stage calls of a run so that the
/// provider's TPM quota is not overrun (a burst of 7 stages over a long document would otherwise earn 429s).
/// Capacity = one minute of quota, refilled continuously. <see cref="AcquireAsync"/> waits, FIFO, until the
/// bucket holds the requested estimate; an estimate above the capacity is clamped to it so a single huge call
/// is delayed, never starved. After the call <see cref="Settle"/> trues the reservation up to the usage the
/// provider reported (refund when it used less, extra debit when more). Time comes from a
/// <see cref="TimeProvider"/> so tests are deterministic.
/// </summary>
public sealed class TpmRateLimiter
{
    public const string MeterName = "Raffa.AiFlows.Extraction";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> Waits = Meter.CreateCounter<long>(
        "raffa.extraction.tpm_waits",
        description: "Stage calls that had to wait for the TPM token bucket.");

    private readonly SemaphoreSlim _turn = new(1, 1);
    private readonly object _lock = new();
    private readonly TimeProvider _time;
    private readonly double _capacity;
    private readonly double _refillPerTick;
    private double _available;
    private long _lastRefill;

    public TpmRateLimiter(int tokensPerMinute, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tokensPerMinute);
        _time = timeProvider ?? TimeProvider.System;
        _capacity = tokensPerMinute;
        _available = tokensPerMinute;
        _refillPerTick = tokensPerMinute / (60.0 * _time.TimestampFrequency);
        _lastRefill = _time.GetTimestamp();
    }

    /// <summary>Tokens currently in the bucket (after refill); may be negative after a settle overrun.</summary>
    public double Available
    {
        get
        {
            lock (_lock)
            {
                Refill();
                return _available;
            }
        }
    }

    /// <summary>Reserves <paramref name="estimatedTokens"/> (clamped to the capacity) and returns what was
    /// actually reserved, to pass to <see cref="Settle"/>.</summary>
    public async Task<int> AcquireAsync(int estimatedTokens, CancellationToken cancellationToken)
    {
        var want = Math.Clamp(estimatedTokens, 1, (int)_capacity);

        await _turn.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var waited = false;
            while (true)
            {
                TimeSpan delay;
                lock (_lock)
                {
                    Refill();
                    if (_available >= want)
                    {
                        _available -= want;
                        break;
                    }

                    delay = TimeSpan.FromSeconds((want - _available) / (_capacity / 60.0));
                }

                if (!waited)
                {
                    waited = true;
                    Waits.Add(1);
                }

                await Task.Delay(delay, _time, cancellationToken).ConfigureAwait(false);
            }

            return want;
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary>Replaces the reservation by the tokens the call really used. A call that failed before
    /// reaching the provider passes 0 to refund it all.</summary>
    public void Settle(int reserved, int actualTokens)
    {
        lock (_lock)
        {
            Refill();
            _available = Math.Min(_capacity, _available + reserved - Math.Max(0, actualTokens));
        }
    }

    private void Refill()
    {
        var now = _time.GetTimestamp();
        var elapsed = now - _lastRefill;
        if (elapsed <= 0)
        {
            return;
        }

        _lastRefill = now;
        _available = Math.Min(_capacity, _available + elapsed * _refillPerTick);
    }
}

/// <summary>DI holder of the process-wide <see cref="TpmRateLimiter"/>; <see cref="Limiter"/> is null when
/// the configured limit is 0 (off).</summary>
public sealed class StageTpmLimit(int tokensPerMinute, TimeProvider? timeProvider = null)
{
    public TpmRateLimiter? Limiter { get; } = tokensPerMinute > 0 ? new TpmRateLimiter(tokensPerMinute, timeProvider) : null;
}
