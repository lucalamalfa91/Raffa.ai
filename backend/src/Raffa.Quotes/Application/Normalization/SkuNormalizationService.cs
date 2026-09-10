using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Quotes.Application.Normalization;

/// <summary>
/// Implements task E05/F01/US02/T01 (sku-normalization; parent story us-02-sku-normalization AC-1
/// "Normalize SKU/edition to the canonical product mapping" and AC-2's "Show unmatched SKUs" half —
/// the other half of AC-2, "...and allow manual product mapping", plus AC-3 "Re-run assessment
/// after mapping correction", are task E05/F01/US02/T02's own scope). Mirrors
/// <c>Raffa.Quotes.Application.Extraction.QuoteLineExtractionService</c>'s own "only adds/updates
/// the change tracker, does not call <c>SaveChangesAsync</c> itself" division of responsibility, so
/// a caller (<c>Raffa.Api.QuoteExtractionPipeline</c> today; task T02's own recalculate endpoint
/// later) can persist this alongside whatever else it is already doing in one unit of work.
///
/// <see cref="SkuProductMapping"/> is this module's own, self-contained "canonical product
/// mapping" — see that type's own doc comment for why it is not a reference into
/// <c>Raffa.Suppliers.Products</c>. Nothing populates it yet (task T02's manual-mapping endpoint
/// is its intended first writer), so — honestly — a line with a present SKU normalizes to
/// <see cref="SkuMatchStatus.Unmatched"/> for every tenant today. That is spec §11.3's own guardrail
/// ("Do not generate a savings target if line-item normalization is unresolved") made real rather
/// than a limitation of this service: no benchmark/assessment step for quotes exists yet either
/// (module-map.md's "Assessment" entity is still unbuilt) for a resolved mapping to unblock.
/// </summary>
public sealed class SkuNormalizationService(QuotesDbContext dbContext)
{
    /// <summary>
    /// Normalizes every <see cref="QuoteLine"/> already persisted for <paramref name="quoteId"/>
    /// against this tenant's own <see cref="SkuProductMapping"/> rows, updating each line's
    /// <see cref="QuoteLine.NormalizedSku"/>/<see cref="QuoteLine.NormalizedEdition"/>/
    /// <see cref="QuoteLine.MatchStatus"/> on the change tracker (the caller still owns
    /// <c>SaveChangesAsync</c>). Queries lines back from the database rather than accepting them as
    /// a parameter, so this same method is re-runnable later, unchanged, against whatever a quote's
    /// lines look like at that time: task T02's own "recalculate" endpoint (spec Appendix A
    /// <c>POST /api/quotes/{id}/assessment/recalculate</c>) calls this again after a manual mapping
    /// is added, and every line for the quote — not just the one a person just corrected — is
    /// re-evaluated, so a mapping learned from correcting one line retroactively resolves every
    /// other line sharing the same normalized SKU (this quote or a future one).
    /// </summary>
    public async Task<SkuNormalizationOutcome> NormalizeAsync(
        TenantId tenantId, EntityId quoteId, CancellationToken cancellationToken = default)
    {
        var lines = await dbContext.QuoteLines
            .Where(l => l.TenantId == tenantId && l.QuoteId == quoteId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var mappingsBySku = await LoadMappingsBySkuAsync(tenantId, cancellationToken).ConfigureAwait(false);

        var matched = 0;
        var unmatched = 0;
        var notApplicable = 0;

        foreach (var line in lines)
        {
            switch (Apply(line, mappingsBySku))
            {
                case SkuMatchStatus.Matched:
                    matched++;
                    break;
                case SkuMatchStatus.Unmatched:
                    unmatched++;
                    break;
                default:
                    notApplicable++;
                    break;
            }
        }

        return new SkuNormalizationOutcome(lines.Count, matched, unmatched, notApplicable);
    }

    /// <summary>
    /// The per-line matching rule, isolated as its own pure step (mutates <paramref name="line"/>,
    /// returns its resulting <see cref="SkuMatchStatus"/>) so it is directly unit-testable against a
    /// hand-built mapping dictionary, without a database — mirrors
    /// <c>QuoteLineExtractionService.ComputePricing</c>'s own "pure core, thin DB-aware wrapper"
    /// split. <paramref name="mappingsBySku"/> is keyed by <see cref="SkuProductMapping.NormalizedSku"/>
    /// only (not edition, see that type's own doc comment for why) — one mapping resolves every
    /// line sharing that normalized SKU regardless of edition text.
    /// </summary>
    internal static SkuMatchStatus Apply(
        QuoteLine line, IReadOnlyDictionary<string, SkuProductMapping> mappingsBySku)
    {
        var normalizedSku = SkuNormalizer.Normalize(line.Sku);
        line.NormalizedSku = normalizedSku;
        line.NormalizedEdition = SkuNormalizer.Normalize(line.Edition);

        line.MatchStatus = normalizedSku is null
            ? SkuMatchStatus.NotApplicable
            : mappingsBySku.ContainsKey(normalizedSku)
                ? SkuMatchStatus.Matched
                : SkuMatchStatus.Unmatched;

        return line.MatchStatus;
    }

    private async Task<IReadOnlyDictionary<string, SkuProductMapping>> LoadMappingsBySkuAsync(
        TenantId tenantId, CancellationToken cancellationToken)
    {
        var mappings = await dbContext.SkuProductMappings
            .Where(m => m.TenantId == tenantId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Ordinal: NormalizedSku is always SkuNormalizer's own uppercased output, and the unique
        // (tenant_id, normalized_sku) index (SkuProductMappingConfiguration) guarantees no
        // duplicate key ever reaches this dictionary.
        return mappings.ToDictionary(m => m.NormalizedSku, StringComparer.Ordinal);
    }
}

/// <summary>Counts a caller (<c>Raffa.Api.QuoteExtractionPipeline</c> today) can fold into its
/// own response/telemetry — mirrors <c>QuoteLineExtractionOutcome</c>'s identical shape/purpose for
/// the sibling extraction stage.</summary>
public sealed record SkuNormalizationOutcome(
    int LineCount, int MatchedCount, int UnmatchedCount, int NotApplicableCount);
