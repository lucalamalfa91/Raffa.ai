using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure.Extraction;
using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Handle of one open extraction run. The caller may read what is public; everything the EF store needs
/// across calls (the tracked Contract/Document, field protection, run evidence, stage jobs, the accepted
/// supplier name and the store instance that owns it) is internal to the module. A class, not a record,
/// so it is never copied by value.
/// </summary>
public sealed class ExtractionRun
{
    internal ExtractionRun(
        ExtractionRunStore owner,
        TenantId tenantId,
        Document document,
        Contract contract,
        ExtractionRunStore.RunPlan plan,
        int pageCount,
        ExtractionRunStore.FieldProtection protection,
        bool replaceUnattributedRows,
        DateTimeOffset openedAt,
        double? classificationConfidence)
    {
        Owner = owner;
        TenantId = tenantId;
        Document = document;
        DocumentId = document.Id;
        Contract = contract;
        Plan = plan;
        PageCount = pageCount;
        Protection = protection;
        ReplaceUnattributedRows = replaceUnattributedRows;
        OpenedAt = openedAt;
        ClassificationConfidence = classificationConfidence;
        ReusedStages = new HashSet<ExtractionStage>(plan.Reused.Keys);
        StagesToRun = ExtractionPipeline.Stages.Where(stage => !plan.Reused.ContainsKey(stage)).ToList();
    }

    public Guid RunId => Plan.RunId;

    public string InputHash => Plan.InputHash;

    public TenantId TenantId { get; }

    public EntityId DocumentId { get; }

    public EntityId ContractId => Contract.Id;

    public int PageCount { get; }

    /// <summary>Stages reused from the resumed run's checkpoints (empty on a fresh run).</summary>
    public IReadOnlySet<ExtractionStage> ReusedStages { get; }

    /// <summary><see cref="ExtractionPipeline.Stages"/> minus <see cref="ReusedStages"/>, in pipeline order.</summary>
    public IReadOnlyList<ExtractionStage> StagesToRun { get; }

    public bool IsResume => ReusedStages.Count > 0;

    /// <summary>F5-T04: set by the orchestrator when the document was longer than the chunking budget and
    /// part of it was NOT read. Never silent: the document is routed to review, the audit row says so and the
    /// summary carries it.</summary>
    public bool Truncated { get; private set; }

    /// <summary>F5-T03: the pipeline + prompt version of a run that used a non-default prompt layout
    /// (<c>staged-v2-docfirst/foundry-extract-v3-docfirst</c>); null for the historic layout. Persisted on the
    /// run's audit row.</summary>
    public string? PipelineVersion { get; private set; }

    /// <summary>F5-T03: records the pipeline + prompt version the run used.</summary>
    public void RecordPipelineVersion(string version) => PipelineVersion = version;

    /// <summary>F5-T04: records that part of the document was not read.</summary>
    public void MarkTruncated() => Truncated = true;

    /// <summary>Identity guard: the store instance (so the DbContext scope) that opened this run.</summary>
    internal ExtractionRunStore Owner { get; }

    /// <summary>Tracked entity of the owning scope.</summary>
    internal Contract Contract { get; }

    internal Document Document { get; }

    internal ExtractionRunStore.RunPlan Plan { get; }

    internal ExtractionRunStore.FieldProtection Protection { get; }

    /// <summary>Whether rows with no source document belong to this document's run.</summary>
    internal bool ReplaceUnattributedRows { get; }

    /// <summary>The clock read taken when the run was opened (evidence and audit timestamp).</summary>
    internal DateTimeOffset OpenedAt { get; }

    internal double? ClassificationConfidence { get; }

    /// <summary>Running jobs of this call, by stage.</summary>
    internal Dictionary<ExtractionStage, ExtractionJob> Jobs { get; } = [];

    internal Dictionary<ExtractionStage, EntityId> StageJobIds { get; } = [];

    internal List<(string FieldName, double? Confidence)> RunEvidence { get; } = [];

    /// <summary>First accepted <c>supplier</c> legal name of the run; only <c>metadata</c> produces one.</summary>
    internal string? AcceptedSupplierName { get; set; }
}
