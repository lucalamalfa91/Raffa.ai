using System.Text.Json;
using System.Text.Json.Serialization;

namespace Raffa.AiGateway.Foundry.Wire;

/// <summary>
/// Azure OpenAI chat-completions wire shapes for the `agent` role (<see cref="FoundryAgentClient"/>):
/// the one request type in the gateway that carries <b>function</b> <c>tools</c> (Raffa's own data
/// and flows, never the web — the hosted <c>web_search</c> tool stays on the research role's
/// Responses request, ADR-030). A separate type from <see cref="ChatCompletionRequest"/> on purpose:
/// the answer role's compliance test still proves that request has no <c>tools</c> key, and this
/// one's proves the inverse. Every optional knob is <c>WhenWritingNull</c> (the GPT-5.x family
/// rejects a parameter it does not support).
/// </summary>
public sealed record AgentFunctionCall(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] string Arguments);

public sealed record AgentToolCall(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("function")] AgentFunctionCall Function);

public sealed record AgentChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Content,
    [property: JsonPropertyName("tool_calls")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<AgentToolCall>? ToolCalls = null,
    [property: JsonPropertyName("tool_call_id")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ToolCallId = null);

public sealed record AgentFunctionDefinition(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("parameters")] JsonElement Parameters,
    [property: JsonPropertyName("strict")] bool Strict);

public sealed record AgentTool(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("function")] AgentFunctionDefinition Function);

public sealed record AgentChatCompletionRequest(
    [property: JsonPropertyName("model")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<AgentChatMessage> Messages,
    [property: JsonPropertyName("tools")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<AgentTool>? Tools,
    [property: JsonPropertyName("tool_choice")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ToolChoice,
    [property: JsonPropertyName("parallel_tool_calls")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? ParallelToolCalls,
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

public sealed record AgentResponseMessage(
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("refusal")] string? Refusal,
    [property: JsonPropertyName("tool_calls")] IReadOnlyList<AgentToolCall>? ToolCalls);

public sealed record AgentChatCompletionChoice(
    [property: JsonPropertyName("message")] AgentResponseMessage? Message,
    [property: JsonPropertyName("finish_reason")] string? FinishReason);

public sealed record AgentChatCompletionResponse(
    [property: JsonPropertyName("choices")] IReadOnlyList<AgentChatCompletionChoice>? Choices,
    [property: JsonPropertyName("usage")] ChatCompletionUsage? Usage,
    [property: JsonPropertyName("model")] string? Model);
