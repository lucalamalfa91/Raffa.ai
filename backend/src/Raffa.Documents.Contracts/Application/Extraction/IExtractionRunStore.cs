using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Persistence of one staged-extraction run (F5 PR-1). Every method must be called inside the tenant
/// scope of <see cref="ExtractionRunRequest.TenantId"/> (ADR-009): <see cref="OpenAsync"/> verifies
/// <c>ITenantContext.Current</c> and throws <see cref="InvalidOperationException"/> when it is missing
/// or belongs to another tenant; a handle is valid only for the store instance (and so the scoped
/// DbContext) that opened it. The store never receives a JSON string: it takes typed
/// <see cref="StageFacts"/>, parsed by the caller before anything is staged.
/// </summary>
public interface IExtractionRunStore
{
    /// <summary>Loads the document, ensures its Contract, plans fresh-vs-resume, loads field protection,
    /// records the classification evidence and marks the document Processing. Nothing is saved yet
    /// (the first save is in <see cref="StartStagesAsync"/>). A missing document is a
    /// <see cref="Result{T}"/> failure; a missing tenant scope throws.</summary>
    Task<Result<ExtractionRun>> OpenAsync(ExtractionRunRequest request, CancellationToken cancellationToken);

    /// <summary>Creates and saves one Running ExtractionJob per stage in <paramref name="stages"/>
    /// (one SaveChangesAsync). Returns the job ids by stage.</summary>
    Task<IReadOnlyDictionary<ExtractionStage, EntityId>> StartStagesAsync(
        ExtractionRun run, IReadOnlyList<ExtractionStage> stages, CancellationToken cancellationToken);

    /// <summary>Rebuilds the result of a stage finished by the run being resumed. No save.</summary>
    Task<StagedExtractionStageResult> ReuseCheckpointAsync(
        ExtractionRun run, ExtractionStage stage, CancellationToken cancellationToken);

    /// <summary>Applies parsed facts (contract fields + evidence rows, or replace-not-add rows), then the
    /// job's terminal status, in ONE SaveChangesAsync. <paramref name="facts"/>.Stage must equal
    /// <paramref name="stage"/>.</summary>
    Task<StagedExtractionStageResult> ApplyAsync(
        ExtractionRun run, ExtractionStage stage, StageFacts facts, string? modelId, CancellationToken cancellationToken);

    /// <summary>Marks the stage job Failed with a typed kind and saves. <paramref name="modelId"/> is
    /// kept on the job when the gateway did answer but its payload could not be used.</summary>
    Task<StagedExtractionStageResult> FailAsync(
        ExtractionRun run,
        ExtractionStage stage,
        string error,
        ExtractionStageFailureKind kind,
        CancellationToken cancellationToken,
        string? modelId = null);

    /// <summary>Derived deadline, status officialisation, document status, save, the "never Review 0
    /// fields" auto-complete and the audit row. Returns the summary.</summary>
    Task<StagedExtractionSummary> CloseAsync(
        ExtractionRun run, IReadOnlyList<StagedExtractionStageResult> stageResults, CancellationToken cancellationToken);

    /// <summary>F5-T05 (context dependencies, resume): the facts a resumed run's list stages should see
    /// for the scalar <paramref name="stages"/> it did not re-run, rebuilt from the Contract's current
    /// values (the ones a human or an earlier auto-accept left there), restricted to fields whose latest
    /// evidence is accepted. Reads only; must not be called while another store call is in flight.
    /// The default (no store support) is "no context".</summary>
    Task<IReadOnlyDictionary<ExtractionStage, StageFacts>> ReadScalarContextAsync(
        ExtractionRun run, IReadOnlyList<ExtractionStage> stages, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<ExtractionStage, StageFacts>>(new Dictionary<ExtractionStage, StageFacts>());

    /// <summary>F5-T05 (Q2): records the hash of the request text this stage actually sent (context
    /// included) on its still-tracked job; persisted by the stage's own <see cref="ApplyAsync"/> or
    /// <see cref="FailAsync"/> save. Called only when it differs from the run's base hash. Not safe to
    /// call while another store call is in flight. Default: no-op.</summary>
    void SetStageInputHash(ExtractionRun run, ExtractionStage stage, string inputHash)
    {
    }
}

/// <summary>What opening a run needs. <paramref name="InputHash"/> identifies the page-marked text
/// (and pipeline version) the run reads; a resume only reuses checkpoints with the same hash.
/// <paramref name="NoReuseStages"/> are stages whose request text depends on other stages' output (context
/// dependencies on): their checkpoint is never reused, so a resume never mixes modes.</summary>
public sealed record ExtractionRunRequest(
    TenantId TenantId,
    EntityId DocumentId,
    string InputHash,
    int PageCount,
    double? ClassificationConfidence,
    IReadOnlySet<ExtractionStage>? NoReuseStages = null);
