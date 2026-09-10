using System.Text.Json;
using System.Text.Json.Serialization;

namespace Raffa.AiGateway.Foundry.Wire;

/// <summary>
/// Azure OpenAI chat-completions wire shapes (<see cref="FoundryChatCompletionsClient"/>), for the
/// GA <c>openai/v1/chat/completions</c> surface and, as a fallback, the date-versioned
/// <c>openai/deployments/{deployment}/chat/completions</c> route. Explicit <see cref="JsonPropertyNameAttribute"/>
/// names because the wire is snake_case; every optional knob is <c>WhenWritingNull</c> so an unset
/// configuration value is genuinely absent from the request — the GPT-5.x family rejects, rather
/// than ignores, a parameter it does not support.
/// </summary>
public sealed record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

public sealed record ChatJsonSchema(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("strict")] bool Strict,
    [property: JsonPropertyName("schema")] JsonElement Schema);

public sealed record ChatResponseFormat(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("json_schema")] ChatJsonSchema JsonSchema);

/// <summary>
/// The request body. Deliberately has <b>no</b> <c>tools</c>, <c>tool_choice</c>, <c>functions</c>,
/// <c>data_sources</c> or any other grounding/browsing property (ADR-024: "no tools, no web
/// grounding, no browsing on the deployment or the request") — a property that does not exist on
/// this type cannot be serialized, which is what <c>FoundryAnswerClientTests</c> asserts on the raw
/// JSON.
/// </summary>
/// <param name="Model">Deployment name — required on the <c>v1</c> route, <see langword="null"/>
/// (omitted) on the date-versioned route where the deployment is in the URL.</param>
public sealed record ChatCompletionRequest(
    [property: JsonPropertyName("model")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
    [property: JsonPropertyName("response_format")] ChatResponseFormat ResponseFormat,
    [property: JsonPropertyName("max_completion_tokens")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? MaxCompletionTokens,
    [property: JsonPropertyName("temperature")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? Temperature,
    [property: JsonPropertyName("reasoning_effort")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ReasoningEffort);

public sealed record ChatResponseMessage(
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("refusal")] string? Refusal);

public sealed record ChatCompletionChoice(
    [property: JsonPropertyName("message")] ChatResponseMessage? Message,
    [property: JsonPropertyName("finish_reason")] string? FinishReason);

public sealed record ChatCompletionUsage(
    [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
    [property: JsonPropertyName("completion_tokens")] int CompletionTokens,
    [property: JsonPropertyName("total_tokens")] int TotalTokens);

public sealed record ChatCompletionResponse(
    [property: JsonPropertyName("choices")] IReadOnlyList<ChatCompletionChoice>? Choices,
    [property: JsonPropertyName("usage")] ChatCompletionUsage? Usage,
    [property: JsonPropertyName("model")] string? Model);
