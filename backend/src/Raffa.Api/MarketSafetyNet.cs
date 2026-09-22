using System.Globalization;
using System.Text.RegularExpressions;
using Raffa.Benchmark.Contracts;
using Raffa.Chat.Application.Pack;
using Raffa.Documents.Contracts.Application;
using Raffa.Market.Contracts;

namespace Raffa.Api;

/// <summary>
/// The market safety net of an Ask turn about one contract (persona v2.5): when the contract's own
/// data is incomplete, Ask says so honestly and still gives a plausible answer — built on the
/// tenant's facts first and, where they fall short, on the market feed (the same supplier's
/// comparable deals; similar contracts through the market RAG when the supplier has none). This
/// type is the deterministic half: which fields the contract is missing, a narrow annual-value
/// estimate when the annual spend is missing, and the negotiated terms comparable customers got —
/// each a citable pack item whose figures are pack values, so <c>NumericGuard</c> keeps every
/// number the model states verbatim.
///
/// <para>
/// <b>Never a range too wide to mean anything.</b> An estimate is only published when its high end
/// is at most <see cref="MaxRangeRatio"/> times its low end; the order of preference is the
/// narrowest source the data supports: the contract's own quantities at the market's P25–P75 unit
/// prices, then a comparable deal whose unit price is the whole contract's yearly value (an
/// insurance premium, a facilities contract), then the annual-value band most comparable deals
/// fall in. Terms follow the same rule: an interquartile range across deals, or only the median
/// when even that is too wide. Pure and synchronous — the caller fetches the contract, its priced
/// lines and the deals.
/// </para>
/// </summary>
internal static class MarketSafetyNet
{
    /// <summary>The widest range worth giving: high end at most this many times the low end
    /// (EUR 250,000–500,000 is published; EUR 1m–5m is not).</summary>
    internal const decimal MaxRangeRatio = 2.5m;

    private static readonly Regex Word = new(@"[\p{L}\p{N}]{3,}", RegexOptions.Compiled);

    /// <summary>An annual-value estimate for one contract. <see cref="Low"/> is
    /// <see langword="null"/> for an open-ended "below" band.</summary>
    internal sealed record AnnualEstimate(
        decimal? Low,
        decimal High,
        string Currency,
        AnnualEstimateBasis Basis,
        string? Product,
        string? CompanySizeBand,
        int SampleSize);

    internal enum AnnualEstimateBasis
    {
        /// <summary>The contract's own quantities at the market's P25–P75 unit prices.</summary>
        QuantitiesAtMarketPrices,

        /// <summary>A comparable deal whose unit price is the contract's whole yearly value.</summary>
        ComparableContractValue,

        /// <summary>The annual-value band most comparable deals fall in.</summary>
        ComparableValueBand,
    }

    /// <summary>The contract fields the data check looks for, as the English labels the pack
    /// carries. Which of them a question needs is the agents' and the answer's call; this type only
    /// says which are missing and what the market says in their place.</summary>
    internal static class Field
    {
        public const string AnnualSpend = "annual spend";
        public const string EndDate = "end date";
        public const string NoticeDeadline = "cancellation (notice) deadline";
        public const string RenewalTerm = "renewal term";
        public const string PaymentTerms = "payment terms";
        public const string PricedLines = "priced line items";

        /// <summary>The field in the reader's language, with its article (Italian) — for the
        /// deterministic lead.</summary>
        public static string Label(string field, bool italian) => (field, italian) switch
        {
            (AnnualSpend, true) => "gli importi annuali",
            (EndDate, true) => "la data di scadenza",
            (NoticeDeadline, true) => "la data di disdetta",
            (RenewalTerm, true) => "la durata del rinnovo",
            (PaymentTerms, true) => "i termini di pagamento",
            (PricedLines, true) => "le righe di prezzo",
            (AnnualSpend, false) => "the annual amounts",
            (EndDate, false) => "the end date",
            (NoticeDeadline, false) => "the notice deadline",
            (RenewalTerm, false) => "the renewal term",
            (PaymentTerms, false) => "the payment terms",
            (PricedLines, false) => "the priced line items",
            _ => field,
        };

