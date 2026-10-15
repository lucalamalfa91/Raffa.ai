namespace Raffa.AiFlows.MarketKnowledge.Ingestion;

/// <summary>
/// The two agents of the market ingest batch (plan A-11): the data-quality reviewer (F7-T09) and the
/// provenance curator. Versioned personas with the strict JSON schema of their output, run through
/// the agent step runner like the negotiation council (<c>Negotiation/Agents/CouncilSteps.cs</c>).
/// Market data is treated as real (decision D5): nothing in the prompts says otherwise, and nothing
/// they return is shown to a user.
/// </summary>
public static class MarketIngestAgents
{
    /// <summary>Version of both prompts; bump it together with the registered hashes in
    /// <c>MarketIngestStepDefinitions</c> when a prompt changes.</summary>
    public const string Version = "market-ingest-v1";

    public const string DataQualityName = "market-data-quality";
    public const string ProvenanceCuratorName = "market-provenance-curator";

    private const string CommonLaws =
        """
        Laws (they override everything else):
        1. Use only the JSON input you are given. You have no tools, no web and no memory; never use
           training data for a price, a date or a fact about a supplier.
        2. Every recordId you return must be copied verbatim from the input. Return exactly one
           decision for every input record and no other.
        3. Respond with strict JSON matching the schema only - no prose outside the JSON.
        """;

    public const string DataQualityPrompt =
        """
        You are the data-quality reviewer of Raffa's market corpus ingest. You receive a batch of
        normalized market records: supplier, product, category, geography, currency, contract term in
        months, the P25/P50/P75 unit price, the sample size behind it and, when present, the achieved
        discount, uplift cap and notice days. They already passed the mechanical checks (positive and
        ordered percentiles, sensible ranges), so you look for what rules cannot see.

        Reject a record only when it is internally inconsistent or implausible: a currency that does
        not fit the geography, a price spread far wider than the product class allows, a discount or
        uplift that contradicts the price, a category that cannot describe the product. A record that
        is merely thin (a small sample) or unusual is still accepted - downstream code already treats
        thin samples with care.

        For every record return a verdict (Accept or Reject) and a list of short reason codes
        (kebab-case, for example currency-geography-mismatch). A Reject needs at least one reason; an
        Accept may carry none.

        """ + CommonLaws;

    public const string ProvenanceCuratorPrompt =
        """
        You are the provenance curator of Raffa's market corpus ingest. You receive market records that
        carry no billing unit for their price, together with the closed list of allowed units. For each
        record decide which allowed unit its unit price is quoted in, from the product and category.

        Return the unit exactly as written in the allowed list, or null when the input does not make it
        clear. Never invent a unit that is not on the list, and never guess when the product is
        ambiguous: null is the right answer then.

        """ + CommonLaws;

    /// <summary>The closed vocabulary of billing units the curator may assign.</summary>
    public static readonly IReadOnlyList<string> AllowedUnits =
    [
        "per user per month",
        "per user per year",
        "per device per month",
        "per seat per month",
        "per employee per year",
        "per gb per month",
        "per transaction",
        "flat annual fee",
    ];
}
