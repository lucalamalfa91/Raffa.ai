using Raffa.Documents.Contracts.Domain;

namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// Task E13/F04/US01/T02 (documents-v2-api, R-DOC-09 "rows show the real stage ... from the API,
/// not a client-side timer"): maps this codebase's own <see cref="ExtractionStage"/> pipeline onto
/// the six stage names the Documents screen renders (and the V2 prototype's <c>stageLabels</c>).
///
/// <para>
/// The mapping is deliberately coarse where the two vocabularies do not line up one-to-one — the
/// screen shows a progress story, the database records eight extraction stages — but every value
/// is derived from a real row: the most recently <em>started</em> <see cref="ExtractionJob"/> for
/// the document. A document with no started job is honestly "Uploading", never a guessed midpoint.
/// </para>
/// </summary>
public static class DocumentProcessingStageMap
{
    /// <summary>The document exists but nothing has started yet.</summary>
    public const string Uploading = "Uploading";

    /// <summary>The <see cref="ExtractionStage.Classification"/> job is the latest one started.</summary>
    public const string Classifying = "Classifying";

    /// <summary>Classification finished; the text pass is done and no extraction stage has started yet.</summary>
    public const string OcrText = "OCR / text";

    /// <summary>Line items — the tabular pass.</summary>
    public const string SectionsAndTables = "Sections & tables";

    /// <summary>Metadata / commercial terms / dates — the scalar facts.</summary>
    public const string ExtractingFacts = "Extracting facts";

    /// <summary>Clauses, obligations and risks — the last, schema-validated stages.</summary>
    public const string ValidatingSchema = "Validating schema";

    /// <summary>Every stage name the API may report, in screen order (R-DOC-09).</summary>
    public static readonly IReadOnlyList<string> All =
        [Uploading, Classifying, OcrText, SectionsAndTables, ExtractingFacts, ValidatingSchema];

    /// <summary>
    /// The stage to report for a document whose <see cref="DocumentProcessingStatus"/> is
    /// <see cref="DocumentProcessingStatus.Processing"/>, given its extraction jobs. Callers report
    /// <see langword="null"/> for every terminal status — a finished document has no stage.
    /// </summary>
    /// <param name="jobs">This document's extraction jobs (any order).</param>
    public static string Resolve(IReadOnlyCollection<ExtractionJobSnapshot> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        var started = jobs
            .Where(j => j.StartedAt is not null)
            .OrderByDescending(j => j.StartedAt)
            .ToList();

        if (started.Count == 0)
        {
            return Uploading;
        }

        var latest = started[0];
        if (latest.Stage == ExtractionStage.Classification)
        {
            // Classification finished and nothing else has started: the parse/OCR text pass is the
            // honest description of where this document is.
            return latest.Status is ExtractionJobStatus.Queued or ExtractionJobStatus.Running
                ? Classifying
                : OcrText;
        }

        return latest.Stage switch
        {
            ExtractionStage.Metadata or ExtractionStage.CommercialTerms or ExtractionStage.DatesAndRenewalTerms =>
                ExtractingFacts,
            ExtractionStage.LineItems => SectionsAndTables,
            ExtractionStage.LegalClauses or ExtractionStage.Obligations or ExtractionStage.Risk =>
                ValidatingSchema,
            _ => ExtractingFacts,
        };
    }
}

/// <summary>The two fields <see cref="DocumentProcessingStageMap.Resolve"/> needs off an
/// <see cref="ExtractionJob"/>, so the map stays a pure function over projected data.</summary>
public sealed record ExtractionJobSnapshot(ExtractionStage Stage, ExtractionJobStatus Status, DateTimeOffset? StartedAt);
