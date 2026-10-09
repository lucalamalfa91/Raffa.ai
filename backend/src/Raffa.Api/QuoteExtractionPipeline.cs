using System.Text;
using System.Text.Json;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Quotes.Application.Extraction;
using Raffa.Quotes.Application.Normalization;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Raffa.Api;

/// <summary>
/// Orchestrator for task E05/F01/US01/T01 (quote-extraction; parent story
/// us-01-quote-line-extraction AC-1/AC-2/AC-4). This is the one place in the solution that calls
/// both <c>Raffa.AiGateway</c> and <c>Raffa.Quotes</c>: ADR-002's dependency-direction rule
/// (<c>Raffa.ArchitectureTests.DependencyDirectionTests</c>) allows <c>Raffa.Quotes</c> to
/// reference only <c>Raffa.SharedKernel</c>/<c>Raffa.Benchmark</c>, and
/// <c>Raffa.Documents.Contracts</c> (whose <see cref="HybridDocumentParsingService"/> AC-4
/// reuses) cannot reference <c>Raffa.Quotes</c> either — only <c>Raffa.Api</c>, the
/// composition root, is allowed to see every module at once (backend/README.md's own "Dependency
/// direction" section). <c>internal</c>, not <c>public</c>: this is host-composition wiring, not a
/// domain module's own public API surface — enforced by
/// <c>Raffa.ArchitectureTests.DependencyDirectionTests.Host_must_not_contain_domain_types</c>,
/// the same treatment <c>Raffa.Worker.Queue.QueueConsumerHostedService</c> already gets for the
/// identical reason.
///
/// <b>AC-4</b> ("Scanned/image quote PDFs reuse the epic-02 hybrid OCR path (ADR-017); no 2-page
/// cap"): calls the exact same
/// <c>Raffa.Documents.Contracts.Application.Extraction.HybridDocumentParsingService</c> instance
/// task E02/F01/US02/T02 built for contracts — native text extraction when the file has
/// sufficient extractable text, the AI Gateway `ocr` role (Azure AI Document Intelligence) for
/// scanned/image/low-text quote PDFs, full document, no page cap. There is no quote-specific parse
/// code path to drift out of sync with the contract one.
///
/// <b>Synchronous, in-request, not a queue dispatch</b> — same deliberate interim choice as
/// <c>DocumentProcessingPipeline</c>'s own doc comment: nothing in this codebase dispatches a
/// queued job to a handler off a durable queue yet
/// (<c>Raffa.Worker.Queue.QueueConsumerHostedService</c>'s own doc comment), so running the
/// AI Gateway `extract` call inline, in the same `POST /api/quotes` request, is the smallest
/// honest way to make AC-1's "creates an extraction job" promise actually resolve to real line
/// items today.
///
/// <b>Never fails an already-durable upload</b>: any failure here (parse failure, gateway
/// failure, malformed payload) is recorded on the <see cref="Quote"/>/<see cref="QuoteExtractionJob"/>
/// rows and returned to the caller, but never unwinds the upload itself — the bytes are already
/// safely stored and the <see cref="Quote"/> row already exists by the time this runs (mirrors
/// <c>DocumentProcessingPipeline</c>'s identical posture).
///
/// <b>Task E05/F01/US01/T02 (quote-normalization)</b>: right after <paramref name="lineExtractionService"/>
/// adds a quote's <see cref="QuoteLine"/> rows to the change tracker, this pipeline also runs
/// <see cref="QuoteLineNormalizationService"/> over those same not-yet-saved rows — spec §11.1's
/// "Normalize unit economics" pipeline step, in the same unit of work as extraction, before the one
/// shared <c>SaveChangesAsync</c> call below. See that service's own doc comment for why "same unit
/// of work" (not a separate save) is load-bearing here.
/// <b>Task E05/F01/US02/T01 (sku-normalization)</b> added the call to
/// <see cref="SkuNormalizationService.NormalizeAsync"/> below, right after the freshly-extracted
/// lines are saved: every quote gets a <c>QuoteLine.MatchStatus</c> for real from its first upload
/// onward, not only once task E05/F01/US02/T02's own "recalculate" endpoint exists. It runs against
/// already-persisted rows (a second <see cref="QuotesDbContext.SaveChangesAsync"/> call below,
/// after the one that durably saves the raw extracted lines) rather than the in-memory ones
/// <see cref="QuoteLineExtractionService.ApplyExtractedLines"/> just added to the change tracker,
/// deliberately: an EF Core LINQ query re-reads from the database, so it would see none of those
/// still-unsaved rows yet — querying only after they are actually committed is what makes this
/// service's own "query the quote's current lines, re-runnable later unchanged" contract (see its
/// own doc comment) correct both here and from a later, independent recalculate call.
/// </summary>
internal sealed class QuoteExtractionPipeline(
    QuotesDbContext dbContext,
    IAiGateway aiGateway,
    HybridDocumentParsingService parsingService,
    QuoteLineExtractionService lineExtractionService,
    QuoteLineNormalizationService lineNormalizationService,
    SkuNormalizationService skuNormalizationService,
    ITenantContext tenantContext,
    IClock clock,
    IAuditWriter auditWriter,
    ILogger<QuoteExtractionPipeline>? logger = null)
{
    /// <summary>Caller-owned stage label for the AI Gateway's `extract` role (see
    /// <c>IAiGateway.ExtractAsync</c>'s own doc comment: "mirrors, but does not reference,
    /// ExtractionStage; the gateway must not depend on a domain module's enum").</summary>
    private const string StageName = "QuoteLineItems";

    /// <summary>Recorded actor for this pipeline's own audit entry — same "no human caller, label
    /// it as automation" convention as
    /// <c>StagedExtractionService.SystemActor</c>/<c>DocumentProcessingPipeline</c>'s own
    /// equivalents.</summary>
    private const string SystemActor = "system:quote-extraction";

    /// <summary>
    /// Runs one quote through parse, extract, normalize and SKU-match, and always leaves the
    /// <see cref="Quote"/> and its <see cref="QuoteExtractionJob"/> in a terminal state (task
    /// F6-T02, "0 job Running orfani"). A <see cref="Result"/> failure and a <em>throw</em>
    /// (corrupt file in the parser, an unreachable model, a database fault, a client that
    /// disconnected mid-run) end the same way: both rows go to <c>Failed</c> with a typed
    /// <c>ErrorDetail</c> (<see cref="QuoteExtractionFailure"/>), the terminal write ignores the
    /// request's cancellation token (the very reason it is needed may be that the token fired), and
    /// any line this run already persisted is removed so a <c>Failed</c> quote never carries a
    /// half-processed set of rows. The one exception is a throw <em>after</em> the final status
    /// save committed (the audit write): the work is durable and terminal, so it is rethrown
    /// unchanged rather than overwritten.
    /// </summary>
    public async Task<Result<QuoteProcessingSummary>> ProcessAsync(
        TenantId tenantId,
        EntityId quoteId,
        string fileName,
        string mimeType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var run = new RunState();
        try
        {
            return await RunAsync(run, tenantId, quoteId, fileName, mimeType, content, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (!run.StatusCommitted)
        {
            var (kind, detail) = Describe(exception, run.Stage, cancellationToken);
            if (kind == QuoteExtractionFailureKind.Cancelled)
            {
                logger?.LogWarning(
                    "Quote {QuoteId} extraction was cancelled before it finished; marking it Failed.", quoteId);
            }
            else
            {
                logger?.LogError(
                    exception, "Quote {QuoteId} extraction threw at stage {Stage}; marking it Failed.",
                    quoteId, run.Stage);
            }

            await FailAsync(run, tenantId, quoteId, kind, detail).ConfigureAwait(false);
            return Result<QuoteProcessingSummary>.Failure(detail);
        }
    }

    private async Task<Result<QuoteProcessingSummary>> RunAsync(
        RunState run,
        TenantId tenantId,
        EntityId quoteId,
        string fileName,
        string mimeType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        var quote = await dbContext.Quotes
            .SingleOrDefaultAsync(q => q.TenantId == tenantId && q.Id == quoteId, cancellationToken)
            .ConfigureAwait(false);

        if (quote is null)
        {
            return Result<QuoteProcessingSummary>.Failure($"Quote {quoteId} was not found for this tenant.");
        }

        // The row QuoteUploadService queued at upload — advanced to completion here, the same
        // "advance the queued row, never insert a second one" shape
        // DocumentProcessingPipeline.ClassifyAsync already uses for ExtractionJob.
        var job = await dbContext.QuoteExtractionJobs
            .Where(j => j.TenantId == tenantId
                && j.QuoteId == quoteId
                && j.Status == QuoteExtractionJobStatus.Queued)
            .OrderBy(j => j.QueuedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var startedAt = clock.UtcNow;
        if (job is not null)
        {
            job.Status = QuoteExtractionJobStatus.Running;
            job.StartedAt = startedAt;
        }

        run.Stage = QuoteExtractionFailureKind.ParseFailed;
        var parseResult = await parsingService
            .ParseAsync(fileName, mimeType, content, cancellationToken)
            .ConfigureAwait(false);

        if (parseResult.IsFailure)
        {
            return await FailAsync(
                run, tenantId, quoteId, QuoteExtractionFailureKind.ParseFailed, parseResult.Error)
                .ConfigureAwait(false);
        }

        var pages = parseResult.Value;
        if (pages.Count == 0)
        {
            // ADR-017: "over-budget jobs fail visibly... they are not silently truncated" —
            // generalized here to "no readable text at all", mirroring
            // StagedExtractionService.RunAsync's identical guard.
            return await FailAsync(
                run, tenantId, quoteId, QuoteExtractionFailureKind.NoReadableText,
                "Quote extraction requires at least one page of document text.")
                .ConfigureAwait(false);
        }

        var documentText = BuildPageMarkedText(pages);

        run.Stage = QuoteExtractionFailureKind.ExtractionFailed;
        var extractRequest = new AiExtractionRequest(StageName, documentText, QuoteLineJsonSchema.LineItems());
        var extractResult = await aiGateway.ExtractAsync(extractRequest, cancellationToken).ConfigureAwait(false);

        if (extractResult.IsFailure)
        {
            return await FailAsync(
                run, tenantId, quoteId, QuoteExtractionFailureKind.ExtractionFailed, extractResult.Error)
                .ConfigureAwait(false);
        }

        if (job is not null)
        {
            job.ModelId = extractResult.Value.Metadata.ModelId;
        }

        QuoteLineExtractionOutcome outcome;
        var completedAt = clock.UtcNow;
        run.Stage = QuoteExtractionFailureKind.PersistenceFailed;
        try
        {
            outcome = lineExtractionService.ApplyExtractedLines(
                tenantId, quoteId, extractResult.Value.PayloadJson, pages.Count, completedAt);
        }
        catch (JsonException ex)
        {
            // The gateway does not validate the model's output against the schema it was given
            // (IAiGateway.ExtractAsync's own doc comment) — mirrors
            // StagedExtractionService.ApplyStageResultAsync's identical guard against malformed
            // JSON from a real (non-fixture) model.
            return await FailAsync(
                run, tenantId, quoteId, QuoteExtractionFailureKind.MalformedPayload,
                $"Malformed extraction payload: {ex.Message}")
                .ConfigureAwait(false);
        }

        // Remember exactly which rows this run is about to persist, so a failure after the first
        // save removes those and only those (a quote that already held lines from an earlier
        // successful run must never lose them to a later failed one).
        foreach (var entry in dbContext.ChangeTracker.Entries<QuoteLine>())
        {
            if (entry.State == EntityState.Added)
            {
                run.PersistedLineIds.Add(entry.Entity.Id);
            }
        }

        // Task E05/F01/US01/T02 (quote-normalization): normalizes the very rows ApplyExtractedLines
        // just added to this same QuotesDbContext's change tracker (still unsaved) — spec §11.1
        // "Normalize unit economics", the pipeline step right after "Extract". Deliberately does not
        // factor into finalJobStatus below: an unresolved normalization (spec §11.3) is an expected,
        // common outcome for a term outside QuoteBillingCadence's own small recognized vocabulary,
        // not an extraction-quality problem — see QuoteLineNormalizationOutcome's own doc comment for
        // who (a future task) actually reads UnresolvedCount to act on it.
        var lineNormalizationOutcome = lineNormalizationService.NormalizeLines(tenantId, quoteId);
        // Persist the raw extracted lines before normalizing them: SkuNormalizationService queries
        // QuoteLines back from the database (see its own doc comment), so it must run after they
        // are actually committed, not while they are still pending Added entries.
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Task E05/F01/US02/T01 (sku-normalization, AC-1/AC-2's "show unmatched" half): every line
        // just saved above gets a NormalizedSku/NormalizedEdition/MatchStatus so spec §11.3's
        // guardrail ("no target without resolved normalization") has real data from this upload
        // onward — see QuoteExtractionPipeline's own doc comment for why this runs here, after the
        // first SaveChangesAsync, rather than against the in-memory lines directly.
        //
        // Task E05/F02/US01/T01 (market-assessment): this line used to redeclare the same
        // `normalizationOutcome` name the line-normalization call above already used (a `var`/`var`
        // collision — CS0128, does not compile) and the record/return-statement below carried a
        // matching stray duplicate line each (leftover from two sibling tasks — quote-normalization
        // and sku-normalization — each appending its own new field to this same method/record in
        // parallel branches without reconciling with the other's own addition). Renamed to its own
        // distinct name so both outcomes are readable below; no behavioural change intended for
        // either sibling task's own already-landed logic.
        var skuNormalizationOutcome = await skuNormalizationService
            .NormalizeAsync(tenantId, quoteId, cancellationToken)
            .ConfigureAwait(false);

        // Human-in-the-loop principle: nothing extracted, something skipped, or any line below the
        // confidence threshold all mean a person should look at this quote before it is trusted,
        // even though the AI Gateway call itself succeeded — mirrors
        // StagedExtractionService.ApplyStageResultAsync's identical decision rule. Deliberately does not
        // also factor in unmatched SKUs: that is a product-mapping gap for a person to resolve via
        // task E05/F01/US02/T02's manual mapping, not an extraction-confidence problem, so it does
        // not change this job's own Completed/NeedsReview outcome.
        var finalJobStatus = outcome.ExtractedCount == 0 || outcome.SkippedCount > 0 || outcome.AnyLowConfidence
            ? QuoteExtractionJobStatus.NeedsReview
            : QuoteExtractionJobStatus.Completed;

        if (job is not null)
        {
            job.Status = finalJobStatus;
            job.CompletedAt = completedAt;
        }

        quote.ProcessingStatus = MapStatus(finalJobStatus);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        run.StatusCommitted = true;

        await auditWriter.WriteAsync(
                new AuditEntry(
                    tenantId,
                    SystemActor,
                    $"quote.extraction.{quote.ProcessingStatus.ToString().ToLowerInvariant()}",
                    "quote",
                    quoteId.Value.ToString(),
                    completedAt),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<QuoteProcessingSummary>.Success(new QuoteProcessingSummary(
            quoteId,
            quote.ProcessingStatus,
            outcome.ExtractedCount,
            outcome.SkippedCount,
            pages.Count,
            lineNormalizationOutcome.NormalizedCount,
            lineNormalizationOutcome.UnresolvedCount,
            skuNormalizationOutcome.UnmatchedCount,
            outcome.InvalidCount));
    }

    /// <summary>
    /// The one terminal-failure path, for a <see cref="Result"/> failure and a throw alike: marks
    /// the quote and every still-open job of this quote <c>Failed</c>, removes the lines this run
    /// persisted, and records the typed error. It deliberately does <em>not</em> reuse the tracked
    /// entities of the failed run: after a throw the change tracker can hold half-applied or
    /// Added-but-unsaved rows (a failed <c>SaveChanges</c> leaves them tracked), and saving
    /// those again would just throw again. It clears the tracker, re-reads the rows and writes with
    /// <see cref="CancellationToken.None"/> — the request token may be exactly what fired. Best
    /// effort by construction: if even this write fails (the database is down) it is logged, not
    /// thrown, because the caller is already reporting the original failure.
    /// </summary>
    private async Task<Result<QuoteProcessingSummary>> FailAsync(
        RunState run, TenantId tenantId, EntityId quoteId, QuoteExtractionFailureKind kind, string error)
    {
        var errorDetail = QuoteExtractionFailure.Format(kind, error);
        var now = clock.UtcNow;

        try
        {
            dbContext.ChangeTracker.Clear();

            var quote = await dbContext.Quotes
                .SingleOrDefaultAsync(q => q.TenantId == tenantId && q.Id == quoteId, CancellationToken.None)
                .ConfigureAwait(false);

            // Never downgrade a quote that already reached a successful terminal state.
            if (quote is not null
                && quote.ProcessingStatus is not (QuoteProcessingStatus.Completed or QuoteProcessingStatus.NeedsReview))
            {
                var openJobs = await dbContext.QuoteExtractionJobs
                    .Where(j => j.TenantId == tenantId
                        && j.QuoteId == quoteId
                        && (j.Status == QuoteExtractionJobStatus.Queued
                            || j.Status == QuoteExtractionJobStatus.Running))
                    .ToListAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                foreach (var openJob in openJobs)
                {
                    openJob.Status = QuoteExtractionJobStatus.Failed;
                    openJob.StartedAt ??= now;
                    openJob.CompletedAt = now;
                    openJob.ErrorDetail = errorDetail;
                }

                if (run.PersistedLineIds.Count > 0)
                {
                    var persisted = run.PersistedLineIds.ToHashSet();
                    var orphans = (await dbContext.QuoteLines
                            .Where(l => l.TenantId == tenantId && l.QuoteId == quoteId)
                            .ToListAsync(CancellationToken.None)
                            .ConfigureAwait(false))
                        .Where(l => persisted.Contains(l.Id))
                        .ToList();
                    dbContext.QuoteLines.RemoveRange(orphans);
                }

                quote.ProcessingStatus = QuoteProcessingStatus.Failed;
                await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            logger?.LogError(
                exception, "Could not record the failure of quote {QuoteId} extraction ({Kind}).", quoteId, kind);
        }

        try
        {
            await auditWriter.WriteAsync(
                    new AuditEntry(
                        tenantId,
                        SystemActor,
                        "quote.extraction.failed",
                        "quote",
                        quoteId.Value.ToString(),
                        now),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "Could not audit the failure of quote {QuoteId} extraction.", quoteId);
        }

        return Result<QuoteProcessingSummary>.Failure(error);
    }

    /// <summary>Maps a thrown exception to its typed kind and a short, content-free detail: the
    /// exception type and message, never the document text.</summary>
    private static (QuoteExtractionFailureKind Kind, string Detail) Describe(
        Exception exception, QuoteExtractionFailureKind stage, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return (QuoteExtractionFailureKind.Cancelled, "The request was cancelled before extraction finished.");
        }

        return (stage, $"{exception.GetType().Name}: {exception.Message}");
    }

    /// <summary>Mutable bookkeeping of one <see cref="ProcessAsync"/> run, read by its catch
    /// block: the stage that was executing, the ids of the lines this run persisted, and whether
    /// the final status save already committed.</summary>
    private sealed class RunState
    {
        public QuoteExtractionFailureKind Stage { get; set; } = QuoteExtractionFailureKind.Unexpected;

        public List<EntityId> PersistedLineIds { get; } = [];

        public bool StatusCommitted { get; set; }
    }

    private static QuoteProcessingStatus MapStatus(QuoteExtractionJobStatus status) => status switch
    {
        QuoteExtractionJobStatus.Completed => QuoteProcessingStatus.Completed,
        QuoteExtractionJobStatus.NeedsReview => QuoteProcessingStatus.NeedsReview,
        QuoteExtractionJobStatus.Failed => QuoteProcessingStatus.Failed,
        _ => QuoteProcessingStatus.Processing,
    };

    /// <summary>Same <c>[[PAGE n]]</c> marker convention as
    /// <c>StagedExtractionService.BuildPageMarkedText</c> (that method is <c>private</c> to its
    /// own module, so this is a small, deliberate, documented duplicate — not a shared helper —
    /// per ADR-002's dependency-direction rule) so a structured-output model can report which page
    /// a line came from by reading these markers.</summary>
    private static string BuildPageMarkedText(IReadOnlyList<DocumentPageText> pages)
    {
        var builder = new StringBuilder();

        foreach (var page in pages)
        {
            builder.Append("[[PAGE ").Append(page.PageNumber).Append("]]\n");
            builder.Append(page.Text);
            builder.Append("\n\n");
        }

        return builder.ToString();
    }
}

/// <summary>Outcome of one <see cref="QuoteExtractionPipeline.ProcessAsync"/> run — the response
/// shape `POST /api/quotes` (see <see cref="QuotesEndpointExtensions"/>) folds into its own JSON
/// reply.</summary>
/// <param name="NormalizedLineItemCount">Task E05/F01/US01/T02 (quote-normalization): how many of
/// this run's <see cref="LineItemCount"/> lines resolved to a real
/// <c>Raffa.Quotes.Domain.QuoteLine.NormalizedAnnualUnitPrice</c> — see
/// <c>Raffa.Quotes.Application.Normalization.QuoteLineNormalizationOutcome</c>'s own doc
/// comment.</param>
/// <param name="UnresolvedNormalizationCount">The complement of <paramref name="NormalizedLineItemCount"/>
/// within <see cref="LineItemCount"/> — spec §11.3's "line-item normalization is unresolved" outcome,
/// made visible over HTTP as well as in the database.</param>
/// <param name="UnmatchedSkuCount">Added by task E05/F01/US02/T01 (sku-normalization, AC-2's "show
/// unmatched SKUs" half).</param>
/// <param name="InvalidCount">Task F6-T04: lines discarded for an out-of-range value; already
/// included in <see cref="SkippedCount"/>.</param>
internal sealed record QuoteProcessingSummary(
    EntityId QuoteId,
    QuoteProcessingStatus ProcessingStatus,
    int LineItemCount,
    int SkippedCount,
    int PageCount,
    int NormalizedLineItemCount,
    int UnresolvedNormalizationCount,
    int UnmatchedSkuCount,
    int InvalidCount = 0);
