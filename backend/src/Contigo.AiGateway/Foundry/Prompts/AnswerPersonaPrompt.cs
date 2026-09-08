using System.Text.Json;

namespace Contigo.AiGateway.Foundry.Prompts;

/// <summary>
/// Versioned default persona prompt + JSON schema for the `answer` role (ADR-023/ADR-024: a
/// savings/negotiation copilot, never a chunk concatenator; abstain or redirect rather than
/// fabricate). <see cref="FoundryAnswerClient"/> uses <see cref="DefaultSystemPrompt"/> only when
/// the caller does not supply its own <c>AiAnswerRequest.SystemPrompt</c> — ADR-024's own versioned
/// persona prompt is assembled by a later task ("F06 replaces the chunk-concat by supplying a
/// prompt + pack"); until then this default keeps the role functionally answerable against the
/// existing evidence-only call path (<c>Contigo.Chat.Application.RagAnswerService</c>).
/// </summary>
public static class AnswerPersonaPrompt
{
    /// <summary>Bump when the prompt or schema text below changes.</summary>
    public const string Version = "foundry-answer-v1";

    public const string DefaultSystemPrompt =
        """
        You are Ask Contigo, a contract savings and negotiation copilot. Answer only from the
        evidence and context pack given to you in this request - never from training data, the
        public web, or browsing; you have no tools and must not attempt to use any. Never invent a
        number, date, clause, action, or citation that is not present in the given evidence or
        context pack. If the given evidence/pack is insufficient to answer, set canDetermine to
        false and explain why in abstainReason instead of guessing. Never give legal advice -
        redirect legal questions to the user's own legal counsel instead of answering them.
        Respond with strict JSON matching the given schema only: no prose, no markdown fences
        outside answerMarkdown's own value.
        """;

    /// <summary>
    /// <c>{ canDetermine, answerMarkdown, citationKeys[], actionKeys[], abstainReason,
    /// followUps[] }</c> — exactly ADR-024's structured `answer` shape. <c>answerMarkdown</c> and
    /// <c>abstainReason</c> are nullable in the schema itself (one or the other is expected to be
    /// null depending on <c>canDetermine</c>), never omitted, so
    /// <c>FoundryAnswerClient</c>'s parser can always find the key.
    /// </summary>
    public static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {
          "type": "object",
          "properties": {
            "canDetermine": { "type": "boolean" },
            "answerMarkdown": { "type": ["string", "null"] },
            "citationKeys": { "type": "array", "items": { "type": "string" } },
            "actionKeys": { "type": "array", "items": { "type": "string" } },
            "abstainReason": { "type": ["string", "null"] },
            "followUps": { "type": "array", "items": { "type": "string" } }
          },
          "required": [
            "canDetermine", "answerMarkdown", "citationKeys", "actionKeys", "abstainReason",
            "followUps"
          ],
          "additionalProperties": false
        }
        """).RootElement;
}
