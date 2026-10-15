using Raffa.AiFlows.Shared.Audit;
using System.Globalization;
using Raffa.AiFlows.Ask.Gate;
using Raffa.AiFlows.Ask.Interview;
using Raffa.AiFlows.Ask.Planning;
using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.Negotiation.Verdict;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Reply;
using Raffa.AiFlows.Shared.Routing;
using Raffa.AiFlows.WebResearch.Orchestration;
using Raffa.Benchmark.Contracts;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Reply;
using Raffa.Insights.Contracts;
using Raffa.Insights.Savings;
using Raffa.Market.Contracts;
using Raffa.Market.Retrieval;
using Raffa.Documents.Contracts.Application;
using Raffa.Insights.Application;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using static Raffa.AiFlows.Ask.Packs.ContractResolution;

namespace Raffa.AiFlows.Ask.Packs;

/// <summary>
/// Pure builders of <see cref="PackItem"/>s and the number formatters they share. Moved verbatim
/// from <see cref="AskCopilotService"/> (F1 Ask split, PR-1).
/// </summary>
internal static class PackItemFactory
{
    /// <summary>
    /// Task E25/F02/US01/T01 (NW-55): still the pre-existing <c>/contracts/{id}</c> CTA and a
    /// <see langword="null"/> <see cref="PackItem.PreviewUrl"/> -- deliberately untouched by this
    /// task. A contract-level fact (e.g. "ends on 2027-01-01") has no single source page: it is
    /// computed from <see cref="PortfolioListItem"/> columns, which name no
    /// <c>SourceDocumentId</c>/<c>SourcePage</c> at all, so there is nothing here for
    /// <see cref="AskCopilotService.ResolveTenantClauseLinks"/>'s viewer link to resolve against. AC-3 ("PreviewUrl is
    /// set only for tenant <em>pages</em>") already reads this item as correctly page-less, not as a
    /// gap: a contract route is still a valid <see cref="PackItem.Href"/> shape for
    /// <see cref="PackCorpus.Tenant"/> per that field's own doc comment ("a document/contract route
    /// for PackCorpus.Tenant").
    /// </summary>
    internal static PackItem BuildContractFactItem(PortfolioListItem item, string displayName)
    {
        var values = new List<PackValue>();
        if (item.EndDate is { } endDate)
        {
            values.Add(new PackValue("endDate", endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PackValueKind.Date));
        }

        if (item.CancellationDeadline is { } deadline)
        {
            values.Add(new PackValue("cancellationDeadline", deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PackValueKind.Date));
        }

        // The real contract currency, never "n/a": NumericGuard matches an amount only against a
        // pack value of the same currency, so "n/a" forced the model into bare "667000.00" figures
        // (closes the golden set's GAP-ASK-SPEND-CURRENCY-NA).
        var currency = string.IsNullOrWhiteSpace(item.Currency) ? "n/a" : item.Currency.Trim();
        if (item.AnnualSpend is { } spend)
        {
            values.Add(new PackValue("annualSpend", spend.ToString(CultureInfo.InvariantCulture), PackValueKind.Amount, currency));
        }

        var title = ContractTitle(item, displayName);
        var spendSentence = item.AnnualSpend is { } annualSpend && currency != "n/a"
            ? $" Annual spend {currency} {annualSpend.ToString("0.##", CultureInfo.InvariantCulture)}."
            : string.Empty;

        var snippet = item.EndDate is { } end
            ? $"{title} ends on {end:yyyy-MM-dd}" +
              (item.AutoRenewal ? ", auto-renews unless notice is given" : ", does not auto-renew") +
              (item.CancellationDeadline is { } cd ? $" (notice by {cd:yyyy-MM-dd})" : string.Empty) + "." + spendSentence
            : $"{title} has no validated end date yet.{spendSentence}";

        return new PackItem(
            $"fact:{item.ContractId}:renewal",
            PackCorpus.Tenant,
            title,
            null,
            null,
            null,
            snippet,
            $"/contracts/{item.ContractId}",
            null,
            null,
            "validated contract",
            values,
            item.ContractId.ToString());
    }

