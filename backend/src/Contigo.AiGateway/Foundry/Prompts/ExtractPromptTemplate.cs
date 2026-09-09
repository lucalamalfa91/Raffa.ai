namespace Contigo.AiGateway.Foundry.Prompts;

/// <summary>
/// Versioned prompt wrapper for the `extract` role. Unlike <see cref="ClassifyPromptTemplate"/>/
/// <see cref="AnswerPersonaPrompt"/>, `extract` has no fixed schema of its own — the caller
/// supplies one per stage (<c>AiExtractionRequest.JsonSchema</c>; ADR-004: "the gateway does not
/// know or validate the domain schema") — so this type owns only the system-prompt wording and the
/// version tag, parameterized by the caller's own stage name. Field semantics live in the schema's
/// own <c>description</c>s (owned by the domain module); this prompt owns the rules that apply to
/// every stage: evidence only, <c>null</c> for an absent value (strict mode forbids omitting a
/// property), normalised value formats the pipeline's parsers accept, and page/span evidence.
/// </summary>
public static class ExtractPromptTemplate
{
    /// <summary>Bump when the prompt wording below changes.</summary>
    public const string Version = "foundry-extract-v2";

    public static string SystemPrompt(string stageName) =>
        $"You extract structured facts for the '{stageName}' stage of a contract-review pipeline, " +
        "from the document text only. The document may be written in any language (Italian supplier " +
        "contracts are common); read it in its own language. The text carries [[PAGE n]] markers " +
        "where each page starts.\n" +
        "Rules:\n" +
        "1. Extract only what the text states. Never invent a value, a page, a quote or a confidence.\n" +
        "2. When the document does not state a value, return null for it - never a guess, never a " +
        "placeholder such as \"N/A\", \"unknown\" or an empty string. A fact whose value is null is " +
        "treated as not found. For a list stage, do not add an item the text does not support.\n" +
        "3. Every property in the schema is required: fill it or set it to null; nothing may be omitted.\n" +
        "4. Normalise values: dates as ISO 8601 calendar dates \"YYYY-MM-DD\" (\"2026-02-01\" for " +
        "\"1 febbraio 2026\"; null when no calendar day can be resolved); amounts and numbers as digits " +
        "with \".\" as the decimal separator, no thousands separators, no currency symbols or units " +
        "(\"48000\", \"1250.75\", \"12\"); for a number-typed property use a JSON number, never a " +
        "string; currencies as ISO 4217 codes (\"EUR\", \"USD\", \"CHF\", \"GBP\"); booleans as " +
        "\"true\" or \"false\"; durations in months as the integer number of months (\"36\" for three " +
        "years); enums as exactly one of the schema's listed values; short classifying fields " +
        "(payment terms, governing law, clause type, obligation type, risk type, status, criticality) " +
        "as short English phrases (\"Net 30\", \"Italy\", \"limitation of liability\", \"active\").\n" +
        "5. Evidence: sourcePage is the integer n of the [[PAGE n]] marker that precedes the evidence; " +
        "sourceSpan is a verbatim quote of at most 300 characters from that page, in the document's " +
        "original language, that supports the value; descriptions and raw clause text quote or " +
        "summarise the original language. Set sourcePage and sourceSpan to null only when you " +
        "genuinely cannot point to a page.\n" +
        "6. confidence is a number from 0 to 1: your probability that the value is right. Use a value " +
        "below 0.6 when the document is ambiguous or contradicts itself (two different fees for the " +
        "same field, a clause that both affirms and denies renewal, parties named without saying who " +
        "supplies), and 0 next to a null value.\n" +
        "Respond with strict JSON matching the given schema only: no prose, no markdown fences.";
}
