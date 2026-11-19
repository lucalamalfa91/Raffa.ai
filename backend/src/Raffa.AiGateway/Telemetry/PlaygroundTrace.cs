using System.Diagnostics;

namespace Raffa.AiGateway.Telemetry;

/// <summary>
/// One step of a playground run, as the Super Admin's inspector shows it. <see cref="Kind"/> is
/// <c>model_call</c> (one gateway role call), <c>tool_call</c> (one Ask tool the agent asked for),
/// <c>guard</c> (a grounding/numeric check, a regeneration, a fallback) or <c>note</c>.
/// <see cref="Input"/> / <see cref="Output"/> are raw JSON or text, truncated at
/// <see cref="PlaygroundTraceCollector.MaxPayloadChars"/>.
/// </summary>
public sealed record PlaygroundTraceEvent(
    int Seq,
    string Kind,
    string Name,
    DateTimeOffset At,
    long DurationMs,
    bool Ok,
    string? Model,
    int? PromptTokens,
    int? CompletionTokens,
    int? CachedTokens,
    string? Input,
    string? Output,
    string? Note);

/// <summary>
/// Thread-safe, per-run list of <see cref="PlaygroundTraceEvent"/>. Created only by
/// <see cref="PlaygroundTrace.Begin"/>, which the platform-admin playground endpoints call after the
/// <c>PlatformAdminGuard</c> check; normal requests never have one, so every <c>Record</c> call on the
/// production path is a null check.
/// </summary>
public sealed class PlaygroundTraceCollector
{
    public const int MaxPayloadChars = 60_000;

    private readonly object _gate = new();
    private readonly List<PlaygroundTraceEvent> _events = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public IReadOnlyList<PlaygroundTraceEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    public long ElapsedMs => _clock.ElapsedMilliseconds;

    public void Add(
        string kind, string name, long durationMs, bool ok, string? model = null,
        int? promptTokens = null, int? completionTokens = null, int? cachedTokens = null,
        string? input = null, string? output = null, string? note = null)
    {
        lock (_gate)
        {
            _events.Add(new PlaygroundTraceEvent(
                _events.Count + 1, kind, name, DateTimeOffset.UtcNow, durationMs, ok, model,
                promptTokens, completionTokens, cachedTokens, Truncate(input), Truncate(output), note));
        }
    }

    private static string? Truncate(string? text) =>
        text is { Length: > MaxPayloadChars } ? text[..MaxPayloadChars] + "…[truncated]" : text;
}

/// <summary>
/// Ambient (<see cref="AsyncLocal{T}"/>) playground trace: flows into every task a run starts, like
/// <see cref="RunContext"/>. Opened only by the platform-admin playground endpoints.
/// </summary>
public static class PlaygroundTrace
{
    private static readonly AsyncLocal<PlaygroundTraceCollector?> Ambient = new();

    /// <summary>The collector of the current async flow, or <see langword="null"/> outside a playground run.</summary>
    public static PlaygroundTraceCollector? Current => Ambient.Value;

    public static bool IsActive => Ambient.Value is not null;

    /// <summary>Starts a trace for the rest of the async flow; dispose to restore the previous value.</summary>
    public static IDisposable Begin(out PlaygroundTraceCollector collector)
    {
        var previous = Ambient.Value;
        collector = new PlaygroundTraceCollector();
        Ambient.Value = collector;
        return new Scope(previous);
    }

    /// <summary>Records one event when a trace is active; otherwise does nothing.</summary>
    public static void Record(
        string kind, string name, long durationMs = 0, bool ok = true, string? input = null, string? output = null, string? note = null) =>
        Ambient.Value?.Add(kind, name, durationMs, ok, input: input, output: output, note: note);

    private sealed class Scope(PlaygroundTraceCollector? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
