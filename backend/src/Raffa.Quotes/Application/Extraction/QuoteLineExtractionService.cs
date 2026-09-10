using System.Text.Json;
using System.Text.Json.Serialization;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;

namespace Raffa.Quotes.Application.Extraction;

/// <summary>
/// Implements the persistence half of task E05/F01/US01/T01 (quote-extraction; parent story
/// us-01-quote-line-extraction AC-2 "Line items extract quantity/SKU/edition/price/discount/term
/// (evidence + confidence)" and AC-3 "Separate arithmetic from LLM language (App C #6)").
///
/// Deliberately takes an already-produced <c>AiExtractionResult.PayloadJson</c> string rather than
/// depending on <c>Raffa.AiGateway.IAiGateway</c> directly: ADR-002's dependency-direction rule
/// (<c>Raffa.ArchitectureTests.DependencyDirectionTests</c>) only allows this module to
/// reference <c>Raffa.SharedKernel</c> and <c>Raffa.Benchmark</c> — never
/// <c>Raffa.AiGateway</c> or <c>Raffa.Documents.Contracts</c> (whose own
/// <c>HybridDocumentParsingService</c> this story's AC-4 reuses). The actual AI Gateway call and
/// the hybrid-OCR reuse both happen in <c>Raffa.Api.QuoteExtractionPipeline</c> — "the one
/// project allowed to reference every module" (backend/README.md's own "Dependency direction"
/// section) — which calls this service afterward with the raw JSON payload, exactly mirroring how
/// <c>Raffa.Documents.Contracts.Application.Extraction.StagedExtractionService.ApplyLineItems</c>
/// only ever sees a payload string too, never the gateway itself.
///
/// <b>AC-3</b>: this type is the one place quote-line arithmetic happens. The AI Gateway `extract`
/// role is never asked to report a line total (see <see cref="QuoteLineJsonSchema"/>'s own doc
/// comment — there is no schema property for one); <see cref="ComputePricing"/> derives
/// <c>QuoteLine.UnitPrice</c> (when only <c>listPrice</c>/<c>discountPercent</c> were extracted)
/// and <c>QuoteLine.ExtendedPrice</c> in plain, deterministic C# decimal arithmetic — Appendix C
/// rule 6 ("prefer deterministic arithmetic... to LLM reasoning") applied concretely, not just
/// asserted in a comment.
///
/// Only adds rows to the change tracker; it does not call <c>SaveChangesAsync</c> itself — the
/// caller (<c>QuoteExtractionPipeline</c>) persists this alongside its own
/// <see cref="QuoteExtractionJob"/> status update as one unit of work, mirroring
/// <c>StagedExtractionService.ApplyLineItems</c>'s identical division of responsibility.
/// </summary>
public sealed class QuoteLineExtractionService(QuotesDbContext dbContext)
{
    /// <summary>Below this, a fact is treated as needing human review rather than trusted
    /// outright — same threshold and reasoning as
    /// <c>Raffa.Documents.Contracts.Application.Extraction.StagedExtractionService
    /// .LowConfidenceThreshold</c> (documented separately, not shared: the two run against
    /// independent module boundaries — see this module's own <c>TenantScopedEntity</c> doc
    /// comment for why duplication, not a shared constant, is this codebase's established
    /// pattern here).</summary>
    private const double LowConfidenceThreshold = 0.6;