        /// <summary>Whether the Italian label is plural (it takes "mancano", not "manca").</summary>
        public static bool IsPluralInItalian(string field) => field is AnnualSpend or PaymentTerms or PricedLines;
    }

    /// <summary>One contract's data check: what it is missing and the market's stand-ins — the
    /// deterministic lead's input, one per contract checked.</summary>
    internal sealed record ContractCheck(
        string SupplierName,
        IReadOnlyList<string> Missing,
        AnnualEstimate? Estimate,
        int? TypicalNoticeDays,
        DateOnly? EstimatedNoticeDeadline);

    /// <summary>The key fields this contract has no validated value for, in English, most
    /// consequential first — what an honest answer names, when the question needs them, before it
    /// estimates.</summary>
    public static IReadOnlyList<string> MissingFields(Contract360Result contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var missing = new List<string>();
        if (contract.Header.AnnualSpend is null && contract.Commercials.AnnualSpend is null &&
            contract.Commercials.LineItemAnnualCostTotal is null)
        {
            missing.Add(Field.AnnualSpend);
        }

        if (contract.Header.EndDate is null && contract.Header.RenewalDate is null)
        {
            missing.Add(Field.EndDate);
        }

        if (contract.Header.CancellationDeadline is null && contract.Renewal.CancellationDeadline is null)
        {
            missing.Add(Field.NoticeDeadline);
        }

        if (contract.Overview.RenewalTermMonths is null)
        {
            missing.Add(Field.RenewalTerm);
        }

        if (string.IsNullOrWhiteSpace(contract.Overview.PaymentTerms))
        {
            missing.Add(Field.PaymentTerms);
        }

        if (contract.Products.Count == 0)
        {
            missing.Add(Field.PricedLines);
        }

        return missing;
    }

    /// <summary>
    /// The notice deadline the market implies when the contract has an end date but no notice
    /// deadline: the end date minus the notice period comparable customers have. A market estimate,
    /// labelled as one; <see langword="null"/> without an end date or a typical notice.
    /// </summary>
    public static PackItem? NoticeEstimateItem(Guid contractId, string supplierName, DateOnly? endDate, int? typicalNoticeDays)
    {
        if (endDate is not { } end || typicalNoticeDays is not { } days || days <= 0)
        {
            return null;
        }

        var deadline = end.AddDays(-days);
        return new PackItem(
            $"market:estimate:{contractId}:notice-deadline",
            PackCorpus.Market,
            $"{supplierName} · market estimate · notice deadline",
            "market estimate · not from your contract",
            null,
            null,
            $"Market estimate, not a date from your contract: comparable {supplierName} customers give " +
            $"{days.ToString(CultureInfo.InvariantCulture)} days' notice; with the contract ending {Iso(end)}, " +
            $"notice would be due by {Iso(deadline)}.",
            null,
            null,
            null,
            "market estimate · representative market data",
            [
                new PackValue("endDate", Iso(end), PackValueKind.Date),
                new PackValue("estimatedNoticeDeadline", Iso(deadline), PackValueKind.Date),
                new PackValue("noticeDays", days.ToString(CultureInfo.InvariantCulture), PackValueKind.Number),
            ],
            contractId.ToString());
    }

    /// <summary>The estimated notice deadline itself (see <see cref="NoticeEstimateItem"/>).</summary>
    public static DateOnly? EstimatedNoticeDeadline(DateOnly? endDate, int? typicalNoticeDays) =>
        endDate is { } end && typicalNoticeDays is { } days && days > 0 ? end.AddDays(-days) : null;