    internal static IReadOnlyList<PackValue> BuildDateValues(DateOnly? renewalDate, DateOnly? cancellationDeadline)
    {
        var values = new List<PackValue>();
        if (renewalDate is { } renewal)
        {
            values.Add(new PackValue("renewalDate", renewal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PackValueKind.Date));
        }

        if (cancellationDeadline is { } deadline)
        {
            values.Add(new PackValue("cancellationDeadline", deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PackValueKind.Date));
        }

        return values;
    }

    internal static PackItem ToMarketPackItem(MarketNote note) =>
        new(
            $"market:{note.RecordId}",
            PackCorpus.Market,
            note.Title,
            note.Provenance,
            null,
            null,
            note.Snippet,
            null,
            null,
            note.RecordId,
            note.Provenance,
            []);

    /// <summary>
    /// One <see cref="PackCorpus.Market"/> item for a benchmarked priced line -- a representative
    /// band (P25/P50/P75 + provenance) when one resolved, else an honest "insufficient market data"
    /// entry, never a silently dropped line (AC-3, task E28/F01/US01/T01, NW-82). Shared by
    /// <see cref="AskCopilotService.BuildMarketComparePackAsync"/> and <see cref="AskCopilotService.BuildRenewalStrategyPackAsync"/>
    /// (task E31/F01/US01/T01, NW-95) so the two packs can never narrate two different wordings for
    /// the identical line's missing band.
    /// </summary>
    internal static PackItem BuildMarketPricedLineItem(string supplierName, PricedLine line)
    {
        var lineKey = $"market:{supplierName}:{line.Description}".Replace(' ', '-');

        if (line.Benchmark is not { } distribution)
        {
            return new PackItem(
                lineKey,
                PackCorpus.Market,
                $"{supplierName} · {line.Description}",
                null,
                null,
                null,
                "Insufficient market data for this line — too few comparables to publish a " +
                "benchmark (Appendix C rule 10; ADR-001).",
                null,
                null,
                null,
                "insufficient market data",
                []);
        }

        return new PackItem(
            lineKey,
            PackCorpus.Market,
            $"{supplierName} · {line.Description}",
            line.AdapterName,
            null,
            null,
            $"P25 {distribution.P25} · P50 {distribution.P50} · P75 {distribution.P75} " +
            $"{line.Currency}/unit · n = {line.SampleSize?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}",
            null,
            null,
            null,
            FormatRepresentativeProvenance(line),
            [
                new PackValue("p25", distribution.P25.ToString(CultureInfo.InvariantCulture), PackValueKind.Amount, line.Currency),
                new PackValue("p50", distribution.P50.ToString(CultureInfo.InvariantCulture), PackValueKind.Amount, line.Currency),
                new PackValue("p75", distribution.P75.ToString(CultureInfo.InvariantCulture), PackValueKind.Amount, line.Currency),
            ]);
    }

    /// <summary>
    /// One <see cref="PackCorpus.Raffa"/> citation item for a capability (AC-2 "one raffa Renewals
    /// item", task E31/F01/US01/T01, NW-95) -- the same <see cref="FeatureCitation"/> shape
    /// <see cref="BuildFeatureCitations"/> already cites directly into a deterministic reply
    /// (<see cref="GateReplyBuilder.BuildCapabilityReply"/>), but as a genuine <see cref="PackItem"/> here so the
    /// `answer` role can cite it like any other pack source instead of it being injected out of
    /// band. <see langword="null"/> for an unrecognized key -- never fabricated (Appendix C rule
    /// 10) -- though every key this file passes is one of <see cref="CapabilityCatalog"/>'s own
    /// constants, so this is not expected in practice.
    /// </summary>
    internal static PackItem? BuildFeatureCitationPackItem(string capabilityKey)
    {
        var capability = CapabilityCatalog.Find(capabilityKey);
        if (capability is null)
        {
            return null;
        }

        var feature = FeatureCitation.For(capability);

        return new PackItem(
            $"raffa:{capabilityKey}",
            PackCorpus.Raffa,
            feature.Title,
            feature.Subtitle,
            null,
            null,
            feature.Snippet,
            feature.Href,
            null,
            null,
            "Raffa feature",
            []);
    }

