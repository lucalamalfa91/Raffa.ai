using System.Collections.Concurrent;

namespace Raffa.AiGateway.Telemetry;

/// <summary>
/// What an AI call belongs to: the <see cref="TurnId"/> of the user turn, the <see cref="RunId"/>
/// of the agentic run inside it, and the <see cref="StepName"/> being executed. Immutable; a
/// change is a new value (see <see cref="RunContext"/>).
/// </summary>
public sealed record RunContextData(string? RunId, string? TurnId, string? StepName)
{
    /// <summary>Audit fragments the turn's steps leave for the turn's one audit row
    /// (<see cref="RunContext.AddTurnDetail"/>); shared by every scope of the same turn.</summary>
    internal ConcurrentQueue<string>? TurnDetails { get; init; }
}

/// <summary>
/// Ambient (<see cref="AsyncLocal{T}"/>) run context that flows into every task a step starts, so
/// <see cref="Logging.LoggingAiGateway"/> can stamp <c>run=</c>, <c>step=</c> and <c>turn=</c> on
/// each <c>ai.*</c> audit row and span without any call signature carrying them (plan T-01; the
/// future step runner opens one run and one step per agent through the same three methods).
///
/// Every <c>Begin*</c> returns a scope that restores the previous value when disposed. Values are
/// written into space-separated <c>key=value</c> audit lines and span tags, so they are reduced to
/// a conservative character set and length: an id or a step name can never carry text.
/// </summary>
public static class RunContext
{
    private const int MaxValueLength = 64;
    private const int MaxTurnDetailLength = 1024;

    private static readonly AsyncLocal<RunContextData?> Ambient = new();

    /// <summary>The context of the current async flow, or <see langword="null"/> outside a turn or run.</summary>
    public static RunContextData? Current => Ambient.Value;

    /// <summary>A fresh opaque id (16 hex characters).</summary>
    public static string NewId() => Guid.NewGuid().ToString("N")[..16];

    /// <summary>Starts a user turn: sets <see cref="RunContextData.TurnId"/> (a fresh id by default)
    /// and clears the step and any earlier turn's details; the run id is kept so a turn opened
    /// inside a run stays in it.</summary>
    public static IDisposable BeginTurn(string? turnId = null) =>
        Replace(current => current with { TurnId = Sanitize(turnId) ?? NewId(), StepName = null, TurnDetails = new ConcurrentQueue<string>() });

    /// <summary>Starts an agentic run inside the current turn (a fresh id by default), step cleared.</summary>
    public static IDisposable BeginRun(string? runId = null) =>
        Replace(current => current with { RunId = Sanitize(runId) ?? NewId(), StepName = null });

    /// <summary>Names the step being executed; run and turn are kept.</summary>
    public static IDisposable BeginStep(string stepName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepName);
        return Replace(current => current with { StepName = Sanitize(stepName) });
    }

    /// <summary>
    /// Leaves a fragment for the audit row the current turn writes when it ends (plan F2-T01: the
    /// agentic flow's steps, failures and market-query count). The caller passes names and counts
    /// only; control characters are dropped and the fragment is capped, so a stray newline can
    /// never split an audit line. A no-op outside a turn.
    /// </summary>
    public static void AddTurnDetail(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail) || Ambient.Value?.TurnDetails is not { } details)
        {
            return;
        }

        var cleaned = new string(detail.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length > MaxTurnDetailLength)
        {
            cleaned = cleaned[..MaxTurnDetailLength];
        }

        details.Enqueue(cleaned);
    }

    /// <summary>The fragments left so far by the current turn, space-joined, or <see langword="null"/> when none.</summary>
    public static string? TurnDetail =>
        Ambient.Value?.TurnDetails is { IsEmpty: false } details ? string.Join(' ', details) : null;

    /// <summary>Keeps only letters, digits and <c>. _ - : [ ]</c>; anything else becomes
    /// <c>_</c>; empty stays <see langword="null"/>; capped at 64 characters.</summary>
    internal static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return new string(value.Trim().Take(MaxValueLength)
            .Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or ':' or '[' or ']' ? c : '_')
            .ToArray());
    }

    private static Scope Replace(Func<RunContextData, RunContextData> next)
    {
        var previous = Ambient.Value;
        Ambient.Value = next(previous ?? new RunContextData(null, null, null));
        return new Scope(previous);
    }

    private sealed class Scope(RunContextData? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Ambient.Value = previous;
            }
        }
    }
}