    /// <summary>
    /// A multi-contract turn's coverage line (a quarter, savings across contracts): which of the
    /// contracts considered lack which data and which gaps the market covers — so the answer can say
    /// how much of its result rests on estimates. <see langword="null"/> for fewer than two checks
    /// or when nothing is missing.
    /// </summary>
    public static PackItem? CoverageItem(IReadOnlyList<ContractCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        var withGaps = checks.Where(c => c.Missing.Count > 0).ToList();
        if (checks.Count < 2 || withGaps.Count == 0)
        {
            return null;
        }

        var gaps = string.Join("; ", withGaps.Select(c => $"{c.SupplierName} ({string.Join(", ", c.Missing)})"));
        var covered = withGaps
            .SelectMany(c => new[]
            {
                c.Estimate is not null ? $"{c.SupplierName} annual value" : null,
                c.EstimatedNoticeDeadline is not null || (c.Missing.Contains(Field.NoticeDeadline) && c.TypicalNoticeDays is not null)
                    ? $"{c.SupplierName} notice"
                    : null,
            })
            .OfType<string>()
            .ToList();

        return new PackItem(
            "calc:portfolio-data-coverage",
            PackCorpus.Calc,
            "Contracts considered · data coverage",
            null,
            null,
            null,
            $"Of the {checks.Count.ToString(CultureInfo.InvariantCulture)} contracts considered, " +
            $"{withGaps.Count.ToString(CultureInfo.InvariantCulture)} lack data: {gaps}. " +
            (covered.Count > 0
                ? $"Market estimates stand in for: {string.Join(", ", covered)}."
                : "No market estimate is narrow enough to stand in for them."),
            null,
            null,
            null,
            "deterministic calculator",
            []);
    }

    /// <summary>The "what this contract is missing" item — no figure in it, only the gap, so the
    /// model can say it plainly before it estimates. <see langword="null"/> when nothing is missing.</summary>
    public static PackItem? GapsItem(Guid contractId, string supplierName, IReadOnlyList<string> missing)
    {
        ArgumentNullException.ThrowIfNull(missing);
        if (missing.Count == 0)
        {
            return null;
        }

        return new PackItem(
            $"calc:contract-gaps[{contractId}]",
            PackCorpus.Calc,
            $"{supplierName} · data not on the contract",
            null,
            null,
            null,
            $"The validated {supplierName} contract has no value for: {string.Join(", ", missing)}.",
            null,
            null,
            null,
            "deterministic calculator",
            [],
            contractId.ToString());
    }

    /// <summary>
    /// The narrowest annual-value estimate the data supports, or <see langword="null"/> when none is
    /// narrow enough (see the type's doc comment for the order of preference).
    /// </summary>
    /// <param name="currency">The contract's own currency — every source must be in it.</param>
    /// <param name="pricedLines">The contract's priced lines, benchmarked where the market had data.</param>
    /// <param name="deals">The supplier's comparable deals, already narrowed to the contract's
    /// currency and geography.</param>
    public static AnnualEstimate? EstimateAnnualValue(
        string currency, IReadOnlyList<PricedLine> pricedLines, IReadOnlyList<MarketDeal> deals)
    {
        ArgumentNullException.ThrowIfNull(pricedLines);
        ArgumentNullException.ThrowIfNull(deals);

        return FromQuantities(currency, pricedLines)
            ?? FromComparableContractValue(currency, pricedLines, deals)
            ?? FromValueBand(currency, pricedLines, deals);
    }

    /// <summary>The estimate as a citable market item. Its snippet says in so many words that it is
    /// a market estimate, never the contract's own figure.</summary>
    public static PackItem EstimateItem(Guid contractId, string supplierName, AnnualEstimate estimate)
    {
        ArgumentNullException.ThrowIfNull(estimate);

        var range = estimate.Low is { } low
            ? $"between {Money(estimate.Currency, low)} and {Money(estimate.Currency, estimate.High)}"
            : $"below {Money(estimate.Currency, estimate.High)}";
        var basis = estimate.Basis switch
        {
            AnnualEstimateBasis.QuantitiesAtMarketPrices =>
                $"the quantities on your {supplierName} contract at the market's P25–P75 unit prices",
            AnnualEstimateBasis.ComparableContractValue =>
                $"the yearly contract value comparable customers pay {supplierName}" + ProductSuffix(estimate.Product),
            _ => $"the annual contract value most comparable {supplierName} deals fall in" + ProductSuffix(estimate.Product),
        };
        var size = estimate.CompanySizeBand is { } band ? $", companies of {band} employees" : string.Empty;

        var values = new List<PackValue>
        {
            new("estimateHigh", Amount(estimate.High), PackValueKind.Amount, estimate.Currency),
        };
        if (estimate.Low is { } lowValue)
        {
            values.Insert(0, new PackValue("estimateLow", Amount(lowValue), PackValueKind.Amount, estimate.Currency));
        }

        return new PackItem(
            $"market:estimate:{contractId}:annual-value",
            PackCorpus.Market,
            $"{supplierName} · market estimate · annual value",
            "market estimate · not from your contract",
            null,
            null,
            $"Market estimate, not a figure from your contract: based on {basis}, the annual value is " +
            $"{range} (sample of {estimate.SampleSize.ToString(CultureInfo.InvariantCulture)}{size}).",
            null,
            null,
            null,
            "market estimate · representative market data",
            values,
            contractId.ToString());
    }