    internal static IReadOnlyList<ReplyCitation> BuildFeatureCitations(IReadOnlyList<string> keys)
    {
        var citations = new List<ReplyCitation>();
        var n = 1;

        foreach (var key in keys)
        {
            var capability = CapabilityCatalog.Find(key);
            if (capability is null)
            {
                continue;
            }

            var feature = FeatureCitation.For(capability);
            citations.Add(new ReplyCitation(
                n++, feature.Corpus, feature.Title, feature.Subtitle, feature.Snippet,
                null, null, null, null, null, feature.Href, null));
        }

        return citations;
    }

    /// <summary>
    /// <see cref="AskCopilotService.BuildMarketComparePackAsync"/>'s own market-corpus provenance label for a line
    /// whose band resolved (task text, verbatim: "a provenance label (representative · adapter · n
    /// · as-of)") — distinct from, and simpler than,
    /// <c>Raffa.Insights.Strategy.StrategyPackBuilder.AnnotateTargetExplanation</c>'s own
    /// "representative (source: X; n=Y; as of Z)" phrase folded into a target's free-text
    /// explanation (that file is out of this task's "Files to create or modify" scope, so its own
    /// wording is left exactly as it already is); both name the same three facts — adapter, sample
    /// size, as-of date — because ADR-001 w17 clause 4 requires a representative claim to carry
    /// them, never a bare percentile.
    /// </summary>
    internal static string FormatRepresentativeProvenance(PricedLine line) =>
        $"representative · {line.AdapterName} · n={line.SampleSize?.ToString(CultureInfo.InvariantCulture) ?? "n/a"} " +
        $"· as of {(line.AsOf is { } asOf ? asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "n/a")}";

    internal static PackItem BuildMarketDealItem(MarketDeal deal)
    {
        var note = MarketNoteComposer.Compose(deal);
        var values = new List<PackValue>
        {
            new("p25", Amount2(deal.UnitPriceP25), PackValueKind.Amount, deal.Currency),
            new("p50", Amount2(deal.UnitPriceP50), PackValueKind.Amount, deal.Currency),
            new("p75", Amount2(deal.UnitPriceP75), PackValueKind.Amount, deal.Currency),
            new("termMonths", deal.TermMonths.ToString(CultureInfo.InvariantCulture), PackValueKind.Number),
            new("sampleSize", deal.SampleSize.ToString(CultureInfo.InvariantCulture), PackValueKind.Number),
        };

        if (deal.DiscountAchievedPct is { } discount)
        {
            values.Add(new PackValue("discountAchievedPct", Pct((decimal)discount), PackValueKind.Percentage));
        }

        if (deal.UpliftCapPct is { } uplift)
        {
            values.Add(new PackValue("upliftCapPct", Pct((decimal)uplift), PackValueKind.Percentage));
        }

        if (deal.NoticeDays is { } notice)
        {
            values.Add(new PackValue("noticeDays", notice.ToString(CultureInfo.InvariantCulture), PackValueKind.Number));
        }

        return new PackItem(
            InsightsCitationKeys.Market(deal.RecordId),
            PackCorpus.Market,
            note.Title,
            $"{deal.TermMonths} months · {deal.Geography} · {note.Provenance}",
            null, null,
            note.Snippet,
            null, null,
            deal.RecordId,
            note.Provenance,
            values);
    }

    internal static IReadOnlyList<PackItem> DistinctByCitationKey(IEnumerable<PackItem> items)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<PackItem>();
        foreach (var item in items)
        {
            if (seen.Add(item.CitationKey))
            {
                result.Add(item);
            }
        }

        return result;
    }

    internal static string FeasibilityLabel(SavingsFeasibility feasibility) => feasibility switch
    {
        // The council's verdict (CouncilVerdict) reads these labels back: one definition, in Raffa.Chat.
        SavingsFeasibility.Reachable => CouncilVerdict.ReachableLabel,
        SavingsFeasibility.Stretch => CouncilVerdict.StretchLabel,
        SavingsFeasibility.NotSupported => CouncilVerdict.NotSupportedLabel,
        _ => CouncilVerdict.NoTargetLabel,
    };

    internal static string Amount(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

    internal static string Amount2(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    internal static string Pct(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);
}
