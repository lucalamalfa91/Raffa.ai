using System.Diagnostics;

namespace Raffa.JevBenchmark.Tests.Harness;

/// <summary>Monotonic millisecond clock; a fake one makes latency deterministic in unit tests.</summary>
public interface IBenchClock
{
    double NowMs { get; }
}

public sealed class StopwatchBenchClock : IBenchClock
{
    public double NowMs => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
}

/// <summary>One outbound model call made while serving a case (Foundry via the metering gateway, Jev via the recording handler).</summary>
/// <param name="Backend">"foundry" or "jev".</param>
/// <param name="Operation">Gateway role ("classify", "analyze") or "decision" for a Jev HTTP attempt.</param>
/// <param name="Usable">Jev only: the 2xx body carried at least one non-empty choice. Foundry: same as Success.</param>
public sealed record CallRecord(
    string Backend,
    string Operation,
    double LatencyMs,
    bool Success,
    bool Usable,
    int? HttpStatus,
    int InputTokens,
    int OutputTokens,
    double? CostUsd,
    string? Error);

/// <summary>Collects the <see cref="CallRecord"/>s of the case currently being served by one arm.</summary>
public sealed class CallLedger
{
    private readonly List<CallRecord> _calls = [];
    private readonly object _gate = new();

    public void Add(CallRecord call)
    {
        lock (_gate)
        {
            _calls.Add(call);
        }
    }

    public IReadOnlyList<CallRecord> Drain()
    {
        lock (_gate)
        {
            var snapshot = _calls.ToList();
            _calls.Clear();
            return snapshot;
        }
    }
}

/// <summary>
/// What one arm did with one case in one repetition. <see cref="Predicted"/> is null exactly when
/// <see cref="Error"/> is true. <see cref="FallbackReason"/> is only ever set on the Jev arm:
/// "jev-error" (no usable Jev answer, Foundry served it) or "low-confidence" (Jev answered but the
/// gateway's confidence/option-order gate sent the document to Foundry).
/// </summary>
public sealed record CaseObservation(
    string Track,
    string Arm,
    string CaseId,
    int Rep,
    string ExpectedLabel,
    string? Predicted,
    double? Confidence,
    double LatencyMs,
    bool Error,
    bool TimedOut,
    string? FallbackReason,
    string? ServedBy,
    string? Outcome,
    string? ErrorMessage,
    IReadOnlyList<CallRecord> Calls)
{
    public bool Correct => !Error && Predicted is not null && string.Equals(Predicted, ExpectedLabel, StringComparison.Ordinal);

    public bool FellBack => FallbackReason is not null;
}
