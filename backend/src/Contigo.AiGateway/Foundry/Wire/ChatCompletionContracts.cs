using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contigo.AiGateway.Foundry.Wire;

/// <summary>
/// Azure OpenAI-compatible chat-completions wire shapes (classify/extract/answer roles all POST
/// here — <see cref="FoundryChatCompletionsClient"/>). One message shape serves both directions:
/// a request message (<c>role</c>/<c>content</c> the caller sets) and a response message
/// (<c>role</c>/<c>content</c> the model returns).
/// </summary>
public sealed record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

/// <summary>JSON-schema structured-output descriptor (<c>response_format.json_schema</c>).</summary>
public sealed record ChatJsonSchema(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("strict")] bool Strict,
    [property: JsonPropertyName("schema")] JsonElement Schema);

/// <summary>
/// <c>response_format</c> requesting JSON-schema-constrained structured output (never free text —
/// ADR-004: "a structured-output-capable model... not free text").
/// </summary>
public sealed record ChatResponseFormat(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("json_schema")] ChatJsonSchema JsonSchema);

/// <summary>
/// Chat-completions request body. Deliberately carries ONLY <see cref="Messages"/>,
/// <see cref="Temperature"/> and <see cref="ResponseFormat"/> — no <c>tools</c>,
/// <c>tool_choice</c>, <c>functions</c>, or <c>data_sources</c> (Azure OpenAI "on your data"
/// grounding) property exists on this type at all, so it is structurally impossible for any
/// caller (in particular <see cref="FoundryAnswerClient"/>) to ever put one of those keys on the
/// wire — ADR-024 / task E13/F01/US01/T02: "the answer request must carry no tools, tool_choice,
/// grounding or browsing fields", enforced by the type system rather than by remembering not to
/// set an optional property.
/// </summary>
public sealed record ChatCompletionRequest(
    [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
    [property: JsonPropertyName("temperature")] double Temperature,
    [property: JsonPropertyName("response_format")] ChatResponseFormat ResponseFormat);

public sealed record ChatCompletionChoice(
    [property: JsonPropertyName("message")] ChatMessage? Message);

public sealed record ChatCompletionResponse(
    [property: JsonPropertyName("choices")] IReadOnlyList<ChatCompletionChoice>? Choices);
