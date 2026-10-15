using Microsoft.EntityFrameworkCore;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Quotes.Application.Header;

/// <summary>A person's correction or confirmation of a quote's header fields. A <see langword="null"/>
/// field is left unchanged (there is no way to blank a field; a value can only be replaced).</summary>
public sealed record QuoteHeaderUpdate(
    string? Supplier = null, string? Currency = null, string? Geography = null, DateOnly? PurchaseDate = null);

/// <summary>The header fields of a quote as recorded.</summary>
public sealed record QuoteHeader(
    EntityId QuoteId, string? Supplier, string? Currency, string? Geography, DateOnly? PurchaseDate);

/// <summary>
/// Task F6-T07 ("conferma utente"): sets or corrects the header fields of a quote, which are what the
/// benchmark match is built from. The header stage fills blanks from the document; this is the person
/// confirming or overriding them. Nothing is recomputed here: the assessment reads the quote's fields
/// when it is asked for, so a correction is reflected in the next
/// <c>GET /api/quotes/{id}/assessment</c>.
///
/// <para>
/// A person's value always wins: it is validated by <see cref="QuoteHeaderRules"/> and written over
/// whatever is there, whether the document stage or an earlier upload put it there. One audit row
/// (<c>quote.header.updated</c>) names the fields changed, never their values.
/// </para>
/// </summary>
public sealed class QuoteHeaderUpdateService(
    QuotesDbContext dbContext, ITenantContext tenantContext, IAuditWriter auditWriter, IClock clock)
{
    public const string UpdatedAuditAction = "quote.header.updated";

    /// <summary>Returned (as a failure) for a request that changes nothing.</summary>
    public const string NothingToChangeError =
        "Provide at least one of supplier, currency, geography or purchaseDate.";

    /// <summary>
    /// <see langword="null"/> when the quote does not exist for this tenant (the endpoint's 404); a
    /// failure naming the first invalid field; otherwise the header as recorded after the update.
    /// </summary>
    public async Task<Result<QuoteHeader>?> UpdateAsync(
        TenantId tenantId,
        EntityId quoteId,
        QuoteHeaderUpdate update,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var quote = await dbContext.Quotes
            .SingleOrDefaultAsync(q => q.TenantId == tenantId && q.Id == quoteId, cancellationToken)
            .ConfigureAwait(false);
        if (quote is null)
        {
            return null;
        }

        var now = clock.UtcNow;
        var changed = new List<string>();

        if (update.Supplier is not null)
        {
            var supplier = QuoteHeaderRules.NormalizeSupplier(update.Supplier);
            if (supplier is null)
            {
                return Result<QuoteHeader>.Failure(
                    $"supplier must be between 2 and {QuoteHeaderRules.MaxSupplierLength} characters.");
            }

            quote.Supplier = supplier;
            changed.Add("supplier");
        }

        if (update.Currency is not null)
        {
            var currency = QuoteHeaderRules.NormalizeCurrency(update.Currency);
            if (currency is null)
            {
                return Result<QuoteHeader>.Failure("currency must be a three-letter code such as EUR.");
            }

            quote.Currency = currency;
            changed.Add("currency");
        }

        if (update.Geography is not null)
        {
            var geography = QuoteHeaderRules.NormalizeGeography(update.Geography);
            if (geography is null)
            {
                return Result<QuoteHeader>.Failure(
                    $"geography must be between 1 and {QuoteHeaderRules.MaxGeographyLength} characters.");
            }

            quote.Geography = geography;
            changed.Add("geography");
        }

        if (update.PurchaseDate is { } purchaseDate)
        {
            if (!QuoteHeaderRules.IsPlausibleQuoteDate(purchaseDate, now))
            {
                return Result<QuoteHeader>.Failure(
                    $"purchaseDate must be between {QuoteHeaderRules.EarliestQuoteDate:yyyy-MM-dd} and {QuoteHeaderRules.MaxDaysAhead} days from today.");
            }

            quote.PurchaseDate = purchaseDate;
            changed.Add("purchaseDate");
        }

        if (changed.Count == 0)
        {
            return Result<QuoteHeader>.Failure(NothingToChangeError);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId, actor, UpdatedAuditAction, "quote", quoteId.Value.ToString(), now,
                string.Join(',', changed)),
            cancellationToken).ConfigureAwait(false);

        return Result<QuoteHeader>.Success(
            new QuoteHeader(quote.Id, quote.Supplier, quote.Currency, quote.Geography, quote.PurchaseDate));
    }
}
