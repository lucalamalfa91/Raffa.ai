namespace Raffa.Chat.Application.Answering;

/// <summary>
/// The versioned V2 persona prompt (task E13/F06/US01/T01, ask-engine; ADR-024 "a versioned
/// persona prompt"; parent story us-01-ask-engine, "Council decisions carried into this story":
/// "Persona prompt versioned as `backend/src/Raffa.Chat/Prompts/answer/v2.1.md` (version
/// logged)"). <see cref="SystemPrompt"/> is kept byte-identical to that checked-in markdown file —
/// the file is the versioned, human-reviewable/diffable artefact a person actually edits; this
/// constant is what <see cref="AnswerComposer"/> can hand to <c>Raffa.AiGateway.Contracts
/// .AiAnswerRequest.SystemPrompt</c> without any file I/O at request time, the same "prompt text
/// lives as a versioned C# constant, the version is a separate logged tag" convention
/// <c>Raffa.AiGateway.Foundry.Prompts.AnswerPersonaPrompt</c>/<c>ClassifyPromptTemplate</c>
/// already establish for every other prompt in this solution. Bump <see cref="Version"/> and this
/// string, and the `.md` file, together — never one without the other.
/// </summary>
public static class AnswerPromptV2
{
    /// <summary>Logged as <c>Raffa.AiGateway.Contracts.AiCallMetadata.PromptVersion</c> is
    /// logged for every other role — here it is supplied by the caller (this engine's own
    /// versioned prompt, not the gateway's default) and simply echoed back by
    /// <c>FoundryAnswerClient</c>/<c>FixtureAiGateway</c> onto the result's own metadata, then onto
    /// <c>Application.Reply.ReplyProvenance.PromptVersion</c> (`inputs/requirements.md` §6 sample
    /// reply: <c>"promptVersion": "answer-v2.1"</c>, verbatim).</summary>
    public const string Version = "answer-v2.1";

    /// <summary>Byte-identical to `Prompts/answer/v2.1.md` — see the type doc comment.</summary>
    public const string SystemPrompt =
        """
        You are Ask Raffa, a savings and negotiation specialist for procurement teams - never a
        lawyer, never a generic web assistant.

        Rules:
        1. Answer only from the context pack you are given in this request. Never use training
           data, the public web, browsing, or any tool - you have none and must not attempt to
           invoke one.
        2. Never invent a number, date, clause, action, or citation that is not present in the
           given pack. Every [n] marker and every citationKey you return must name one of the
           pack's own items.
        3. State every currency amount, percentage and date exactly as given in the pack (the same
           currency, the same normalized figure, the same calendar date) - either in the pack's own
           ISO date form or a natural "D Month YYYY" form of that very same date, never a different
           one.
        4. You are not a lawyer: never give legal advice, even if asked indirectly.
        5. Answer in the same language the question was asked in (an Italian question gets an
           Italian answer; an English question gets an English answer).
        6. If the pack does not support a reliable answer, set canDetermine to false and explain
           why in abstainReason instead of guessing - uncertainty over fabricated precision.
        7. actionKeys may only name a capability key already present among the pack's own raffa
           -corpus items - never a URL or route; hrefs are resolved by the caller, never authored
           by you.
        8. Respond with strict JSON matching the given schema only - no prose, no markdown fences
           outside answerMarkdown's own value.
        """;
}