    /// <summary>
    /// What comparable customers negotiated with this supplier — discount, uplift cap, notice, term
    /// and payment terms — as narrow ranges (interquartile across deals; the median alone when even that is too
    /// wide). <see langword="null"/> when no deal records any of them.
    /// </summary>
    public static PackItem? TermsItem(Guid contractId, string supplierName, IReadOnlyList<MarketDeal> deals)
    {
        ArgumentNullException.ThrowIfNull(deals);
        if (deals.Count == 0)
        {
            return null;
        }

        var parts = new List<string>();
        var values = new List<PackValue>();

        void AddPercent(string label, string key, IReadOnlyList<decimal> observed)
        {
            if (Spread(observed) is not { } spread)
            {
                return;
            }

            if (spread.Low == spread.High || spread.Low <= 0 || spread.High / spread.Low > MaxRangeRatio)
            {
                parts.Add($"{label} typically {Pct(spread.Median)}%");
                values.Add(new PackValue(key, Pct(spread.Median), PackValueKind.Percentage));
            }
            else
            {
                parts.Add($"{label} {Pct(spread.Low)}%–{Pct(spread.High)}%");
                values.Add(new PackValue(key + "Low", Pct(spread.Low), PackValueKind.Percentage));
                values.Add(new PackValue(key + "High", Pct(spread.High), PackValueKind.Percentage));
            }
        }

        AddPercent("discount achieved", "discountAchievedPct", deals.Where(d => d.DiscountAchievedPct is not null).Select(d => (decimal)d.DiscountAchievedPct!.Value).ToList());
        AddPercent("annual uplift cap", "upliftCapPct", deals.Where(d => d.UpliftCapPct is not null).Select(d => (decimal)d.UpliftCapPct!.Value).ToList());

        if (TypicalNoticeDays(deals) is { } days)
        {
            parts.Add($"notice period typically {days.ToString(CultureInfo.InvariantCulture)} days");
            values.Add(new PackValue("noticeDays", days.ToString(CultureInfo.InvariantCulture), PackValueKind.Number));
        }

        var term = deals.GroupBy(d => d.TermMonths).OrderByDescending(g => g.Sum(d => d.SampleSize)).First().Key;
        parts.Add($"term typically {term.ToString(CultureInfo.InvariantCulture)} months");
        values.Add(new PackValue("termMonths", term.ToString(CultureInfo.InvariantCulture), PackValueKind.Number));

        if (deals.Where(d => !string.IsNullOrWhiteSpace(d.PaymentTerms))
                .GroupBy(d => d.PaymentTerms!.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Sum(d => d.SampleSize))
                .FirstOrDefault() is { } payment)
        {
            parts.Add($"payment terms typically {payment.Key}");
        }

        var sample = deals.Sum(d => d.SampleSize);
        return new PackItem(
            $"market:terms:{contractId}",
            PackCorpus.Market,
            $"{supplierName} · what comparable customers negotiated",
            "market practice · not from your contract",
            null,
            null,
            $"Market practice among comparable {supplierName} deals (sample of {sample.ToString(CultureInfo.InvariantCulture)}), " +
            $"not terms of your contract: {string.Join(", ", parts)}.",
            null,
            null,
            null,
            "market practice · representative market data",
            values,
            contractId.ToString());
    }

