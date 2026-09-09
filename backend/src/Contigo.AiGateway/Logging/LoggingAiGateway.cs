using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Contracts;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Tenancy;

namespace Contigo.AiGateway.Logging;

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
/// Writes through <see cref="IAuditWriter"/> (in <c>Contigo.SharedKernel</c>, not
/// <c>Contigo.Audit</c> — the same gateway-abstraction shape
/// <c>Contigo.Documents.Contracts.Application.DocumentUploadService</c> already uses), because
/// module-map.md fixes AI usage as one of <c>Contigo.Audit</c>'s own <c>AuditEvent</c> purposes
/// ("AuditEvent (access, correction, negotiation, AI usage)") and "Audit abstraction ◄── all
/// modules (write-only)" — this project already references <c>Contigo.SharedKernel</c> for
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
    /// Placeholder actor for an automated AI Gateway call. Deliberately not
    /// <c>DocumentUploadService.UnattributedActor</c>'s <c>"unattributed"</c> value: that constant
    /// means "a human caller exists but ADR-010 auth is not wired yet to name them" — here there
    /// is no human caller to attribute in the first place, the gateway itself is the actor.
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
    public async Task<Result<AiClassificationResult>> ClassifyAsync(
        AiClassificationRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _inner.ClassifyAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            await LogAsync("classified", result.Value.Metadata, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<Result<AiExtractionResult>> ExtractAsync(
        AiExtractionRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _inner.ExtractAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            await LogAsync("extracted", result.Value.Metadata, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<Result<AiEmbeddingResult>> EmbedAsync(
        AiEmbeddingRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _inner.EmbedAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            await LogAsync("embedded", result.Value.Metadata, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<Result<AiAnswerResult>> AnswerAsync(
        AiAnswerRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _inner.AnswerAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            await LogAsync("answered", result.Value.Metadata, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<Result<AiOcrResult>> OcrAsync(
        AiOcrRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _inner.OcrAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            // ADR-017: "Per-page OCR usage MUST be logged (page count, model id, cost
            // attribution)" — the one field the other four roles' log line does not carry, so it
            // is threaded through as an addendum rather than duplicating LogAsync's whole body.
            await LogAsync(
                    "ocr",
                    result.Value.Metadata,
                    cancellationToken,
                    extraDetail: $"pageCount={result.Value.Pages.Count}")
                .ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// Writes one append-only audit row per successful role call. Requires an active
    /// <see cref="ITenantContext.BeginScope"/> scope: an AI Gateway call that cannot be attributed
    /// to a tenant is a caller bug, not something to log anonymously or drop silently — the same
    /// "fail closed" posture <see cref="ITenantContext.Current"/>'s own doc comment describes for
    /// RLS. Mirrors <c>DocumentUploadService.UploadAsync</c>'s own choice to let a write failure
    /// here throw and fail the call: "ADR-011 treats audit as a compliance control, not a
    /// best-effort side-channel".
    /// </summary>
    /// <param name="extraDetail">
    /// Role-specific addendum appended to the standard reproducibility fields — today only
    /// <see cref="OcrAsync"/> supplies one (page count, ADR-017). <see langword="null"/> for every
    /// other role, unchanged from before this parameter existed.
    /// </param>
    private async Task LogAsync(
        string role, AiCallMetadata metadata, CancellationToken cancellationToken, string? extraDetail = null)
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

        if (extraDetail is not null)
        {
            detail += $" {extraDetail}";
        }

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
