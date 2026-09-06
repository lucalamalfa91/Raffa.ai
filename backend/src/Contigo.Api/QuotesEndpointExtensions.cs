using Contigo.Quotes.Application;
using Contigo.Quotes.Application.Assessment;
using Contigo.Quotes.Application.Normalization;
using Contigo.SharedKernel;

namespace Contigo.Api;

/// <summary>
/// Maps `POST /api/quotes` (product spec Appendix A API table: "Upload/create quote"; parent
/// story us-01-quote-line-extraction AC-1, task E05/F01/US01/T01, quote-extraction),
/// `GET /api/quotes/{id}/assessment` (Appendix A "Quote assessment"; parent story
/// us-01-market-assessment AC-3, task E05/F02/US01/T01, market-assessment; AC-2's "recommended
/// target range + potential saving" half, task E05/F02/US01/T02, target-saving), and
/// `POST /api/quotes/{id}/assessment/recalculate` (Appendix A "Re-run after product mapping
/// correction"; parent story us-02-sku-normalization AC-2's "...and allow manual product mapping"
/// half, AC-3, task E05/F01/US02/T02, sku-recalculate). Thin composition per
/// ADR-002 — <see cref="QuoteUploadService"/> owns the upload/storage/audit decisions,
/// <see cref="QuoteExtractionPipeline"/> owns the hybrid-parse/AI-extraction orchestration (see
/// that type's own doc comment for why it, not a domain module, is the AI Gateway call site),
/// <see cref="MarketAssessmentService"/> owns the benchmark-matching/classification/target-saving
/// decisions, and <see cref="SkuMappingService"/> owns the manual-mapping/recalculate decisions; this
/// file only translates HTTP &lt;-&gt; those calls, the same shape
/// <see cref="ContractsEndpointExtensions"/>/<see cref="WorkspaceEndpointExtensions"/> already use.
///
/// Same interim `X-Tenant-Id` header placeholder and multipart `file`-field contract as
/// `POST /api/documents` (see <c>Program.cs</c>'s own comment on that endpoint for why this gap is
/// not promoted to reports/open-questions.md by this task either — ADR-010 is not in this task's
/// "Architecture decisions in force" list, so there is still no validated caller principal to take
/// the tenant from).
/// </summary>
public static class QuotesEndpointExtensions
{
    public static IEndpointRouteBuilder MapQuotesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/quotes", UploadQuoteAsync);
        // Task E05/F02/US01/T01 (market-assessment) AC-3.
        endpoints.MapGet("/api/quotes/{id}/assessment", GetAssessmentAsync);
        // Task E05/F01/US02/T02 (sku-recalculate) AC-2/AC-3.
        endpoints.MapPost("/api/quotes/{id}/assessment/recalculate", RecalculateAssessmentAsync);
        return endpoints;
    }

    /// <summary>
    /// AC-1 ("`POST /api/quotes` uploads a quote and creates an extraction job"): stores the
    /// uploaded bytes + creates the queued <c>QuoteExtractionJob</c> (<see cref="QuoteUploadService"/>),
    /// then — same "read the bytes once, reuse them for the pipeline" shape `POST /api/documents`
    /// already uses, since an <c>IFormFile</c>'s own stream is not guaranteed re-readable after the
    /// first copy — runs <see cref="QuoteExtractionPipeline"/> synchronously before responding
    /// (AC-2 "line items extract...", AC-4 "reuse the epic-02 hybrid OCR path"). A pipeline
    /// failure is reported honestly in the response (`processingStatus`/`lineItemCount` fall back
    /// to the just-uploaded, pre-processing values) but never turns an already-successful upload
    /// into an HTTP error — the bytes are safely stored and the <c>Quote</c> row already exists
    /// either way (mirrors `POST /api/documents`'s identical posture).
    /// </summary>
    private static async Task<IResult> UploadQuoteAsync(
        HttpRequest request,
        QuoteUploadService uploadService,
        QuoteExtractionPipeline extractionPipeline,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeaderValues)
            || !Guid.TryParse(tenantHeaderValues.ToString(), out var tenantGuid))
        {
            return Results.BadRequest("A valid 'X-Tenant-Id' header (a GUID) is required.");
        }

        if (!request.HasFormContentType)
        {
            return Results.BadRequest("Expected multipart/form-data with a 'file' field.");
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files["file"];
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest("A non-empty 'file' form field is required.");
        }

        // Task E05/F02/US01/T01 (market-assessment): optional form fields — see Quote's own doc
        // comment for why these are explicit caller input rather than inferred from the document.
        // All four are optional; a quote uploaded without them simply cannot be matched against the
        // Benchmark Service yet (MarketAssessmentQueryBuilder reports that honestly, per line).
        var supplier = NullIfBlank(form["supplier"]);
        var currency = NullIfBlank(form["currency"]);
        var geography = NullIfBlank(form["geography"]);
        DateOnly? purchaseDate = null;
        var purchaseDateText = NullIfBlank(form["purchaseDate"]);
        if (purchaseDateText is not null)
        {
            if (!DateOnly.TryParse(purchaseDateText, System.Globalization.CultureInfo.InvariantCulture, out var parsedPurchaseDate))
            {
                return Results.BadRequest("The 'purchaseDate' form field, when present, must be a valid date (e.g. '2026-09-05').");
            }

            purchaseDate = parsedPurchaseDate;
        }

        byte[] fileBytes;
        await using (var uploadStream = file.OpenReadStream())
        await using (var buffer = new MemoryStream())
        {
            await uploadStream.CopyToAsync(buffer, cancellationToken);
            fileBytes = buffer.ToArray();
        }

        var tenantId = new TenantId(tenantGuid);

        using var storageContent = new MemoryStream(fileBytes);
        var result = await uploadService.UploadAsync(
            tenantId,
            file.FileName,
            file.ContentType,
            storageContent,
            cancellationToken,
            supplier: supplier,
            currency: currency,
            geography: geography,
            purchaseDate: purchaseDate);

        if (result.IsFailure)
        {
            return Results.BadRequest(result.Error);
        }

        var uploaded = result.Value;

        var processingResult = await extractionPipeline.ProcessAsync(
            tenantId, uploaded.QuoteId, uploaded.FileName, uploaded.MimeType, fileBytes, cancellationToken);

        var processingStatus = processingResult.IsSuccess
            ? processingResult.Value.ProcessingStatus
            : uploaded.ProcessingStatus;
        var lineItemCount = processingResult.IsSuccess ? processingResult.Value.LineItemCount : 0;
        // Task E05/F01/US02/T01 (sku-normalization, AC-2 "Show unmatched SKUs..."): 0 on a pipeline
        // failure, same honest "nothing ran yet" fallback lineItemCount already uses above.
        var unmatchedSkuCount = processingResult.IsSuccess ? processingResult.Value.UnmatchedSkuCount : 0;

        // Task E05/F01/US01/T02 (quote-normalization): 0/0 on a pipeline failure, same honest
        // fallback as lineItemCount above — normalization never ran if extraction itself did not.
        var normalizedLineItemCount = processingResult.IsSuccess ? processingResult.Value.NormalizedLineItemCount : 0;
        var unresolvedNormalizationCount =
            processingResult.IsSuccess ? processingResult.Value.UnresolvedNormalizationCount : 0;

        return Results.Created($"/api/quotes/{uploaded.QuoteId}", new
        {
            id = uploaded.QuoteId.Value,
            fileName = uploaded.FileName,
            mimeType = uploaded.MimeType,
            processingStatus = processingStatus.ToString(),
            lineItemCount,
            normalizedLineItemCount,
            unresolvedNormalizationCount,
            unmatchedSkuCount,
            // Task E05/F02/US01/T01 (market-assessment): echoes what was actually recorded
            // (including a null, when the caller did not supply one) so a caller can see
            // immediately whether GET .../assessment will be able to match this quote's lines yet.
            supplier = uploaded.Supplier,
            currency = uploaded.Currency,
            geography = uploaded.Geography,
            purchaseDate = uploaded.PurchaseDate,
            createdAt = uploaded.CreatedAt,
        });
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// AC-3 ("<c>GET /api/quotes/{id}/assessment</c> returns the assessment with
    /// confidence/provenance", task E05/F02/US01/T01, market-assessment) plus AC-2's "recommended
    /// target range + potential saving" half (task E05/F02/US01/T02, target-saving —
    /// <c>targetSaving</c> below): thin HTTP translation over
    /// <see cref="MarketAssessmentService.AssessAsync"/> — that service owns every matching/
    /// classification/target-saving decision (per-line status, market position,
    /// confidence/provenance, recommended target/saving); this handler only shapes the wire
    /// response. Same interim <c>X-Tenant-Id</c> header placeholder as
    /// <see cref="UploadQuoteAsync"/> above (ADR-010 is not in this task's "Architecture decisions
    /// in force" list either).
    /// </summary>
    private static async Task<IResult> GetAssessmentAsync(
        string id,
        HttpRequest request,
        MarketAssessmentService assessmentService,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeaderValues)
            || !Guid.TryParse(tenantHeaderValues.ToString(), out var tenantGuid))
        {
            return Results.BadRequest("A valid 'X-Tenant-Id' header (a GUID) is required.");
        }

        if (!Guid.TryParse(id, out var quoteGuid))
        {
            return Results.BadRequest("The quote id in the route must be a GUID.");
        }

        var result = await assessmentService.AssessAsync(
            new TenantId(tenantGuid), new EntityId(quoteGuid), cancellationToken);

        if (result.IsFailure)
        {
            // The only documented failure is "quote not found for this tenant" (see
            // MarketAssessmentService.AssessAsync's own doc comment) — never a 400/500 for an
            // otherwise-valid, merely-absent id, the same "tenant-scoped lookup miss is a 404, not
            // an error" convention DocumentQueryService/PortfolioEndpointExtensions already use.
            return Results.NotFound(result.Error);
        }

        return Results.Ok(BuildAssessmentResponse(result.Value));
    }

    /// <summary>
    /// Wire-shapes a <see cref="QuoteMarketAssessment"/> — extracted from <see cref="GetAssessmentAsync"/>
    /// (task E05/F01/US02/T02, sku-recalculate) so <see cref="RecalculateAssessmentAsync"/> below
    /// returns the exact same per-line assessment shape rather than a second, drifting copy of this
    /// projection — both endpoints describe "the current assessment for this quote", just reached
    /// differently (a plain read versus a read after a correction+recalculate).
    /// </summary>
    private static object BuildAssessmentResponse(QuoteMarketAssessment assessment) => new
    {
        quoteId = assessment.QuoteId.Value,
        lines = assessment.Lines.Select(line => new
        {
            quoteLineId = line.QuoteLineId.Value,
            status = line.Status.ToString(),
            position = line.Position?.ToString(),
            unitPrice = line.UnitPrice,
            // Task E05/F04/US01/T01 (r4-integration) fix: LineMarketAssessment.Quantity has
            // existed since task E05/F02/US01/T02 (target-saving) specifically so a caller never
            // has to re-fetch the line (see that record's own doc comment) — and the HTTP surface
            // table in backend/README.md has documented `quantity` as part of this response's own
            // shape since that same task — but this handler never actually serialized it. Adding
            // it now aligns the wire response with its own already-published contract.
            quantity = line.Quantity,
            benchmark = line.Benchmark is null
                ? null
                : new
                {
                    hasSufficientData = line.Benchmark.HasSufficientData,
                    distribution = line.Benchmark.Distribution is null
                        ? null
                        : new
                        {
                            p25 = line.Benchmark.Distribution.P25,
                            p50 = line.Benchmark.Distribution.P50,
                            p75 = line.Benchmark.Distribution.P75,
                        },
                    metric = line.Benchmark.Metric,
                    currency = line.Benchmark.Currency,
                },
            confidence = line.Provenance is null
                ? null
                : new
                {
                    level = line.Provenance.ConfidenceLevel.ToString(),
                    score = line.Provenance.ConfidenceScore,
                    source = line.Provenance.Source,
                    sampleSize = line.Provenance.SampleSize,
                    comparisonDimensions = line.Provenance.ComparisonDimensions.Select(d => d.ToString()),
                    updatedAt = line.Provenance.UpdatedAt,
                    summary = line.Provenance.Summary,
                },
            // Task E05/F02/US01/T02 (target-saving), AC-2's "recommended target range +
            // potential saving" half: null exactly when TargetSaving itself is (no benchmark
            // call was made); still populated — with every numeric field null plus a named
            // reason — when a call was made but returned no usable distribution (spec §11.3's
            // benchmark-trust rule, see LineTargetSaving's own doc comment).
            targetSaving = line.TargetSaving is null
                ? null
                : new
                {
                    recommendedTargetLow = line.TargetSaving.RecommendedTargetLow,
                    recommendedTargetHigh = line.TargetSaving.RecommendedTargetHigh,
                    savingsRangeLow = line.TargetSaving.SavingsRangeLow,
                    savingsRangeHigh = line.TargetSaving.SavingsRangeHigh,
                    totalSavingsRangeLow = line.TargetSaving.TotalSavingsRangeLow,
                    totalSavingsRangeHigh = line.TargetSaving.TotalSavingsRangeHigh,
                    explanation = line.TargetSaving.Explanation,
                },
            explanation = line.Explanation,
        }),
    };

    /// <summary>
    /// `POST /api/quotes/{id}/assessment/recalculate` (task E05/F01/US02/T02, sku-recalculate;
    /// product spec Appendix A "Re-run after product mapping correction"; parent story
    /// us-02-sku-normalization AC-2's "...and allow manual product mapping" half, AC-3 "Re-run
    /// assessment after mapping correction"): thin HTTP translation over
    /// <see cref="SkuMappingService.RecalculateAsync"/> — that service owns every
    /// validation/upsert/re-normalize/re-assess decision; this handler only shapes the wire
    /// request/response, the same division <see cref="GetAssessmentAsync"/>/<see cref="UploadQuoteAsync"/>
    /// already use. Same interim <c>X-Tenant-Id</c> header placeholder as every other endpoint in
    /// this file (ADR-010 is not in this task's "Architecture decisions in force" list either).
    ///
    /// <paramref name="request"/>'s <c>mappings</c> may be omitted/empty — a caller can POST
    /// <c>{}</c> to re-read the current unmatched-line list and a fresh assessment with no new
    /// correction at all (see <see cref="QuoteRecalculateRequest"/>'s own doc comment). 404 when
    /// <see cref="SkuMappingService.QuoteNotFoundError"/> comes back, 400 for every other validation
    /// failure (a blank <c>sku</c>/<c>canonicalSku</c> on a supplied correction) — same
    /// <c>Result&lt;T&gt;.Error</c>-sentinel-to-404 convention <see cref="NegotiationsEndpointExtensions
    /// .CaptureOutcomeAsync"/>'s own handler already uses for the identical shape of failure.
    /// </summary>
    private static async Task<IResult> RecalculateAssessmentAsync(
        string id,
        QuoteRecalculateRequest request,
        HttpRequest httpRequest,
        SkuMappingService mappingService,
        CancellationToken cancellationToken)
    {
        if (!httpRequest.Headers.TryGetValue("X-Tenant-Id", out var tenantHeaderValues)
            || !Guid.TryParse(tenantHeaderValues.ToString(), out var tenantGuid))
        {
            return Results.BadRequest("A valid 'X-Tenant-Id' header (a GUID) is required.");
        }

        if (!Guid.TryParse(id, out var quoteGuid))
        {
            return Results.BadRequest("The quote id in the route must be a GUID.");
        }

        var result = await mappingService.RecalculateAsync(
                new TenantId(tenantGuid), new EntityId(quoteGuid), request.Mappings, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return string.Equals(result.Error, SkuMappingService.QuoteNotFoundError, StringComparison.Ordinal)
                ? Results.NotFound(result.Error)
                : Results.BadRequest(result.Error);
        }

        var recalculation = result.Value;

        return Results.Ok(new
        {
            quoteId = recalculation.QuoteId.Value,
            mappingsAppliedCount = recalculation.MappingsAppliedCount,
            normalization = new
            {
                lineCount = recalculation.NormalizationOutcome.LineCount,
                matchedCount = recalculation.NormalizationOutcome.MatchedCount,
                unmatchedCount = recalculation.NormalizationOutcome.UnmatchedCount,
                notApplicableCount = recalculation.NormalizationOutcome.NotApplicableCount,
            },
            // AC-2 "Show unmatched SKUs" half — every line still unresolved after this call's own
            // (possibly zero) corrections were applied.
            unmatchedLines = recalculation.UnmatchedLines.Select(line => new
            {
                quoteLineId = line.QuoteLineId.Value,
                sku = line.Sku,
                normalizedSku = line.NormalizedSku,
                edition = line.Edition,
                description = line.Description,
            }),
            // AC-3 "Re-run assessment after mapping correction" half — same shape
            // GET /api/quotes/{id}/assessment returns, freshly recomputed.
            assessment = BuildAssessmentResponse(recalculation.Assessment),
        });
    }
}