    private static readonly JsonSerializerOptions PayloadSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>
    /// Parses <paramref name="payloadJson"/> (the AI Gateway `extract` role's raw structured
    /// output, produced against <see cref="QuoteLineJsonSchema.LineItems"/>) and adds one
    /// <see cref="QuoteLine"/> row per recognized item to the change tracker. A line whose
    /// <c>description</c> is blank is skipped, not persisted (same "one row = one fact, an
    /// unnamed line is not a fact" rule
    /// <c>StagedExtractionService.ApplyLineItems</c> already applies to
    /// <c>ContractLineItem</c>).
    /// </summary>
    public QuoteLineExtractionOutcome ApplyExtractedLines(
        TenantId tenantId,
        EntityId quoteId,
        string payloadJson,
        int pageCount,
        DateTimeOffset now)
    {
        var payload = JsonSerializer.Deserialize<ExtractedQuoteLinesPayload>(payloadJson, PayloadSerializerOptions);
        var items = payload?.Items ?? [];

        var extracted = 0;
        var skipped = 0;
        var anyLowConfidence = false;

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Description))
            {
                skipped++;
                continue;
            }

            if (item.Confidence is null || item.Confidence < LowConfidenceThreshold)
            {
                anyLowConfidence = true;
            }

            var (unitPrice, extendedPrice) = ComputePricing(
                item.Quantity, item.UnitPrice, item.ListPrice, item.DiscountPercent);

            dbContext.QuoteLines.Add(new QuoteLine
            {
                TenantId = tenantId,
                QuoteId = quoteId,
                Sku = item.Sku,
                Edition = item.Edition,
                Description = item.Description,
                Quantity = item.Quantity,
                Unit = item.Unit,
                UnitPrice = unitPrice,
                ListPrice = item.ListPrice,
                DiscountPercent = item.DiscountPercent,
                Term = item.Term,
                ExtendedPrice = extendedPrice,
                SourceSpan = item.SourceSpan,
                SourcePage = ClampPage(item.SourcePage, pageCount),
                Confidence = item.Confidence,
                CreatedAt = now,
            });

            extracted++;
        }

        return new QuoteLineExtractionOutcome(extracted, skipped, anyLowConfidence);
    }

    /// <summary>
    /// AC-3's deterministic arithmetic, isolated as its own pure function so it is directly
    /// unit-testable independent of JSON parsing or the database: never asks the model for a
    /// total (there is no such property to ask for — see <see cref="QuoteLineJsonSchema"/>).
    /// <paramref name="reportedUnitPrice"/> wins when the model reported one directly; otherwise,
    /// when both <paramref name="listPrice"/> and <paramref name="discountPercent"/> are present,
    /// the unit price is derived as <c>listPrice * (1 - discountPercent / 100)</c>. The extended
    /// price is <paramref name="quantity"/> × the resulting unit price, or <see langword="null"/>
    /// when either factor is unknown — never a guessed default (Appendix C rule 10: "return
    /// uncertainty instead of fabricated precision").
    /// </summary>
    internal static (decimal? UnitPrice, decimal? ExtendedPrice) ComputePricing(
        decimal? quantity, decimal? reportedUnitPrice, decimal? listPrice, decimal? discountPercent)
    {
        var unitPrice = reportedUnitPrice;

        if (unitPrice is null && listPrice is not null && discountPercent is not null)
        {
            unitPrice = listPrice.Value * (1m - (discountPercent.Value / 100m));
        }

        var extendedPrice = quantity is not null && unitPrice is not null
            ? quantity.Value * unitPrice.Value
            : (decimal?)null;

        return (unitPrice, extendedPrice);
    }

    /// <summary>A page number the model reports outside the document's actual page range is
    /// treated as absent rather than stored as a plausible-looking but wrong citation — same rule
    /// as <c>StagedExtractionService.ClampPage</c>.</summary>
    private static int? ClampPage(int? page, int pageCount) =>
        page is >= 1 && page <= pageCount ? page : null;
}

/// <summary>Counts <c>Raffa.Api.QuoteExtractionPipeline</c> uses to decide the resulting
/// <see cref="QuoteExtractionJobStatus"/>/<see cref="QuoteProcessingStatus"/> — mirrors
/// <c>StagedExtractionService</c>'s own inline stage-result tuple shape, named here since this
/// service's result crosses an assembly boundary to its caller.</summary>
public sealed record QuoteLineExtractionOutcome(int ExtractedCount, int SkippedCount, bool AnyLowConfidence);
