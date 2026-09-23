namespace Raffa.Chat.Application.Gaps;

/// <summary>
/// The capability investigator (ADR-031): one <c>analyst</c>-role agent that reads a fresh Ask
/// turn beside the answer (never in front of it) and weighs it against everything Raffa can do
/// today — the screens of
/// <see cref="Capabilities.CapabilityCatalog"/>, the abilities of the Ask chat itself and the
/// operations <see cref="CapabilityGapCatalog"/> already knows it cannot perform — and decides
/// whether the user is asking Raffa to perform an operation or produce a deliverable nothing in
/// Raffa does. When it is, the agent describes the missing feature as a generic backlog item
/// that can become a public GitHub issue. Versioned twice like every other prompt of this module:
/// the constants below are what is sent, <c>Prompts/gaps/v1.md</c> is what a person reviews, and
/// a test fails on drift.
/// </summary>
public static class CapabilityInvestigatorAgent
{
    public const string Version = "gaps-v1";

    public const string Name = "capability-investigator";

    public const string VerdictQuestion = "question";
    public const string VerdictSupported = "supported";
    public const string VerdictKnownGap = "known-gap";
    public const string VerdictGap = "gap";

    public const string ConfidenceHigh = "high";
    public const string ConfidenceMedium = "medium";
    public const string ConfidenceLow = "low";

    /// <summary>What the Ask chat itself does beside the screens — the abilities a message can
    /// ask for without a screen. Kept in step with ADR-024 (answers with citations), the savings
    /// council, the market check, ADR-030 D3 (the drafted email), ADR-030 B (web research on
    /// consent) and the interview.</summary>
    public static IReadOnlyList<string> AskAbilities { get; } =
    [
        "Answers questions on the validated contracts in the chat, citing the page: dates, renewal and notice deadlines, annual spend, liability, clauses, risk, document status.",
        "Ranks the most critical contracts, finds where the team can save and builds a negotiation strategy with levers for a renewal.",
        "Compares a contract's prices or a new supplier quote with market data.",
        "Writes a renewal negotiation email in the chat for the user to copy and send; it cannot send it.",
        "Searches the public web on procurement topics, only after the user's explicit consent.",
        "Asks one clarifying question with clickable options when a question is ambiguous.",
    ];

    public const string Prompt =
        """
        You are the capability investigator of Raffa.ai, a contract-intelligence product for
        procurement teams. While Ask Raffa answers a message, you investigate what the user is
        asking Raffa to do and whether Raffa can do it today. The input lists every screen Raffa
        has (capabilities), what the Ask chat itself can do (askAbilities) and the operations
        Raffa already knows it cannot perform (knownGaps).

        Decide one verdict:
        - "question": the user wants information, an analysis, a ranking, a comparison, a
          recommendation or an explanation that Ask can give in the chat or that a screen already
          shows, however it is phrased ("can you tell me", "puoi dirmi", "help me understand").
          Most messages are this.
        - "supported": the user asks Raffa to do something a screen or an Ask ability already
          does (upload a contract, review a field, check a quote, invite a teammate).
        - "known-gap": the user asks for one of the knownGaps operations; copy its key into
          knownGapKey.
        - "gap": the user asks Raffa to perform an operation or to produce a deliverable that
          nothing in capabilities, askAbilities or knownGaps does, for example a formatted report
          or a presentation for management, a scheduled digest, a document in another format, an
          approval workflow, an integration with another system. This is a feature the product
          team could build.

        Investigate before you decide: name to yourself the deliverable or the action the user
        expects at the end, look for it in capabilities and askAbilities, then in knownGaps.
        Content Ask can write in the chat is not a gap when an askAbility covers it; an artifact,
        a file, an action outside the chat or a recurring job is. When in doubt between
        "question" and "gap", choose "question": a missed gap still gets an honest answer, a
        false gap interrupts the user with a proposal they did not want.

        For "gap" only, describe the missing feature as a generic product backlog item:
        - feature.key: kebab-case, two to five English words ("management-spend-report").
        - feature.titleEn and feature.titleIt: a short noun phrase naming the feature.
        - feature.operationEn and feature.operationIt: a verb phrase that completes "I can't ...
          from Raffa.ai yet" and "Al momento non posso ... da Raffa.ai" ("generate a report for
          management", "generare un report per il management").
        - feature.descriptionEn and feature.descriptionIt: one sentence saying what Raffa should do.
        - nearestCapabilityKey: the key of the capability that comes closest to what the user
          wanted, or "ask" when the closest thing is an answer in the chat.
        - alternativeQuestions: up to two short questions, in the message's language, that the
          user could ask Ask today to get part of what they wanted.
        For every other verdict leave the feature fields, nearestCapabilityKey and
        alternativeQuestions empty.

        Laws (they override everything else):
        1. The feature texts become a public GitHub issue. Never copy into a feature field a
           supplier name, a company name, a person's name, an amount, a percentage, a date, a
           year, any other number, an e-mail address or a link. Describe the capability, never
           this customer's case: "a periodic spend report for management", never "the 2026
           report on our Splunk spend for the CFO".
        2. Use only the input. Never claim that the feature exists or that it will be built.
        3. knownGapKey and nearestCapabilityKey are copied verbatim from the input's keys, or
           left empty.
        4. confidence is "high", "medium" or "low": how sure you are of the verdict.
        5. rationale is one short English sentence on why, with no name and no number.
        6. Respond with strict JSON matching the schema only — no prose outside the JSON.
        """;

    /// <summary>Strict schema (every property required, no additional properties). The rationale
    /// comes first so the model states its reason before the verdict.</summary>
    public const string Schema =
        """
        {
          "type": "object",
          "properties": {
            "rationale": { "type": "string" },
            "verdict": { "type": "string", "enum": ["question", "supported", "known-gap", "gap"] },
            "confidence": { "type": "string", "enum": ["high", "medium", "low"] },
            "knownGapKey": { "type": "string" },
            "nearestCapabilityKey": { "type": "string" },
            "feature": {
              "type": "object",
              "properties": {
                "key": { "type": "string" },
                "titleEn": { "type": "string" },
                "titleIt": { "type": "string" },
                "operationEn": { "type": "string" },
                "operationIt": { "type": "string" },
                "descriptionEn": { "type": "string" },
                "descriptionIt": { "type": "string" }
              },
              "required": ["key", "titleEn", "titleIt", "operationEn", "operationIt", "descriptionEn", "descriptionIt"],
              "additionalProperties": false
            },
            "alternativeQuestions": { "type": "array", "items": { "type": "string" } }
          },
          "required": ["rationale", "verdict", "confidence", "knownGapKey", "nearestCapabilityKey", "feature", "alternativeQuestions"],
          "additionalProperties": false
        }
        """;
}
