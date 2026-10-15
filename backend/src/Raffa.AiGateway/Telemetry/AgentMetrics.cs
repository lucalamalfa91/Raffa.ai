using System.Diagnostics.Metrics;

namespace Raffa.AiGateway.Telemetry;

/// <summary>
/// The agents' metrics, on the same name as their spans (<see cref="AgentTelemetry.SourceName"/>).
/// BCL <see cref="Meter"/> only: with no listener recording is a single branch. A host adds
/// <c>AddMeter(AgentMetrics.MeterName)</c> to export. Tags are agent and verifier names and an
/// outcome, never text (ADR-011).
/// </summary>
public static class AgentMetrics
{
    public const string MeterName = "Raffa.Agents";

    public const string VerifierInterventionsName = "raffa.agent.verifier.interventions";

    public const string TagAgent = "raffa.agent";
    public const string TagVerifier = "raffa.verifier";
    public const string TagOutcome = "raffa.verifier.outcome";

    /// <summary>The verifier rejected an output and the agent was sent back once with the violation named.</summary>
    public const string OutcomeRetried = "retried";

    /// <summary>The verifier rejected an output and no retry was left (or allowed): the step degraded.</summary>
    public const string OutcomeRejected = "rejected";

    public static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> VerifierInterventions = Meter.CreateCounter<long>(
        VerifierInterventionsName, unit: "{intervention}",
        description: "Outputs a verifier rejected (plan A-03: 'verifier intervened', per agent).");

    public static void RecordVerifierIntervention(string agent, string verifier, string outcome) =>
        VerifierInterventions.Add(
            1,
            new KeyValuePair<string, object?>(TagAgent, agent),
            new KeyValuePair<string, object?>(TagVerifier, verifier),
            new KeyValuePair<string, object?>(TagOutcome, outcome));
}
