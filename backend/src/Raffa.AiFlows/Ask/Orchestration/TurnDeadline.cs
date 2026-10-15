using System.Diagnostics;
using System.Globalization;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiGateway.Telemetry;

namespace Raffa.AiFlows.Ask.Orchestration;

/// <summary>
/// A-04: the caps on one Ask turn (<see cref="AskTurnOptions.TurnDeadline"/>,
/// <see cref="AskTurnOptions.AgentPhaseDeadline"/>, <see cref="AskTurnOptions.AnswerDeadline"/>),
/// composed with the caller's own token. The orchestrator owns the turn cap because
/// <c>StepRunner</c> rethrows a linked-token cancellation (only a step's own deadline is turned into
/// a skipped step): a phase that hits its cap is degraded explicitly by the phase, never truncated
/// silently.
///
/// <para>
/// With no cap configured (the default) nothing is linked: <see cref="ForAgents"/> and
/// <see cref="ForAnswer"/> return the caller's token untouched and <see cref="Active"/> is
/// <see langword="false"/>, so the turn runs and audits exactly as before.
/// </para>
/// </summary>
internal sealed class TurnDeadline : IDisposable
{
    private readonly AskTurnOptions options;
    private readonly CancellationToken caller;
    private readonly CancellationTokenSource? turnCts;
    private readonly List<CancellationTokenSource> phases = [];
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private string expired = "none";

    private TurnDeadline(AskTurnOptions options, CancellationToken caller)
    {
        this.options = options;
        this.caller = caller;
        Active = Positive(options.TurnDeadline) || Positive(options.AgentPhaseDeadline) || Positive(options.AnswerDeadline);

        if (Positive(options.TurnDeadline))
        {
            turnCts = CancellationTokenSource.CreateLinkedTokenSource(caller);
            turnCts.CancelAfter(options.TurnDeadline!.Value);
        }
    }

    /// <summary>Starts the clock of a turn. <paramref name="options"/> <see langword="null"/> = no cap.</summary>
    public static TurnDeadline Start(AskTurnOptions? options, CancellationToken caller) =>
        new(options ?? new AskTurnOptions(), caller);

    /// <summary>Whether any cap is configured; the audit fragment is only written when it is.</summary>
    public bool Active { get; }

    /// <summary>The phase that was cut short (<c>agents</c>, <c>answer</c>, or <c>turn</c> when the whole-turn
    /// cap fired), or <c>none</c>.</summary>
    public string ExpiredPhase => expired;

    /// <summary>The token the agent phase runs under: caller ∪ turn cap ∪ agent-phase cap.</summary>
    public CancellationToken ForAgents() => Phase(options.AgentPhaseDeadline);

    /// <summary>The token the answer phase runs under: caller ∪ turn cap ∪ answer cap.</summary>
    public CancellationToken ForAnswer() => Phase(options.AnswerDeadline);

    /// <summary>
    /// Whether <paramref name="phaseToken"/> ended because a cap fired and not because the caller
    /// gave up. The caller's own cancellation is never a degrade: it propagates as today.
    /// </summary>
    public bool Expired(CancellationToken phaseToken) =>
        phaseToken.IsCancellationRequested && !caller.IsCancellationRequested;

    /// <summary>Records that <paramref name="phase"/> was cut short (the first cut wins) and returns <see langword="true"/>.</summary>
    public bool Mark(string phase)
    {
        if (expired == "none")
        {
            expired = turnCts?.IsCancellationRequested == true ? "turn" : phase;
        }

        return true;
    }

    /// <summary>The turn-row fragment: <c>deadline=none|agents|answer|turn turnMs=N</c>.</summary>
    public string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"deadline={expired} turnMs={stopwatch.ElapsedMilliseconds}");

    /// <summary>Leaves <see cref="Describe"/> on the turn's audit row, when a cap is configured.</summary>
    public void WriteDetail()
    {
        if (Active)
        {
            RunContext.AddTurnDetail(Describe());
        }
    }

    public void Dispose()
    {
        foreach (var phase in phases)
        {
            phase.Dispose();
        }

        phases.Clear();
        turnCts?.Dispose();
    }

    private CancellationToken Phase(TimeSpan? cap)
    {
        var parent = turnCts?.Token ?? caller;
        if (!Positive(cap))
        {
            return parent;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(parent);
        cts.CancelAfter(cap!.Value);
        phases.Add(cts);
        return cts.Token;
    }

    private static bool Positive(TimeSpan? value) => value is { } v && v > TimeSpan.Zero;

    /// <summary>The explicit pack item an agent phase cut short adds, so the answer says what is
    /// missing instead of silently dropping the market analysis (plan 3.2).</summary>
    internal static PackItem AgentsSkippedItem(bool italian) =>
        new(
            "calc:deadline:market-analysis",
            PackCorpus.Calc,
            italian ? "Analisi di mercato non disponibile" : "Market analysis not available",
            null, null, null,
            AgentsSkippedSentence(italian),
            null, null, null,
            "turn deadline",
            []);

    /// <summary>The sentence both the pack item and the deterministic lead say.</summary>
    internal static string AgentsSkippedSentence(bool italian) =>
        italian
            ? "L'analisi di mercato non è stata completata entro il tempo disponibile: la risposta usa solo i dati del contratto."
            : "The market analysis was not completed within the time available: the answer uses only the contract's own data.";

    /// <summary>The deterministic lead of a degraded turn: the skipped-analysis sentence, then the data check's own lead.</summary>
    internal static string DegradedLead(string? lead, bool italian) =>
        AgentsSkippedSentence(italian) + "\n\n" + (lead ?? string.Empty);
}
