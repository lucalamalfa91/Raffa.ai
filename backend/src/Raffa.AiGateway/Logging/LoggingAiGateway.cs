using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Telemetry;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiGateway.Logging;

/// <summary>
/// <see cref="IAiGateway"/> decorator that persists the reproducibility metadata every role call
/// already produces (task E02/F01/US01/T02, ai-gateway-logging; parent story
/// us-01-ai-gateway-classification AC-2: "logging model/version/prompt/timestamp/input-hash").
/// Task E02/F01/US01/T01 guaranteed every successful <see cref="IAiGateway"/> call returns an
/// <see cref="AiCallMetadata"/> record on its result (see that type's own doc comment); this type
/// is the "persists this record" half of that split.
///
/// A decorator rather than logic baked into <see cref="Fixtures.FixtureAiGateway"/> because
/// logging is cross-cutting: it applies identically to the fixture today and to a later
/// Foundry-backed <see cref="IAiGateway"/> implementation, without either one duplicating the
/// audit-write concern. A composition root wraps whichever inner <see cref="IAiGateway"/> it
/// constructs with this type, so domain code (which only ever sees <see cref="IAiGateway"/>,
/// AC-3) is unaffected by the wrap.
///
/// Writes through <see cref="IAuditWriter"/> (in <c>Raffa.SharedKernel</c>, not
/// <c>Raffa.Audit</c> — the same gateway-abstraction shape
/// <c>Raffa.Documents.Contracts.Application.DocumentUploadService</c> already uses), because
/// module-map.md fixes AI usage as one of <c>Raffa.Audit</c>'s own <c>AuditEvent</c> purposes
/// ("AuditEvent (access, correction, negotiation, AI usage)") and "Audit abstraction ◄── all
/// modules (write-only)" — this project already references <c>Raffa.SharedKernel</c> for
/// <see cref="Result{T}"/>, so no new project reference is needed and ADR-002's "AI Gateway must
/// never reference a domain module" rule stays intact.
///
/// Only <em>successful</em> calls are logged — a failed call (for example empty input) never
/// reaches a model and therefore never produces an <see cref="AiCallMetadata"/> to log. The audit
/// <c>Detail</c> carries model id/version, prompt version, input hash, and the no-training flag —
/// never the raw prompt, document text, or retrieved content (ADR-011: "Raw prompt and retrieved
/// contract text are never written to logs").
/// </summary>
public sealed class LoggingAiGateway : IAiGateway
{
    /// <summary>
    /// The reserved, documented non-human principal for an automated AI Gateway call (ADR-011 w16
    /// clause 16 convention: a <c>system:&lt;component&gt;</c>-shaped string an authenticated
    /// caller's token subject can never produce) — there is no human caller to attribute in the
    /// first place, the gateway itself is the actor.
    /// </summary>
    private const string SystemActor = "ai-gateway";

    /// <summary>
    /// <see cref="AuditEntry.ResourceType"/> for every AI Gateway log row, paired with
    /// <see cref="AuditEntry.ResourceId"/> = the call's <see cref="AiCallMetadata.InputHash"/>
    /// (see <see cref="LogAsync"/>) rather than a domain entity id, because the gateway is
    /// deliberately domain-agnostic (module-map "Rule of direction") and has no document/contract
    /// id to point at — the hash is itself the reproducible pointer ADR-011's "Assumptions" section
    /// describes: "verify a given model/version ran on a given input without storing the
    /// confidential input itself".
    /// </summary>
    private const string ResourceType = "ai_call";

    /// <summary>Written for <c>run=</c>/<c>turn=</c> when no <see cref="RunContext"/> is active, so
    /// the key is always present and a query on it never has to treat absence as a case.</summary>
    private const string NoneValue = "none";

    private readonly IAiGateway _inner;
    private readonly IAuditWriter _auditWriter;
    private readonly ITenantContext _tenantContext;
    private readonly AiGatewayComplianceOptions _complianceOptions;

    /// <summary>
    /// Serialises the audit writes of one decorator instance. The decorator is Scoped and its
    /// <see cref="IAuditWriter"/> wraps a Scoped DbContext, which is not thread-safe; callers that
    /// run several role calls concurrently within one request (extraction stages) still get one
    /// audit row per call, written one at a time, while the inner provider calls stay concurrent.
    /// </summary>
    private readonly SemaphoreSlim _auditWriteLock = new(1, 1);

    public LoggingAiGateway(
        IAiGateway inner,
        IAuditWriter auditWriter,
        ITenantContext tenantContext,
        AiGatewayComplianceOptions complianceOptions)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(complianceOptions);

        // ADR-011 treats no-training as locked, not a deployment preference: "the gateway is the
        // single choke point that proves it". Refusing to construct over a non-compliant
        // configuration makes that literally true, and fails at composition-root wiring time
        // (once, at startup) rather than silently per call.
        if (!complianceOptions.NoTraining)
        {
            throw new InvalidOperationException(
                "AI Gateway refuses to start: AiGatewayComplianceOptions.NoTraining is false. " +
                "ADR-011 requires every Foundry call to run through a no-training endpoint; this " +
                "is not a configurable opt-out.");
        }

