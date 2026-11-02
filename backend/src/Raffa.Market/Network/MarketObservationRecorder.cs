using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.Extensions.Logging;
using Raffa.Market.Infrastructure;
using Raffa.Market.Infrastructure.Entities;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Market;

namespace Raffa.Market.Network;

/// <summary>
/// The invisible half of a renewal case: what the supplier answered is anonymised and added to the shared
/// market store, and an audit row in the contributing workspace says so (what was recorded, never who).
/// Never throws and never waits on anything the user sees: a failure is logged and the case goes on.
/// </summary>
public sealed partial class MarketObservationRecorder(
    IDbContextFactory<MarketDbContext> dbFactory,
    IAuditWriter auditWriter,
    IClock clock,
    MarketObservationOptions options,
    ILogger<MarketObservationRecorder>? logger = null) : IMarketObservationSink
{
    public const string RecordedAction = "market.observation.recorded";

    public const string ResourceType = "market_observation";

    public const string ReplySource = "supplier-reply";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task RecordAsync(
        TenantId tenantId, string actor, string correlationId, SupplierReplyObservation observation, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!options.Enabled || observation is null || string.IsNullOrWhiteSpace(observation.Supplier))
            {
                return;
            }

            var clean = Sanitize(observation);
            if (clean is null)
            {
                return;
            }

            var supplierKey = SupplierKey(clean.Supplier);
            var entity = new MarketObservationEntity
            {
                Id = Guid.NewGuid(),
                WorkspaceKey = Hash(options.Pepper, "ws", tenantId.Value.ToString()),
                ObservationKey = Hash(options.Pepper, "obs", tenantId.Value.ToString(), correlationId, supplierKey, clean.TermMonths?.ToString() ?? string.Empty),
                SupplierKey = supplierKey,
                Supplier = clean.Supplier,
                Category = clean.Category,
                Geography = clean.Geography,
                Currency = clean.Currency,
                Product = clean.Product,
                Sku = clean.Sku,
                Unit = clean.Unit,
                UnitPrice = clean.UnitPrice,
                TermMonths = clean.TermMonths,
                DiscountPct = clean.DiscountPct,
                UpliftCapPct = clean.UpliftCapPct,
                NoticeDays = clean.NoticeDays,
                PaymentTerms = clean.PaymentTerms,
                Source = ReplySource,
                ObservedAt = clean.ObservedAt,
            };

            // The API role may only INSERT into market_observation (it can never read what other workspaces
            // contributed), so duplicates are caught by the unique key instead of being looked up first.
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            db.MarketObservations.Add(entity);
            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // The same reply recorded twice: the unique key kept one, which is what we want.
                return;
            }

            var fields = new List<string>();
            if (clean.UnitPrice is not null) fields.Add("unitPrice");
            if (clean.DiscountPct is not null) fields.Add("discountPct");
            if (clean.UpliftCapPct is not null) fields.Add("upliftCapPct");
            if (clean.NoticeDays is not null) fields.Add("noticeDays");
            if (clean.PaymentTerms is not null) fields.Add("paymentTerms");
            await auditWriter.WriteAsync(
                    new AuditEntry(
                        tenantId, string.IsNullOrWhiteSpace(actor) ? "raffa" : actor, RecordedAction, ResourceType, entity.Id.ToString(), clock.UtcNow,
                        JsonSerializer.Serialize(new { supplier = clean.Supplier, termMonths = clean.TermMonths, fields, anonymised = true }, Json)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "A supplier reply could not be recorded as a market observation.");
        }
    }

    /// <summary>Keeps only values that are plausible and short: a model slip never reaches the shared store.</summary>
    internal static SupplierReplyObservation? Sanitize(SupplierReplyObservation o)
    {
        var supplier = o.Supplier.Trim();
        if (supplier.Length is < 2 or > 200)
        {
            return null;
        }

        var clean = o with
        {
            Supplier = supplier,
            Category = Short(o.Category, 100),
            Geography = Short(o.Geography, 100),
            Currency = o.Currency is { Length: 3 } c && c.All(char.IsLetter) ? c.ToUpperInvariant() : null,
            Product = Short(o.Product, 200),
            Sku = Short(o.Sku, 100),
            Unit = Short(o.Unit, 60),
            UnitPrice = o.UnitPrice is > 0 and < 10_000_000 ? decimal.Round(o.UnitPrice.Value, 4) : null,
            TermMonths = o.TermMonths is >= 1 and <= 120 ? o.TermMonths : null,
            DiscountPct = o.DiscountPct is >= 0 and <= 90 ? Math.Round(o.DiscountPct.Value, 2) : null,
            UpliftCapPct = o.UpliftCapPct is >= 0 and <= 50 ? Math.Round(o.UpliftCapPct.Value, 2) : null,
            NoticeDays = o.NoticeDays is >= 0 and <= 730 ? o.NoticeDays : null,
            PaymentTerms = o.PaymentTerms is { } p && PaymentTermsPattern().IsMatch(p.Trim()) ? p.Trim() : null,
        };
        return clean.HasTerms ? clean : null;
    }

    internal static string SupplierKey(string supplier) =>
        WhitespacePattern().Replace(supplier.Trim().ToLowerInvariant(), " ");

    internal static string Hash(string pepper, params string[] parts)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(pepper));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(string.Join('|', parts)))).ToLowerInvariant();
    }

    private static string? Short(string? text, int max) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim().Length > max ? text.Trim()[..max] : text.Trim();

    [GeneratedRegex(@"^[\p{L}\p{N} .,%/+\-]{1,100}$")]
    private static partial Regex PaymentTermsPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
