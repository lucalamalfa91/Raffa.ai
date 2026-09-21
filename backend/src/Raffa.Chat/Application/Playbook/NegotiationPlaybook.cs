using Raffa.Chat.Application.Pack;

namespace Raffa.Chat.Application.Playbook;

/// <summary>Spend categories the playbook is indexed by (matched to the market corpus's own
/// <c>Category</c> strings, fallback <see cref="Generic"/>).</summary>
public enum PlaybookCategory
{
    Generic,
    Saas,
    Cloud,
    Consulting,
    Telco,
    Insurance,
    Logistics,
    Facility,
}

/// <summary>The lever family a playbook entry serves; the string form matches
/// <c>Raffa.Insights.Savings.SavingsLeverType</c> member names so the composition root can select
/// entries by the levers a contract actually grounds without a project reference.</summary>
public enum PlaybookLever
{
    MarketDiscount,
    AboveBandRepricing,
    MultiYearTerm,
    UpliftCap,
    NoticeTiming,
    PaymentTerms,
    VolumeFlexibility,
    Alternatives,
    Bundle,
    QuarterEnd,
}

public sealed record PlaybookEntry(
    string Key,
    PlaybookCategory Category,
    PlaybookLever Lever,
    string Title,
    string Tactic,
    string WhenToUse,
    string WhatToAsk);

/// <summary>
/// Raffa's own curated negotiation know-how — the tactics a senior buyer brings to a renewal,
/// written as citable pack items (corpus <see cref="PackCorpus.Raffa"/>). Deliberately free of
/// digits: the numbers in an answer must come from the tenant's facts, the market corpus or the
/// calculators, never from a generic playbook, so these entries can never trip the numeric guard
/// or lend a made-up figure an air of evidence.
/// </summary>
public static class NegotiationPlaybook
{
    public const string Provenance = "Raffa negotiation playbook";

