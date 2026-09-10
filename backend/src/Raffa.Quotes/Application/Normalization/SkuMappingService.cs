using Raffa.Quotes.Application.Assessment;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Quotes.Application.Normalization;

/// <summary>
/// Implements task E05/F01/US02/T02 (sku-recalculate; parent story us-02-sku-normalization AC-2's
/// "...and allow manual product mapping" half, AC-3 "Re-run assessment after mapping correction").
/// The intended first writer of <see cref="SkuProductMapping"/> — see that type's own doc comment —
/// standing in for "a person resolving an unmatched line" (this story's own Story text). Backs
/// `POST /api/quotes/{id}/assessment/recalculate` (product spec Appendix A: "Re-run after product
/// mapping correction").
///
/// Composes three already-accepted services rather than re-deriving their logic (the same "reuse, do
/// not re-implement" posture <c>Raffa.Quotes.Application.Strategy.NegotiationStrategyService</c>
/// already takes for <see cref="MarketAssessmentService"/>):
/// <list type="bullet">
/// <item><see cref="SkuNormalizer"/> — the same pure text rule the mapping key and every
/// <see cref="QuoteLine.NormalizedSku"/> already share, so a correction submitted with different
/// casing/whitespace than the original extraction still resolves the same row.</item>
/// <item><see cref="SkuNormalizationService.NormalizeAsync"/> — re-applies every mapping (the one
/// just written here, plus every one already on file for this tenant) across the quote's lines,
/// unchanged — the exact "re-runnable later, unchanged" contract that type's own doc comment names
/// this task as the caller of.</item>
/// <item><see cref="MarketAssessmentService.AssessAsync"/> — AC-3's own "re-run assessment" half,
/// called fresh after normalization so this response's own assessment reflects the just-corrected
/// match state, never a stale one (mirrors that service's own "computed fresh, not stored"
/// posture).</item>
/// </list>
///
/// <para>
/// <b>AC-2's "Show unmatched SKUs" half</b>: deliberately does not extend
/// <see cref="Assessment.LineMarketAssessment"/> with <c>Sku</c>/<c>NormalizedSku</c>/<c>MatchStatus</c>
/// fields, even though that would be a one-line addition there. That record is task E05/F02/US01/T01's
/// own already-accepted file, and <c>NegotiationStrategyService</c>'s own doc comment already declined
/// to extend it for the identical "do not touch unrelated wave artifacts" reason (a second, small query
/// instead). This type follows that same precedent — <see cref="GetUnmatchedLinesAsync"/> is its own
/// small, independent read over <see cref="QuoteLine"/>, returned alongside the assessment on every
/// <see cref="RecalculateAsync"/> response so a caller never has to guess whether a correction is still
/// needed.
/// </para>
///
/// <para>
/// A call with <paramref name="RecalculateAsync"/>'s own <c>corrections</c> null/empty is a valid,
/// honest "pure refresh" — it writes nothing new, but still re-runs normalization (harmless — see
/// <see cref="SkuNormalizationService.NormalizeAsync"/>'s own "re-runnable, unchanged" contract) and
/// returns the current unmatched-line list and a fresh assessment. This is how a caller first
/// discovers what needs a correction, e.g. right after `POST /api/quotes` reports a non-zero
/// `unmatchedSkuCount`, before any correction has been decided.
/// </para>
///
/// Owns its own tenant scope (<see cref="ITenantContext.BeginScope"/>) — the same "trusts nothing
/// upstream already opened one" posture every other tenant-scoped application service in this module
/// takes (<c>QuoteUploadService</c>, <c>NegotiationOutcomeService</c>, and, after task
/// E05/F04/US01/T01's own fix, <c>MarketAssessmentService</c>/<c>NegotiationStrategyService</c> too)
/// — never repeats the always-404-in-production class of bug that task's own doc comment documents.
/// </summary>
public sealed class SkuMappingService(
    QuotesDbContext dbContext,
    ITenantContext tenantContext,
    IClock clock,
    SkuNormalizationService skuNormalizationService,
    MarketAssessmentService marketAssessmentService,
    IAuditWriter auditWriter)
{
    /// <summary>Returned by <see cref="RecalculateAsync"/> when <c>quoteId</c> does not name a
    /// <see cref="Quote"/> for the caller's tenant. <c>Raffa.Api.QuotesEndpointExtensions</c> maps
    /// exactly this string to 404 — same <c>Result&lt;T&gt;.Error</c>-sentinel-to-404 convention
    /// <c>NegotiationOutcomeService.QuoteNotFoundError</c>/<c>ContractCorrectionService
    /// .ContractNotFoundError</c> already establish.</summary>
    public const string QuoteNotFoundError = "Quote not found.";

    public static string SkuRequiredError { get; } =
        "'sku' is required for every manual product-mapping correction.";

    public static string CanonicalSkuRequiredError { get; } =
        "'canonicalSku' is required for every manual product-mapping correction.";

    private const string AuditRecalculatedAction = "quote.sku_mapping_recalculated";
    private const string AuditResourceType = "quote";

    /// <summary>Same interim-actor placeholder as <c>QuoteUploadService.UnattributedActor</c>/
    /// <c>NegotiationOutcomeService.UnattributedActor</c> — ADR-010 (Entra ID/OIDC) is not in this
    /// task's "Architecture decisions in force" list, so there is no validated caller identity
    /// yet.</summary>
    private const string UnattributedActor = "unattributed";

    /// <summary>
    /// Backs `POST /api/quotes/{id}/assessment/recalculate`. Validates every <paramref name="corrections"/>
    /// entry fully before any query or write (an invalid request leaves the database untouched — same
    /// "phase 1 validate, phase 2 mutate" discipline <c>NegotiationOutcomeService.CaptureAsync</c>/
    /// <c>Raffa.Documents.Contracts.Application.ContractCorrectionService.CorrectAsync</c> already
    /// follow), then confirms <paramref name="quoteId"/> names a real, tenant-scoped <see cref="Quote"/>
    /// (the same tenant-scoped existence check <c>NegotiationOutcomeService.CaptureAsync</c> already
    /// performs for the identical id) before writing anything — a bogus quote id in the URL must not
    /// silently write a tenant-level <see cref="SkuProductMapping"/> row and report success.
    ///
    /// Upserts one <see cref="SkuProductMapping"/> row per valid correction (never a duplicate for the
    /// same tenant+normalized-SKU — <see cref="Infrastructure.Configurations.SkuProductMappingConfiguration"/>'s
    /// own unique index would reject one anyway), re-runs <see cref="SkuNormalizationService.NormalizeAsync"/>
    /// for every line on the quote (not just the one line a person happened to correct — a mapping
    /// learned here retroactively resolves every other line, this quote or a future one, sharing the
    /// same normalized SKU), then re-runs <see cref="MarketAssessmentService.AssessAsync"/> (AC-3) so
    /// the returned assessment reflects the just-corrected match state.
    /// </summary>
    public async Task<Result<QuoteRecalculationResult>> RecalculateAsync(
        TenantId tenantId,
        EntityId quoteId,
        IReadOnlyList<SkuMappingCorrection>? corrections,
        CancellationToken cancellationToken = default)
    {
        var validatedCorrections = corrections ?? [];
        foreach (var correction in validatedCorrections)
        {
            if (string.IsNullOrWhiteSpace(correction.Sku))
            {
                return Result<QuoteRecalculationResult>.Failure(SkuRequiredError);
            }

            if (string.IsNullOrWhiteSpace(correction.CanonicalSku))
            {
                return Result<QuoteRecalculationResult>.Failure(CanonicalSkuRequiredError);
            }
        }

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var quoteExists = await dbContext.Quotes
            .AnyAsync(q => q.TenantId == tenantId && q.Id == quoteId, cancellationToken)
            .ConfigureAwait(false);

        if (!quoteExists)
        {
            return Result<QuoteRecalculationResult>.Failure(QuoteNotFoundError);
        }

        var now = clock.UtcNow;
        var mappingsApplied = 0;

        if (validatedCorrections.Count > 0)
        {
            var mappingsBySku = await dbContext.SkuProductMappings
                .Where(m => m.TenantId == tenantId)
                .ToDictionaryAsync(m => m.NormalizedSku, StringComparer.Ordinal, cancellationToken)
                .ConfigureAwait(false);

            foreach (var correction in validatedCorrections)
            {
                var normalizedSku = SkuNormalizer.Normalize(correction.Sku)!;
                mappingsBySku.TryGetValue(normalizedSku, out var existing);

                var mapping = ApplyCorrection(existing, normalizedSku, correction, tenantId, now);
                if (existing is null)
                {
                    dbContext.SkuProductMappings.Add(mapping);
                    // Keeps a second correction in this same request targeting the identical
                    // normalized SKU (e.g. a caller submitting the same fix twice) on the update
                    // path below rather than attempting a second Add — the unique (tenant_id,
                    // normalized_sku) index would otherwise reject it at SaveChangesAsync time.
                    mappingsBySku[normalizedSku] = mapping;
                }

                mappingsApplied++;
            }

            // Persist the mapping write(s) before re-normalizing: SkuNormalizationService queries
            // SkuProductMappings back from the database (see its own doc comment), so it must run
            // after they are actually committed — the same ordering constraint
            // Raffa.Api.QuoteExtractionPipeline's own doc comment documents for QuoteLines.
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var normalizationOutcome = await skuNormalizationService
            .NormalizeAsync(tenantId, quoteId, cancellationToken)
            .ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var unmatchedLines = await GetUnmatchedLinesAsync(tenantId, quoteId, cancellationToken)
            .ConfigureAwait(false);

        // AC-3 "Re-run assessment after mapping correction": fresh, not cached, so it reflects the
        // match state this call just (possibly) changed. Quote existence was already confirmed
        // above, so this is not expected to hit AssessAsync's own "not found" branch — propagated
        // verbatim rather than assumed unreachable (Appendix C rule 10), the same "never re-validate,
        // but never assume away either" posture NegotiationStrategyService.GenerateAsync's own doc
        // comment documents for the identical composed call.
        var assessmentResult = await marketAssessmentService
            .AssessAsync(tenantId, quoteId, cancellationToken)
            .ConfigureAwait(false);

        if (assessmentResult.IsFailure)
        {
            return Result<QuoteRecalculationResult>.Failure(assessmentResult.Error);
        }

        // Recorded only once every step above is durable, still inside this call's own tenant scope
        // (same placement as QuoteUploadService.UploadAsync's own "write then audit" ordering).
        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                UnattributedActor,
                AuditRecalculatedAction,
                AuditResourceType,
                quoteId.Value.ToString(),
                now,
                $"mappingsApplied={mappingsApplied} matched={normalizationOutcome.MatchedCount} " +
                $"unmatched={normalizationOutcome.UnmatchedCount} " +
                $"notApplicable={normalizationOutcome.NotApplicableCount}"),
            cancellationToken).ConfigureAwait(false);

        return Result<QuoteRecalculationResult>.Success(new QuoteRecalculationResult(
            quoteId, mappingsApplied, normalizationOutcome, unmatchedLines, assessmentResult.Value));
    }

    /// <summary>
    /// The per-correction upsert rule, isolated as its own pure step (mutates and returns
    /// <paramref name="existing"/> when supplied, otherwise builds a new row) so it is directly
    /// unit-testable without a database — mirrors <c>SkuNormalizationService.Apply</c>'s own "pure
    /// core, thin DB-aware wrapper" split. Callers must have already validated <c>correction.Sku</c>/
    /// <c>correction.CanonicalSku</c> as non-blank (see <see cref="RecalculateAsync"/>'s own "phase 1
    /// validate" pass) and normalized <paramref name="normalizedSku"/> via <see cref="SkuNormalizer.Normalize"/>
    /// — this method trusts both preconditions rather than re-checking them.
    /// </summary>
    internal static SkuProductMapping ApplyCorrection(
        SkuProductMapping? existing,
        string normalizedSku,
        SkuMappingCorrection correction,
        TenantId tenantId,
        DateTimeOffset now)
    {
        var normalizedEdition = SkuNormalizer.Normalize(correction.Edition);
        var canonicalSku = correction.CanonicalSku.Trim();

        if (existing is not null)
        {
            existing.NormalizedEdition = normalizedEdition;
            existing.CanonicalSku = canonicalSku;
            existing.CanonicalEdition = NullIfBlank(correction.CanonicalEdition);
            existing.CanonicalProductName = NullIfBlank(correction.CanonicalProductName);
            return existing;
        }

        return new SkuProductMapping
        {
            TenantId = tenantId,
            NormalizedSku = normalizedSku,
            NormalizedEdition = normalizedEdition,
            CanonicalSku = canonicalSku,
            CanonicalEdition = NullIfBlank(correction.CanonicalEdition),
            CanonicalProductName = NullIfBlank(correction.CanonicalProductName),
            CreatedAt = now,
        };
    }

    /// <summary>AC-2's "Show unmatched SKUs" half — see this type's own doc comment for why this is
    /// a small, independent read rather than an extension of <see cref="Assessment.LineMarketAssessment"/>.</summary>
    private async Task<IReadOnlyList<UnmatchedQuoteLineSku>> GetUnmatchedLinesAsync(
        TenantId tenantId, EntityId quoteId, CancellationToken cancellationToken)
    {
        var lines = await dbContext.QuoteLines
            .Where(l => l.TenantId == tenantId && l.QuoteId == quoteId && l.MatchStatus == SkuMatchStatus.Unmatched)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Sku/NormalizedSku are never null here: SkuNormalizationService.Apply only assigns
        // MatchStatus.Unmatched when the line has a present (non-blank) Sku (see SkuMatchStatus's
        // own doc comment) -- NotApplicable is the status for a missing one.
        return lines
            .Select(l => new UnmatchedQuoteLineSku(l.Id, l.Sku!, l.NormalizedSku!, l.Edition, l.Description))
            .ToList();
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Outcome of one <see cref="SkuMappingService.RecalculateAsync"/> call —
/// <c>Raffa.Api.QuotesEndpointExtensions</c> folds this into `POST
/// /api/quotes/{id}/assessment/recalculate`'s own JSON response.</summary>
/// <param name="MappingsAppliedCount">How many of the caller's supplied corrections were written
/// (created or updated) — <c>0</c> for a pure-refresh call with no corrections.</param>
/// <param name="NormalizationOutcome">The same per-quote counts <c>Raffa.Api.QuoteExtractionPipeline</c>'s
/// own upload-time normalization pass reports — see <see cref="SkuNormalizationOutcome"/>'s own doc
/// comment.</param>
/// <param name="UnmatchedLines">AC-2's "Show unmatched SKUs" half — every line still unresolved after
/// this call's own (possibly zero) corrections were applied.</param>
/// <param name="Assessment">AC-3's "Re-run assessment after mapping correction" half — freshly
/// recomputed, not cached.</param>
public sealed record QuoteRecalculationResult(
    EntityId QuoteId,
    int MappingsAppliedCount,
    SkuNormalizationOutcome NormalizationOutcome,
    IReadOnlyList<UnmatchedQuoteLineSku> UnmatchedLines,
    QuoteMarketAssessment Assessment);
