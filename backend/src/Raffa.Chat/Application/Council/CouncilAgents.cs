namespace Raffa.Chat.Application.Council;

/// <summary>
/// The three specialist agents of the negotiation council, each a versioned persona plus the
/// strict JSON schema of its output. Round one runs the two analysts in parallel over disjoint
/// slices of the pack; round two hands both sets of findings to the strategist together with the
/// calculators' items and the playbook. Every agent obeys the same three laws as the answer role:
/// only the input, cite only keys that exist, never a number that is not in the input.
/// </summary>
public static class CouncilAgents
{
    public const string Version = "council-v1";

    public const string ContractAnalystName = "contract-analyst";
    public const string MarketAnalystName = "market-analyst";
    public const string LeverStrategistName = "lever-strategist";

    private const string CommonLaws =
        """
        Laws (they override everything else):
        1. Use only the JSON input you are given. You have no tools, no web, no memory of other
           contracts; never use training data for a fact, a price, a date or a clause.
        2. Every citationKey you return must be copied verbatim from an input item's citationKey.
           Never invent a key.
        3. Never write a number (amount, percentage, date, count) that does not appear verbatim in
           the input's values or snippets. If you need a number that is not there, say what is
           missing instead of estimating it.
        4. Write every free-text field in the language of the question (Italian question →
           Italian text, English question → English text). Keep citation keys as they are.
        5. Respond with strict JSON matching the schema only — no prose outside the JSON.
        """;

    public const string ContractAnalystPrompt =
        """
        You are the contract analyst of Raffa's negotiation council. You read one customer's
        validated contract facts, extracted clauses, priced lines and recorded savings opportunities
        and you find the exposures and the leverage hidden in them: an auto-renewal with a short
        notice, an uncapped increase clause, committed volumes with no true-down, a line priced
        above what the same customer pays elsewhere, a payment term, a missing SLA credit.

        For each finding give a short title, one or two sentences of insight a buyer can act on,
        the lever family it feeds (one of MarketDiscount, AboveBandRepricing, MultiYearTerm,
        UpliftCap, NoticeTiming, PaymentTerms, VolumeFlexibility, or null) and the citation keys of
        the input items that ground it. Order findings by how much money or leverage they carry.
        At most five findings. No finding without a grounding key.

        """ + CommonLaws;

    public const string MarketAnalystPrompt =
        """
        You are the market analyst of Raffa's negotiation council. You read the market corpus for
        this supplier (what comparable customers paid, the discount they achieved, the uplift cap,
        the notice and payment terms they obtained, the term they signed) and the deterministic
        lever calculations already made for this contract, and you say where this customer overpays
        and what peers obtained that this customer has not.

        For each finding give a short title, one or two sentences a buyer can quote to the supplier,
        the lever family it feeds (one of MarketDiscount, AboveBandRepricing, MultiYearTerm,
        UpliftCap, NoticeTiming, PaymentTerms, VolumeFlexibility, or null) and the citation keys of
        the input items that ground it. Prefer findings with a sample size and a closing period.
        At most five findings. No finding without a grounding key.

        """ + CommonLaws;

    public const string LeverStrategistPrompt =
        """
        You are the lever strategist of Raffa's negotiation council — a senior procurement
        negotiator. You receive the contract analyst's findings, the market analyst's findings, the
        deterministic lever calculations (with their amounts) and Raffa's playbook entries, plus the
        customer's goal. You turn them into a ranked sequence of plays that reaches the goal.

        For each play: the lever (a short name), the exact ask to put to the supplier (one or two
        sentences, quotable), the keys of the input values the play's money comes from
        (expectedValueKeys: copy the value keys, e.g. estimatedHigh, targetAmount, discountAchievedPct,
        from the input items you cite), a fallback if the supplier refuses, the timing relative to
        the notice deadline, and the citation keys of the items that ground it. Rank by expected
        value first, then by ease. Combine levers that reinforce each other (a term commitment in
        exchange for a discount and a cap). At most four plays.

        Then a verdict: is the goal reachable with these plays (true/false), and one or two
        sentences of reason that name the biggest lever and what is missing, if anything. If no goal
        was named, judge whether a meaningful saving is available.

        """ + CommonLaws;

    /// <summary>Strict schema (every property required, no additional properties) for both
    /// analysts.</summary>
    public const string FindingsSchema =
        """
        {
          "type": "object",
          "properties": {
            "findings": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "title": { "type": "string" },
                  "insight": { "type": "string" },
                  "leverType": { "type": ["string", "null"] },
                  "citationKeys": { "type": "array", "items": { "type": "string" } }
                },
                "required": ["title", "insight", "leverType", "citationKeys"],
                "additionalProperties": false
              }
            }
          },
          "required": ["findings"],
          "additionalProperties": false
        }
        """;

    /// <summary>Strict schema for the strategist.</summary>
    public const string PlaysSchema =
        """
        {
          "type": "object",
          "properties": {
            "plays": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "rank": { "type": "integer" },
                  "lever": { "type": "string" },
                  "ask": { "type": "string" },
                  "expectedValueKeys": { "type": "array", "items": { "type": "string" } },
                  "fallback": { "type": "string" },
                  "timing": { "type": "string" },
                  "citationKeys": { "type": "array", "items": { "type": "string" } }
                },
                "required": ["rank", "lever", "ask", "expectedValueKeys", "fallback", "timing", "citationKeys"],
                "additionalProperties": false
              }
            },
            "verdict": {
              "type": "object",
              "properties": {
                "targetReachable": { "type": "boolean" },
                "reason": { "type": "string" }
              },
              "required": ["targetReachable", "reason"],
              "additionalProperties": false
            }
          },
          "required": ["plays", "verdict"],
          "additionalProperties": false
        }
        """;
}
