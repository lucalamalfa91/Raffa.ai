namespace Raffa.AiGateway.Contracts;

/// <summary>
/// One function tool the `agent` role may call: a name, what it does (the model reads this to
/// decide), and the strict JSON Schema of its arguments (Azure structured function calling:
/// <c>additionalProperties: false</c>, every property required). The gateway only carries the
/// definition; executing the call is the caller's loop (<c>Raffa.AiFlows.Ask.Agentic</c>), which
/// is how the tenant scope, the citation keys and the audit stay where they are today.
/// </summary>
public sealed record AiToolDefinition(string Name, string Description, string ParametersSchemaJson);

/// <summary>A tool call the model asked for: the provider's call id (echoed back on the result
/// message), the tool name and the JSON arguments as the model wrote them (validate before use).</summary>
public sealed record AiToolCall(string Id, string Name, string ArgumentsJson);

/// <summary>
/// One message of the agent conversation, in wire order. <see cref="Role"/> is <c>user</c>,
/// <c>assistant</c> or <c>tool</c> (the system prompt travels separately on the request).
/// An assistant message carries either <see cref="Content"/> (text) or <see cref="ToolCalls"/>;
/// a tool message carries the <see cref="ToolCallId"/> it answers and the result as
/// <see cref="Content"/> (JSON).
/// </summary>
public sealed record AiConversationMessage(
    string Role,
    string? Content,
    IReadOnlyList<AiToolCall>? ToolCalls = null,
    string? ToolCallId = null)
{
    public const string UserRole = "user";
    public const string AssistantRole = "assistant";
    public const string ToolRole = "tool";

    public static AiConversationMessage User(string content) => new(UserRole, content);

    public static AiConversationMessage Assistant(string content) => new(AssistantRole, content);

    public static AiConversationMessage AssistantToolCalls(IReadOnlyList<AiToolCall> toolCalls) => new(AssistantRole, null, toolCalls);

    public static AiConversationMessage ToolResult(string toolCallId, string resultJson) => new(ToolRole, resultJson, ToolCallId: toolCallId);
}

/// <summary>
/// Input of the `agent` role: one step of a tool-using conversation. The model reads the system
/// prompt, the messages so far and the tool definitions, and either asks for tool calls or writes
/// the final answer as strict JSON matching <paramref name="FinalJsonSchema"/>. The caller runs the
/// loop: append the tool results as <see cref="AiConversationMessage.ToolRole"/> messages and call
/// again until <see cref="AiConversationResult.IsFinal"/>.
/// </summary>
/// <param name="AgentName">Stable agent identity for the audit row and the schema name
/// (letters, digits, '-' and '_').</param>
/// <param name="SystemPrompt">The versioned persona and its laws.</param>
/// <param name="Messages">The conversation so far, oldest first.</param>
/// <param name="Tools">The tools the model may call this step (may be empty on the last step to
/// force a final answer).</param>
/// <param name="FinalJsonSchema">The strict JSON Schema of the final answer.</param>
/// <param name="PromptVersion">The caller's prompt version tag, echoed on the metadata.</param>
/// <param name="ForceFinal">When true the request carries <c>tool_choice: none</c>: the model must
/// answer now (the loop's call budget is spent).</param>
public sealed record AiConversationRequest(
    string AgentName,
    string SystemPrompt,
    IReadOnlyList<AiConversationMessage> Messages,
    IReadOnlyList<AiToolDefinition> Tools,
    string FinalJsonSchema,
    string PromptVersion,
    bool ForceFinal = false);

/// <summary>What one agent step produced: tool calls to run (then call again), or the final JSON
/// (schema-constrained by the provider, not parsed by the gateway — the caller owns the shape).</summary>
public sealed record AiConversationResult(
    IReadOnlyList<AiToolCall> ToolCalls,
    string? FinalJson,
    AiCallMetadata Metadata)
{
    public bool IsFinal => ToolCalls.Count == 0;
}
