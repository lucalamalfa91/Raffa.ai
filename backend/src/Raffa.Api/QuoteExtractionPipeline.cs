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
    IAuditWriter auditWriter)
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

    public async Task<Result<QuoteProcessingSummary>> ProcessAsync(
        TenantId tenantId,
        EntityId quoteId,
        string fileName,
        string mimeType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

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

        var parseResult = await parsingService
            .ParseAsync(fileName, mimeType, content, cancellationToken)
            .ConfigureAwait(false);

        if (parseResult.IsFailure)
        {
            return await FailAsync(quote, job, parseResult.Error, cancellationToken).ConfigureAwait(false);
        }

        var pages = parseResult.Value;
        if (pages.Count == 0)
        {
            // ADR-017: "over-budget jobs fail visibly... they are not silently truncated" —
            // generalized here to "no readable text at all", mirroring
            // StagedExtractionService.RunAsync's identical guard.
            return await FailAsync(
                quote, job, "Quote extraction requires at least one page of document text.", cancellationToken)
                .ConfigureAwait(false);
        }

        var documentText = BuildPageMarkedText(pages);

        var extractRequest = new AiExtractionRequest(StageName, documentText, QuoteLineJsonSchema.LineItems());
        var extractResult = await aiGateway.ExtractAsync(extractRequest, cancellationToken).ConfigureAwait(false);

        if (extractResult.IsFailure)
        {
            return await FailAsync(quote, job, extractResult.Error, cancellationToken).ConfigureAwait(false);
        }

        if (job is not null)
        {
            job.ModelId = extractResult.Value.Metadata.ModelId;
        }

        QuoteLineExtractionOutcome outcome;
        var completedAt = clock.UtcNow;
        try
        {
            outcome = lineExtractionService.ApplyExtractedLines(
                tenantId, quoteId, extractResult.Value.PayloadJson, pages.Count, completedAt);
        }
        catch (JsonException ex)
        {
            // The gateway does not validate the model's output against the schema it was given
            // (IAiGateway.ExtractAsync's own doc comment) — mirrors
            // StagedExtractionService.RunStageAsync's identical guard against malformed JSON from
            // a real (non-fixture) model.
            return await FailAsync(
                quote, job, $"Malformed extraction payload: {ex.Message}", cancellationToken)
                .ConfigureAwait(false);
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
        // StagedExtractionService.RunStageAsync's identical decision rule. Deliberately does not
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
            skuNormalizationOutcome.UnmatchedCount));
    }

    /// <summary>Shared terminal-failure path: marks both rows Failed, persists, and returns the
    /// honest error — never leaves <paramref name="quote"/> looking like extraction is still in
    /// flight (mirrors <c>DocumentProcessingPipeline.ProcessAsync</c>'s identical parse-failure
    /// handling).</summary>
    private async Task<Result<QuoteProcessingSummary>> FailAsync(
        Quote quote, QuoteExtractionJob? job, string error, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        quote.ProcessingStatus = QuoteProcessingStatus.Failed;

        if (job is not null)
        {
            job.Status = QuoteExtractionJobStatus.Failed;
            job.ErrorDetail = Truncate(error);
            job.CompletedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<QuoteProcessingSummary>.Failure(error);
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

    private static string Truncate(string value, int maxLength = 1000) =>
        value.Length <= maxLength ? value : value[..maxLength];
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
internal sealed record QuoteProcessingSummary(
    EntityId QuoteId,
    QuoteProcessingStatus ProcessingStatus,
    int LineItemCount,
    int SkippedCount,
    int PageCount,
    int NormalizedLineItemCount,
    int UnresolvedNormalizationCount,
    int UnmatchedSkuCount);
