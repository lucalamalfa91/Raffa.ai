using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Telemetry;

/// <summary>
/// The one <see cref="ActivitySource"/> every agent call is traced on (plan task T-01). It is the
/// BCL <see cref="System.Diagnostics"/> type only: no exporter is wired here, so with no listener
/// <see cref="ActivitySource.StartActivity(string, ActivityKind)"/> returns <see langword="null"/>
/// and the cost is a single branch. A host that wants the spans adds
/// <c>AddSource(AgentTelemetry.SourceName)</c> to its OpenTelemetry tracer provider.
///
/// Span attributes follow the OpenTelemetry GenAI semantic conventions (<c>gen_ai.*</c>) where one
/// exists, and a <c>raffa.*</c> attribute otherwise. Never a prompt, a document or an answer: only
/// names, versions, ids, counts, a tenant hash and the outcome.
/// </summary>
public static class AgentTelemetry
{
    public const string SourceName = "Raffa.Agents";

    public static readonly ActivitySource Source = new(SourceName, "1.0.0");

    // gen_ai.* (OpenTelemetry GenAI semantic conventions).
    public const string OperationName = "gen_ai.operation.name";
    public const string ProviderName = "gen_ai.provider.name";
    public const string AgentName = "gen_ai.agent.name";
    public const string AgentVersion = "gen_ai.agent.version";
    public const string ResponseModel = "gen_ai.response.model";
    public const string UsageInputTokens = "gen_ai.usage.input_tokens";
    public const string UsageOutputTokens = "gen_ai.usage.output_tokens";

    // raffa.* (no convention covers these).
    public const string TenantHash = "raffa.tenant.hash";
    public const string RunId = "raffa.run.id";
    public const string TurnId = "raffa.turn.id";
    public const string StepName = "raffa.step.name";
    public const string Role = "raffa.ai.role";
    public const string Outcome = "raffa.outcome";
    public const string ModelVersion = "raffa.model.version";
    public const string LatencyMs = "raffa.latency_ms";

    // The step runner's own span (plan A-01, Raffa.AiGateway.Agents.StepRunner): one per step, the
    // parent of the gateway call spans it makes.
    public const string StepStatus = "raffa.step.status";
    public const string StepReason = "raffa.step.reason";
    public const string StepAttempts = "raffa.step.attempts";
    public const string VerifierIntervened = "raffa.verifier.intervened";

    public const string OutcomeOk = "ok";
    public const string OutcomeError = "error";
    public const string OutcomeCancelled = "cancelled";
    public const string OutcomeSkipped = "skipped";
    public const string OutcomeFellBack = "fallback";

    /// <summary>A short, stable, non-reversible handle for a tenant: enough to group spans per
    /// tenant, never the tenant id itself (the first 8 bytes of its SHA-256, hex).</summary>
    public static string HashTenant(TenantId tenantId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tenantId.Value.ToString("N"))), 0, 8).ToLowerInvariant();
}
