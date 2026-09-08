using Contigo.Market.Contracts;

namespace Contigo.Market.Tests;

/// <summary>
/// Shared test-only <see cref="MarketDeal"/> builder. Defaults to a Salesforce/Sales Cloud
/// Enterprise/CH shape matching R-MKT-03's own worked example ("Companies of 500-2000 employees
/// closing Salesforce Sales Cloud in CH in 2026 paid P50 CHF 132 per user/month, obtained 3-5 %
/// uplift caps and 90-day notice…") so composer/provenance tests read naturally; every field is
/// overridable for tests that need a different shape (a thin row, a SKU-level deal, …).
/// </summary>
internal static class SampleDeal
{
    public static MarketDeal Create(
        string provider = "Internal Dataset",
        string recordId = "MKT-TEST-0001",
        string supplier = "Salesforce",
        string category = "Enterprise Software",
        string product = "Sales Cloud Enterprise",
        string geography = "CH",
        string currency = "CHF",
        string companySizeBand = "500-2000",
        int termMonths = 12,
        string annualValueBand = "100k-250k",
        decimal unitPriceP25 = 118m,
        decimal unitPriceP50 = 132m,
        decimal unitPriceP75 = 149m,
        IReadOnlyList<NegotiatedClause>? negotiatedClauses = null,
        string closingPeriod = "2026-Q1",
        int sampleSize = 64,
        string source = "mock",
        bool representative = true,
        DateTimeOffset? updatedAt = null,
        string? sku = null,
        double? discountAchievedPct = 8,
        double? upliftCapPct = 4,
        int? noticeDays = 90,
        string? paymentTerms = "Net 30",
        string? licenseRestrictions = null) =>
        new(
            Provider: provider,
            RecordId: recordId,
            Supplier: supplier,
            Category: category,
            Product: product,
            Geography: geography,
            Currency: currency,
            CompanySizeBand: companySizeBand,
            TermMonths: termMonths,
            AnnualValueBand: annualValueBand,
            UnitPriceP25: unitPriceP25,
            UnitPriceP50: unitPriceP50,
            UnitPriceP75: unitPriceP75,
            NegotiatedClauses: negotiatedClauses ?? [],
            ClosingPeriod: closingPeriod,
            SampleSize: sampleSize,
            Source: source,
            Representative: representative,
            UpdatedAt: updatedAt ?? new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero),
            Sku: sku,
            DiscountAchievedPct: discountAchievedPct,
            UpliftCapPct: upliftCapPct,
            NoticeDays: noticeDays,
            PaymentTerms: paymentTerms,
            LicenseRestrictions: licenseRestrictions);
}