        _inner = inner;
        _auditWriter = auditWriter;
        _tenantContext = tenantContext;
        _complianceOptions = complianceOptions;
    }

    /// <inheritdoc/>
    public Task<Result<AiClassificationResult>> ClassifyAsync(
        AiClassificationRequest request, CancellationToken cancellationToken = default) =>
        InvokeAsync(
            "classified", "chat", "classifier", fallbackStep: null,
            () => _inner.ClassifyAsync(request, cancellationToken),
            r => r.Metadata, extraDetail: null, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<AiExtractionResult>> ExtractAsync(
        AiExtractionRequest request, CancellationToken cancellationToken = default) =>
        InvokeAsync(
            "extracted", "chat", "extractor", fallbackStep: request.StageName,
            () => _inner.ExtractAsync(request, cancellationToken),
            r => r.Metadata, extraDetail: null, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<AiEmbeddingResult>> EmbedAsync(
        AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
        InvokeAsync(
            "embedded", "embeddings", "embedder", fallbackStep: null,
            () => _inner.EmbedAsync(request, cancellationToken),
            r => r.Metadata, extraDetail: null, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<AiAnswerResult>> AnswerAsync(
        AiAnswerRequest request, CancellationToken cancellationToken = default) =>
        InvokeAsync(
            "answered", "chat", "answerer", fallbackStep: null,
            () => _inner.AnswerAsync(request, cancellationToken),
            r => r.Metadata, extraDetail: null, cancellationToken);

    /// <inheritdoc/>
    public Task<Result<AiAnalysisResult>> AnalyzeAsync(
        AiAnalysisRequest request, CancellationToken cancellationToken = default) =>
        InvokeAsync(
            "analyzed", "chat", request.AgentName, fallbackStep: request.AgentName,
            () => _inner.AnalyzeAsync(request, cancellationToken),
            r => r.Metadata, extraDetail: null, cancellationToken);

    /// <summary>ADR-030: one <c>ai_call</c> row per web research, carrying the query's hash and the
    /// source count -- never the query text.</summary>
    public Task<Result<AiResearchResult>> ResearchAsync(
        AiResearchRequest request, CancellationToken cancellationToken = default) =>
        InvokeAsync(
            "researched", "chat", "web-researcher", fallbackStep: null,
            () => _inner.ResearchAsync(request, cancellationToken),
            r => r.Metadata,
            r => $"sourceCount={r.Sources.Count} offTopic={r.OffTopic} " +
                 $"queryHash={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Query)))}",
            cancellationToken);

    public Task<Result<AiOcrResult>> OcrAsync(
        AiOcrRequest request, CancellationToken cancellationToken = default) =>
        // ADR-017: "Per-page OCR usage MUST be logged (page count, model id, cost attribution)" --
        // the one field the other roles' log line does not carry, so it is threaded through as an
        // addendum rather than duplicating LogAsync's whole body.
        InvokeAsync(
            "ocr", "ocr", "ocr", fallbackStep: null,
            () => _inner.OcrAsync(request, cancellationToken),
            r => r.Metadata, r => $"pageCount={r.Pages.Count}", cancellationToken);

    /// <summary>
    /// The one path every role call takes (plan T-01): runs the inner call inside an
    /// <c>Raffa.Agents</c> span (agent name and version, tenant hash, tokens, outcome, latency --
    /// never text), then, on success, writes the audit row stamped with the same agent, run, turn
    /// and step. A failed inner call still gets a span (outcome <c>error</c>) but no audit row,
    /// exactly as before. <paramref name="fallbackStep"/> names the step when no
    /// <see cref="RunContext"/> step is active (the agent name for an analyst call, the stage name
    /// for an extraction stage), so every <c>ai.*</c> row carries a <c>step=</c>.
    /// </summary>
    private async Task<Result<T>> InvokeAsync<T>(
        string role,
        string operation,
        string agentName,
        string? fallbackStep,
        Func<Task<Result<T>>> call,
        Func<T, AiCallMetadata> metadataOf,
        Func<T, string?>? extraDetail,
        CancellationToken cancellationToken)
    {
        var context = RunContext.Current;
        var runId = context?.RunId ?? NoneValue;
        var turnId = context?.TurnId ?? NoneValue;
        var stepName = context?.StepName ?? RunContext.Sanitize(fallbackStep) ?? RunContext.Sanitize(agentName) ?? NoneValue;
        var agent = RunContext.Sanitize(agentName) ?? NoneValue;

        using var activity = AgentTelemetry.Source.StartActivity($"{operation} {agent}", ActivityKind.Client);
        if (activity is not null)
        {
            activity.SetTag(AgentTelemetry.OperationName, operation);
            activity.SetTag(AgentTelemetry.AgentName, agent);
            activity.SetTag(AgentTelemetry.Role, role);
            activity.SetTag(AgentTelemetry.RunId, runId);
            activity.SetTag(AgentTelemetry.TurnId, turnId);
            activity.SetTag(AgentTelemetry.StepName, stepName);
            if (_tenantContext.Current is { } tenant)
            {
                activity.SetTag(AgentTelemetry.TenantHash, AgentTelemetry.HashTenant(tenant));
            }
        }

        var clock = Stopwatch.StartNew();
        var outcome = AgentTelemetry.OutcomeError;
        try
        {
            var result = await call().ConfigureAwait(false);
            clock.Stop();

            if (result.IsFailure)
            {
                // A failed call never reaches a model, or the model's answer was unusable: no
                // metadata, no audit row; the span says so (outcome stays "error"). The error text
                // is not copied (it can quote provider output).
                return result;
            }

            var metadata = metadataOf(result.Value);
            if (activity is not null)
            {
                activity.SetTag(AgentTelemetry.AgentVersion, metadata.PromptVersion);
                activity.SetTag(AgentTelemetry.ResponseModel, metadata.ModelId);
                activity.SetTag(AgentTelemetry.ModelVersion, metadata.ModelVersion);
                if (metadata.Usage is { } usage)
                {
                    activity.SetTag(AgentTelemetry.UsageInputTokens, usage.PromptTokens);
                    activity.SetTag(AgentTelemetry.UsageOutputTokens, usage.CompletionTokens);
                }
            }

            var detail = string.Join(' ', new[]
            {
                extraDetail?.Invoke(result.Value),
                $"agent={agent} run={runId} turn={turnId} step={stepName} " +
                $"latencyMs={clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)} outcome={AgentTelemetry.OutcomeOk}",
            }.Where(part => !string.IsNullOrEmpty(part)));

            await LogBestEffortAsync(role, metadata, cancellationToken, detail).ConfigureAwait(false);

            outcome = AgentTelemetry.OutcomeOk;
            return result;
        }
        catch (OperationCanceledException)
        {
            outcome = AgentTelemetry.OutcomeCancelled;
            throw;
        }
        finally
        {
            if (outcome == AgentTelemetry.OutcomeError)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
            }

            activity?.SetTag(AgentTelemetry.Outcome, outcome);
            activity?.SetTag(AgentTelemetry.LatencyMs, clock.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// A successful role call must not be rewritten as "the role could not be reached" because
    /// the audit <c>SaveChanges</c> hit EF's retry-exhaustion
    /// (<see cref="TransientDataAccessFault"/>). A missing tenant scope is still a caller bug
    /// and still throws. A non-transient audit failure still throws (ADR-011).
    /// </summary>
    private async Task LogBestEffortAsync(
        string role, AiCallMetadata metadata, CancellationToken cancellationToken, string extraDetail)
    {
        try
        {
            await LogAsync(role, metadata, cancellationToken, extraDetail).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException && TransientDataAccessFault.IsTransient(exception))
        {
            // Classify/extract already succeeded. Failing the call here is what marked every
            // document in a batch Failed with "classify role could not be reached".
        }
    }

    /// <summary>
    /// Writes one append-only audit row per successful role call. Requires an active
    /// <see cref="ITenantContext.BeginScope"/> scope: an AI Gateway call that cannot be attributed
    /// to a tenant is a caller bug, not something to log anonymously or drop silently — the same
    /// "fail closed" posture <see cref="ITenantContext.Current"/>'s own doc comment describes for
    /// RLS.
    /// </summary>
    /// <param name="extraDetail">The call's own fields (agent, run, turn, step, latency, and a role-specific addendum).</param>
    private async Task LogAsync(
        string role, AiCallMetadata metadata, CancellationToken cancellationToken, string extraDetail)
    {
        var tenantId = _tenantContext.Current ?? throw new InvalidOperationException(
            $"AI Gateway logging requires an active tenant scope (ITenantContext.BeginScope); " +
            $"none was active for the '{role}' role call. ADR-011/brief §8 require every AI call " +
            "to be attributable to a tenant.");

        // Reproducibility fields only (ADR-011): model id/version, prompt version, input hash, and
        // the compliance posture in force for this call. Never the raw prompt, document text, or
        // retrieved content.
        var detail =
            $"model={metadata.ModelId} modelVersion={metadata.ModelVersion} " +
            $"promptVersion={metadata.PromptVersion} inputHash={metadata.InputHash} " +
            $"noTraining={_complianceOptions.NoTraining}";

        if (metadata.Usage is { } usage)
        {
            // Appendix C rule 8: spend must be observable per call — token counts, never text.
            detail += $" promptTokens={usage.PromptTokens} completionTokens={usage.CompletionTokens}";
        }

        detail += $" {extraDetail}";

        await _auditWriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _auditWriter.WriteAsync(
                new AuditEntry(
                    tenantId,
                    SystemActor,
                    $"ai.{role}",
                    ResourceType,
                    metadata.InputHash,
                    metadata.RespondedAtUtc,
                    detail),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _auditWriteLock.Release();
        }
    }
}