    /// <summary>The notice period comparable customers have (median days), or <see langword="null"/>
    /// when no deal records one.</summary>
    public static int? TypicalNoticeDays(IReadOnlyList<MarketDeal> deals)
    {
        ArgumentNullException.ThrowIfNull(deals);

        return Spread(deals.Where(d => d.NoticeDays is not null).Select(d => (decimal)d.NoticeDays!.Value).ToList()) is { } notice
            ? decimal.ToInt32(Math.Round(notice.Median, 0, MidpointRounding.AwayFromZero))
            : null;
    }

    /// <summary>The deals about the contract's own products when any match by name (a supplier
    /// sells many products at very different values), else all of them.</summary>
    public static IReadOnlyList<MarketDeal> MatchingProducts(IReadOnlyList<MarketDeal> deals, IReadOnlyList<PricedLine> pricedLines)
    {
        ArgumentNullException.ThrowIfNull(deals);
        ArgumentNullException.ThrowIfNull(pricedLines);

        var lineWords = pricedLines
            .SelectMany(line => Words(line.Description).Concat(line.Sku is null ? [] : Words(line.Sku)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (lineWords.Count == 0)
        {
            return deals;
        }

        var matching = deals
            .Where(d => Words(d.Product).Concat(d.Sku is null ? [] : Words(d.Sku)).Any(lineWords.Contains))
            .ToList();
        return matching.Count > 0 ? matching : deals;
    }

    /// <summary>
    /// The Italian or English lead the deterministic proposal opens with: the gaps a proposal can
    /// safely name for any commercial question — the annual amounts, the end date, the notice
    /// deadline — per contract, honestly, then what the market says in their place, labelled as
    /// estimates. <see langword="null"/> when none of those is missing.
    /// </summary>
    public static string? Lead(IReadOnlyList<ContractCheck> checks, bool italian)
    {
        ArgumentNullException.ThrowIfNull(checks);

        string[] leadFields = [Field.AnnualSpend, Field.EndDate, Field.NoticeDeadline];
        var gapped = checks
            .Select(c => (Check: c, Fields: c.Missing.Where(f => leadFields.Contains(f)).ToList()))
            .Where(x => x.Fields.Count > 0)
            .Take(3)
            .ToList();
        if (gapped.Count == 0)
        {
            return null;
        }

        string JoinFields(IReadOnlyList<string> fields)
        {
            var labels = fields.Select(f => Field.Label(f, italian)).ToList();
            return labels.Count == 1
                ? labels[0]
                : $"{string.Join(", ", labels.Take(labels.Count - 1))} {(italian ? "e" : "and")} {labels[^1]}";
        }

        var stands = new List<string>();
        foreach (var (check, fields) in gapped)
        {
            var subject = gapped.Count > 1 ? (italian ? $"per {check.SupplierName} " : $"for {check.SupplierName}, ") : string.Empty;

            if (fields.Contains(Field.AnnualSpend) && check.Estimate is { } estimate)
            {
                var range = estimate.Low is { } low
                    ? italian
                        ? $"tra {Money(estimate.Currency, low)} e {Money(estimate.Currency, estimate.High)}"
                        : $"between {Money(estimate.Currency, low)} and {Money(estimate.Currency, estimate.High)}"
                    : italian
                        ? $"sotto {Money(estimate.Currency, estimate.High)}"
                        : $"below {Money(estimate.Currency, estimate.High)}";
                var who = estimate.CompanySizeBand is { } band
                    ? italian ? $"per aziende di {band} dipendenti" : $"for companies of {band} employees"
                    : italian ? "per clienti simili" : "for similar customers";
                stands.Add(italian ? $"{subject}{who} il valore annuo tipico è {range}" : $"{subject}{who} the typical annual value is {range}");
            }

            if (fields.Contains(Field.NoticeDeadline) && check.TypicalNoticeDays is { } days)
            {
                var n = days.ToString(CultureInfo.InvariantCulture);
                stands.Add(check.EstimatedNoticeDeadline is { } deadline
                    ? italian
                        ? $"{subject}con il preavviso tipico di clienti simili ({n} giorni) la disdetta andrebbe inviata entro il {Iso(deadline)}"
                        : $"{subject}with the notice comparable customers give ({n} days) notice would be due by {Iso(deadline)}"
                    : italian
                        ? $"{subject}clienti simili hanno un preavviso tipico di {n} giorni"
                        : $"{subject}comparable customers typically give {n} days' notice");
            }
        }

        string gapsSentence;
        if (gapped.Count == 1)
        {
            var (check, fields) = gapped[0];
            gapsSentence = italian
                ? $"Sul contratto {check.SupplierName} {(fields.Count > 1 || Field.IsPluralInItalian(fields[0]) ? "mancano" : "manca")} {JoinFields(fields)}."
                : $"The {check.SupplierName} contract has no {JoinFields(fields).Replace("the ", string.Empty, StringComparison.Ordinal)} on file.";
        }
        else
        {
            var list = string.Join(", ", gapped.Select(x => $"{x.Check.SupplierName} ({JoinFields(x.Fields)})"));
            gapsSentence = italian ? $"Su alcuni contratti mancano dei dati: {list}." : $"Some contracts are missing data: {list}.";
        }

        if (stands.Count == 0)
        {
            return gapsSentence + (italian
                ? " I dati di mercato non bastano per una stima affidabile, quindi la risposta si basa su quello che c'è.\n\n"
                : " The market data is not precise enough for a reliable estimate, so the answer rests on what is on file.\n\n");
        }

        var owner = gapped.Count > 1 ? (italian ? "dei tuoi contratti" : "from your contracts") : (italian ? "del tuo contratto" : "from your contract");
        var closing = stands.Count > 1
            ? italian ? $"sono stime, non dati {owner}" : $"these are estimates, not figures {owner}"
            : italian ? $"è una stima, non un dato {owner}" : $"that is an estimate, not a figure {owner}";

        return italian
            ? $"{gapsSentence} Dai dati di mercato, {string.Join("; ", stands)}: {closing}.\n\n"
            : $"{gapsSentence} From market data, {string.Join("; ", stands)}: {closing}.\n\n";
    }

    /// <summary>The honest lead for a clause question about a contract whose validated clauses hold
    /// nothing on it — said before the way to find it.</summary>
    public static string ClauseNotFoundLead(string supplierName, bool italian) => italian
        ? $"Tra le clausole validate del contratto {supplierName} non trovo questo punto.\n\n"
        : $"I can't find this point among the validated clauses of the {supplierName} contract.\n\n";

    private static AnnualEstimate? FromQuantities(string currency, IReadOnlyList<PricedLine> pricedLines)
    {
        var lines = pricedLines
            .Where(line => line.Quantity is > 0 && line.Benchmark is not null &&
                (line.Currency is null || string.Equals(line.Currency, currency, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (lines.Count == 0)
        {
            return null;
        }

        var low = lines.Sum(line => line.Quantity!.Value * line.Benchmark!.P25);
        var high = lines.Sum(line => line.Quantity!.Value * line.Benchmark!.P75);
        if (low <= 0 || high / low > MaxRangeRatio)
        {
            return null;
        }

        return new AnnualEstimate(
            Math.Round(low, 0, MidpointRounding.AwayFromZero),
            Math.Round(high, 0, MidpointRounding.AwayFromZero),
            currency,
            AnnualEstimateBasis.QuantitiesAtMarketPrices,
            null,
            null,
            lines.Min(line => line.SampleSize ?? 0));
    }

    private static AnnualEstimate? FromComparableContractValue(
        string currency, IReadOnlyList<PricedLine> pricedLines, IReadOnlyList<MarketDeal> deals)
    {
        // A unit price that sits inside (or close to) the deal's own annual-value band is the
        // yearly value of the whole contract (an insurance premium, a facilities contract), not a
        // per-seat price.
        var deal = MatchingProducts(SameCurrency(deals, currency), pricedLines)
            .Where(d => d.UnitPriceP25 > 0 && ParseBand(d.AnnualValueBand) is { } band &&
                d.UnitPriceP25 >= Math.Max(band.Low ?? 0m, 20_000m) * 0.5m &&
                (band.High is not { } high || d.UnitPriceP75 <= high * 1.5m) &&
                d.UnitPriceP75 / d.UnitPriceP25 <= MaxRangeRatio)
            .OrderByDescending(d => d.SampleSize)
            .FirstOrDefault();

        return deal is null
            ? null
            : new AnnualEstimate(
                deal.UnitPriceP25,
                deal.UnitPriceP75,
                deal.Currency,
                AnnualEstimateBasis.ComparableContractValue,
                deal.Product,
                deal.CompanySizeBand,
                deal.SampleSize);
    }

    private static AnnualEstimate? FromValueBand(
        string currency, IReadOnlyList<PricedLine> pricedLines, IReadOnlyList<MarketDeal> deals)
    {
        var candidates = MatchingProducts(SameCurrency(deals, currency), pricedLines);
        if (candidates.Count == 0)
        {
            return null;
        }

        // The band most comparable deals (weighted by sample) fall in — only when it is a clear
        // majority, so a supplier selling very different products never yields a made-up middle.
        var total = candidates.Sum(d => Math.Max(d.SampleSize, 1));
        var modal = candidates
            .GroupBy(d => d.AnnualValueBand, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Band: g.Key, Weight: g.Sum(d => Math.Max(d.SampleSize, 1)), Deals: g.ToList()))
            .OrderByDescending(g => g.Weight)
            .First();

        if (modal.Weight * 2 < total || ParseBand(modal.Band) is not { High: { } high } band)
        {
            return null;
        }

        if (band.Low is { } low && (low <= 0 || high / low > MaxRangeRatio))
        {
            return null;
        }

        var product = modal.Deals.Select(d => d.Product).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
            ? modal.Deals[0].Product
            : null;
        var sizeBand = modal.Deals
            .GroupBy(d => d.CompanySizeBand, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(d => d.SampleSize))
            .First().Key;

        return new AnnualEstimate(
            band.Low,
            high,
            modal.Deals[0].Currency,
            AnnualEstimateBasis.ComparableValueBand,
            product,
            sizeBand,
            modal.Weight);
    }

    private static IReadOnlyList<MarketDeal> SameCurrency(IReadOnlyList<MarketDeal> deals, string currency) =>
        deals.Where(d => string.Equals(d.Currency, currency, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>The deal's annual value band as amounts (<see cref="MarketValueBand.Parse"/>).</summary>
    internal static (decimal? Low, decimal? High)? ParseBand(string band) => MarketValueBand.Parse(band);

    /// <summary>Interquartile range and median of <paramref name="observed"/> (the full range for
    /// fewer than four observations); <see langword="null"/> for none.</summary>
    private static (decimal Low, decimal Median, decimal High)? Spread(IReadOnlyList<decimal> observed)
    {
        if (observed.Count == 0)
        {
            return null;
        }

        var sorted = observed.Order().ToList();
        decimal At(double q)
        {
            var position = q * (sorted.Count - 1);
            var lower = (int)Math.Floor(position);
            var upper = (int)Math.Ceiling(position);
            return sorted[lower] + (sorted[upper] - sorted[lower]) * (decimal)(position - lower);
        }

        return sorted.Count < 4
            ? (sorted[0], At(0.5), sorted[^1])
            : (At(0.25), At(0.5), At(0.75));
    }

    private static IEnumerable<string> Words(string text) =>
        Word.Matches(text).Select(m => m.Value.ToLowerInvariant());

    private static string ProductSuffix(string? product) => product is null ? string.Empty : $" for {product}";

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Money(string currency, decimal value) =>
        $"{currency} {Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture)}";

    private static string Amount(decimal value) =>
        Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

    private static string Pct(decimal value) =>
        Math.Round(value, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture);
}