    public static IReadOnlyList<PlaybookEntry> All { get; } =
    [
        // --- Generic, any category ---
        new("anchor-on-market", PlaybookCategory.Generic, PlaybookLever.MarketDiscount,
            "Anchor the ask on what peers paid",
            "Open with the market median as the reference price, not with the supplier's list price or last year's invoice. The supplier then negotiates up from the market, not down from their number.",
            "Whenever a market band or a peer discount exists for the product.",
            "\"Comparable customers closed this at the market median with a discount; we expect at least the same on the renewal baseline.\""),
        new("competitive-alternative", PlaybookCategory.Generic, PlaybookLever.Alternatives,
            "Make the alternative credible",
            "A renewal only moves when the supplier believes you can walk. Run a light RFI with one alternative, name it, and keep a dated migration estimate on the table even if you intend to stay.",
            "When the notice window is still open and the product has substitutes.",
            "\"We are evaluating an alternative for this scope; to renew we need the price to close the gap.\""),
        new("time-the-quarter", PlaybookCategory.Generic, PlaybookLever.QuarterEnd,
            "Close in the supplier's quarter end",
            "Sales teams discount most in the last weeks of their fiscal quarter and year. Align your signature date to their quarter end, not yours, and let the rep know the deal can close by then.",
            "When the notice deadline leaves room to choose the closing date.",
            "\"We can sign before your quarter closes if the pricing reflects it.\""),
        new("notice-reservation", PlaybookCategory.Generic, PlaybookLever.NoticeTiming,
            "Reserve the right not to renew, in writing",
            "Send a formal notice reservation before the deadline: it stops the auto-renewal clock, costs nothing, and moves the leverage to your side for the whole negotiation.",
            "Any auto-renewing contract whose notice deadline has not passed.",
            "\"This is our notice that we do not accept automatic renewal on current terms; we remain open to renewing on revised terms.\""),
        new("uplift-cap", PlaybookCategory.Generic, PlaybookLever.UpliftCap,
            "Cap every increase before signing",
            "An uncapped indexation or uplift clause is a silent price rise every year. Cap it at a small fixed percentage or freeze the price for the term; peers routinely obtain a cap.",
            "Any contract with an increase, indexation or CPI clause.",
            "\"Any annual increase is capped at a fixed percentage and applies only from the second year.\""),
        new("multi-year-for-price", PlaybookCategory.Generic, PlaybookLever.MultiYearTerm,
            "Sell the term, never give it away",
            "A longer commitment is the supplier's most valued concession. Trade it only against a lower unit price, a price lock for the whole term and a termination-for-convenience window.",
            "When the market prices longer terms lower and the product is strategic.",
            "\"We will commit to a longer term only with the longer-term unit price, a locked price and an exit at the midpoint.\""),
        new("true-down", PlaybookCategory.Generic, PlaybookLever.VolumeFlexibility,
            "Pay only for what you use",
            "Committed volumes without a true-down right mean paying for shelfware. Bring the usage report and ask for the right to reduce quantities at renewal or annually without penalty.",
            "Seat, licence or capacity based contracts with committed quantities.",
            "\"Quantities can be reduced at each anniversary to actual usage, without penalty, and unused units are credited.\""),
        new("payment-terms-for-price", PlaybookCategory.Generic, PlaybookLever.PaymentTerms,
            "Trade payment terms against price",
            "Suppliers value cash timing. Offer faster payment for a discount, or take longer terms as a concession when price will not move further.",
            "At the end of the round, when the price is close to target.",
            "\"We can pay upfront for the year in exchange for an additional discount; otherwise we expect longer terms.\""),
        new("bundle-and-unbundle", PlaybookCategory.Generic, PlaybookLever.Bundle,
            "Unbundle what you do not use, bundle what you buy elsewhere",
            "Line by line, drop modules with no usage and fold into the renewal the adjacent products you buy from the same supplier under separate order forms: one bigger deal, one better price.",
            "Suppliers with several order forms or modules in the portfolio.",
            "\"We consolidate these order forms into one renewal at the consolidated volume price, and remove the unused modules.\""),
        new("reprice-above-band", PlaybookCategory.Generic, PlaybookLever.AboveBandRepricing,
            "Re-price the lines above the market band first",
            "Do not negotiate a blanket discount: name the specific lines priced above the market median and ask for each to be brought to the median. Line-level asks are hard to refuse.",
            "When benchmark bands exist for the priced lines.",
            "\"These lines are above the market median; we expect them re-priced to the median before we discuss the rest.\""),

        // --- SaaS ---
        new("saas-seat-rightsize", PlaybookCategory.Saas, PlaybookLever.VolumeFlexibility,
            "Right-size seats to active users",
            "Pull the admin console's active-user report. Renew on active seats plus a small growth buffer, with a ramp for any expansion instead of paying for the full year upfront.",
            "Per-seat SaaS with utilisation below the committed count.",
            "\"We renew on active seats with a ramp for growth; new seats are added at the same unit price during the term.\""),
        new("saas-edition-downgrade", PlaybookCategory.Saas, PlaybookLever.Bundle,
            "Match the edition to the features used",
            "Enterprise editions are sold on features most teams never enable. List the features actually used and price the lower edition for the users who do not need the top tier.",
            "SaaS with tiered editions.",
            "\"Move the users who do not use the premium features to the lower edition at its unit price.\""),
        new("saas-renewal-price-protection", PlaybookCategory.Saas, PlaybookLever.UpliftCap,
            "Renewal price protection",
            "Write the renewal price into the order form now: same unit price at the next renewal, expansion at the same price, and a most-favoured-customer clause if the list price drops.",
            "Any SaaS renewal.",
            "\"Renewal and expansion pricing is fixed at today's unit price for the next term.\""),
        new("saas-coterm", PlaybookCategory.Saas, PlaybookLever.Bundle,
            "Co-term everything to one date",
            "Several order forms with different end dates are several small negotiations. Co-term them to one renewal date and negotiate the whole spend at once.",
            "Multiple order forms with the same supplier.",
            "\"All order forms are co-termed to a single renewal date at the consolidated price.\""),

        // --- Cloud ---
        new("cloud-commit-discount", PlaybookCategory.Cloud, PlaybookLever.MultiYearTerm,
            "Commit spend, not products",
            "Cloud providers discount committed annual spend across services far more than any single service. Commit at a level you already exceed and keep the flexibility of which services consume it.",
            "Cloud consumption contracts.",
            "\"We commit to an annual spend we already run and expect the corresponding programme discount on every service.\""),
        new("cloud-rightsize", PlaybookCategory.Cloud, PlaybookLever.AboveBandRepricing,
            "Right-size before you negotiate",
            "Idle instances, over-provisioned storage and unattached volumes are savings that need no negotiation. Clean them first so the commitment you negotiate is the real baseline.",
            "Before any cloud renewal round.",
            "Internal: run the provider's own right-sizing recommendations and act on them before the commercial round."),

        // --- Consulting ---
        new("consulting-rate-card", PlaybookCategory.Consulting, PlaybookLever.AboveBandRepricing,
            "Negotiate the rate card, not the project",
            "Fix role-based day rates for the term, cap the seniority mix, and require named resources; a project price hides rates that a rate card exposes.",
            "Time-and-materials or framework consulting agreements.",
            "\"We sign a fixed rate card per role for the term, with a maximum share of senior roles per engagement.\""),
        new("consulting-outcome", PlaybookCategory.Consulting, PlaybookLever.PaymentTerms,
            "Tie part of the fee to outcomes",
            "Move part of the fee to milestones or measurable outcomes: it lowers the certain spend and aligns the supplier with the result.",
            "Consulting engagements with a definable deliverable.",
            "\"A share of the fee is paid on acceptance of the milestones, not on time spent.\""),

        // --- Telco ---
        new("telco-benchmark-rerate", PlaybookCategory.Telco, PlaybookLever.AboveBandRepricing,
            "Re-rate the tariff to today's market",
            "Telco tariffs drop every year while contracts keep last year's rate. Ask for a re-rate to the current market tariff mid-term and at renewal, with a benchmark clause for the future.",
            "Telecommunication and connectivity contracts.",
            "\"Tariffs are re-rated to the current market at each anniversary; a benchmark clause allows a review on request.\""),

        // --- Insurance ---
        new("insurance-remarket", PlaybookCategory.Insurance, PlaybookLever.Alternatives,
            "Remarket the policy before renewal",
            "Insurers price incumbency. A broker-led remarketing of the policy before the renewal date typically moves the premium even if you stay.",
            "Insurance policies approaching renewal.",
            "\"We are remarketing the policy; to retain it we need the premium aligned to the best quotation received.\""),

        // --- Logistics ---
        new("logistics-volume-tiers", PlaybookCategory.Logistics, PlaybookLever.VolumeFlexibility,
            "Tiered rates with retroactive volume discounts",
            "Ask for rate tiers that apply retroactively once a volume threshold is passed, and a fuel or index surcharge cap.",
            "Freight and logistics agreements.",
            "\"Rates step down retroactively at each volume tier; surcharges are capped for the term.\""),

        // --- Facility ---
        new("facility-scope-audit", PlaybookCategory.Facility, PlaybookLever.Bundle,
            "Audit the scope before the price",
            "Facility contracts accrete scope. Audit the service lines against what is actually delivered and remove or re-price what is not.",
            "Facility management and services contracts.",
            "\"The renewal covers the audited scope only; removed service lines are credited from the baseline.\""),
    ];

