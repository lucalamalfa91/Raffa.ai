namespace Raffa.Market.Ingestion;

/// <summary>
/// A TPM-aware limiter for embedding calls (T-05): a rolling 60-second window of estimated tokens.
/// <see cref="WaitAsync"/> returns immediately while the window has room and otherwise delays until
/// enough of the oldest reservations have aged out. A single request larger than the whole budget is
/// let through once the window is empty (it could never fit otherwise). A budget of <c>0</c> or less
/// disables the limiter. Time and delay come from a <see cref="TimeProvider"/> so tests drive it
/// without sleeping. Not thread-safe by design: the ingestion job is sequential.
/// </summary>
public sealed class EmbeddingRateLimiter(int tokensPerMinute, TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Queue<(DateTimeOffset At, int Tokens)> _reservations = new();
    private long _inWindow;

    /// <summary>Whether the limiter does anything.</summary>
    public bool Enabled => tokensPerMinute > 0;

    /// <summary>
    /// A cheap token estimate for <paramref name="text"/> (about four characters per token, the
    /// rule of thumb for the cl100k family the embed models use); never below 1.
    /// </summary>
    public static int EstimateTokens(string text) => Math.Max(1, (text.Length + 3) / 4);

    /// <summary>Waits until <paramref name="tokens"/> fit in the rolling window, then reserves them.</summary>
    public async Task WaitAsync(int tokens, CancellationToken cancellationToken = default)
    {
        if (!Enabled)
        {
            return;
        }

        tokens = Math.Max(1, tokens);
        while (true)
        {
            var now = _time.GetUtcNow();
            while (_reservations.Count > 0 && now - _reservations.Peek().At >= Window)
            {
                _inWindow -= _reservations.Dequeue().Tokens;
            }

            if (_inWindow + tokens <= tokensPerMinute || _reservations.Count == 0)
            {
                _reservations.Enqueue((now, tokens));
                _inWindow += tokens;
                return;
            }

            var wait = Window - (now - _reservations.Peek().At);
            await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.FromMilliseconds(1), _time, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