    /// <summary>
    /// Selects up to <paramref name="max"/> entries for <paramref name="category"/>: first the
    /// entries serving the levers the contract actually grounds (in <paramref name="leverNames"/>
    /// order — the calculator's own ranking), then the category's own entries, then generic ones;
    /// never a duplicate.
    /// </summary>
    public static IReadOnlyList<PlaybookEntry> Select(PlaybookCategory category, IEnumerable<string> leverNames, int max)
    {
        ArgumentNullException.ThrowIfNull(leverNames);

        var wanted = leverNames
            .Select(name => Enum.TryParse<PlaybookLever>(name, ignoreCase: true, out var lever) ? lever : (PlaybookLever?)null)
            .Where(l => l is not null)
            .Select(l => l!.Value)
            .Distinct()
            .ToList();

        var picked = new List<PlaybookEntry>();

        foreach (var lever in wanted)
        {
            var match = All.FirstOrDefault(e => e.Lever == lever && e.Category == category)
                ?? All.FirstOrDefault(e => e.Lever == lever && e.Category == PlaybookCategory.Generic);
            if (match is not null && !picked.Contains(match))
            {
                picked.Add(match);
            }
        }

        foreach (var entry in All.Where(e => e.Category == category).Concat(All.Where(e => e.Category == PlaybookCategory.Generic)))
        {
            if (picked.Count >= max)
            {
                break;
            }

            if (!picked.Contains(entry))
            {
                picked.Add(entry);
            }
        }

        return picked.Take(max).ToList();
    }

    /// <summary>Maps a market-corpus category string ("Enterprise Software", "Cloud
    /// Infrastructure", "Professional Services"...) onto a playbook category.</summary>
    public static PlaybookCategory CategoryFrom(string? marketCategory)
    {
        if (string.IsNullOrWhiteSpace(marketCategory))
        {
            return PlaybookCategory.Generic;
        }

        var c = marketCategory.ToLowerInvariant();
        if (c.Contains("cloud") || c.Contains("infrastructure") || c.Contains("hosting"))
        {
            return PlaybookCategory.Cloud;
        }

        if (c.Contains("software") || c.Contains("saas") || c.Contains("collaboration") || c.Contains("productivity") || c.Contains("crm") || c.Contains("hr"))
        {
            return PlaybookCategory.Saas;
        }

        if (c.Contains("consult") || c.Contains("professional") || c.Contains("advisory") || c.Contains("audit"))
        {
            return PlaybookCategory.Consulting;
        }

        if (c.Contains("telco") || c.Contains("telecom") || c.Contains("connectivity") || c.Contains("mobile"))
        {
            return PlaybookCategory.Telco;
        }

        if (c.Contains("insur"))
        {
            return PlaybookCategory.Insurance;
        }

        if (c.Contains("logist") || c.Contains("freight") || c.Contains("transport") || c.Contains("shipping"))
        {
            return PlaybookCategory.Logistics;
        }

        if (c.Contains("facilit") || c.Contains("catering") || c.Contains("cleaning") || c.Contains("real estate"))
        {
            return PlaybookCategory.Facility;
        }

        return PlaybookCategory.Generic;
    }

    public static PackItem ToPackItem(PlaybookEntry entry) =>
        new(
            $"raffa:playbook:{entry.Key}",
            PackCorpus.Raffa,
            entry.Title,
            $"{entry.Category} · {entry.Lever}",
            null,
            null,
            $"{entry.Tactic} When to use: {entry.WhenToUse} Ask: {entry.WhatToAsk}",
            null,
            null,
            null,
            Provenance,
            []);
}
