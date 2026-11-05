using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Foundry.Wire;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Foundry;

/// <summary>
/// The `agent` role (ADR-004 amendment 2026-11-05): one chat completion with <b>function tools</b>
/// over Raffa's own data and flows, on <see cref="AiGatewayModelOptions.Agent"/> when configured,
/// else on the `answer` deployment (no infrastructure change to turn the orchestrator on). Every
/// tool's argument schema and the final answer's schema go through
/// <see cref="StrictJsonSchemaValidator"/>, so a shape Azure would reject is a named failure
/// before the call. The model's reply is either tool calls (<c>finish_reason = tool_calls</c>) or
/// the final JSON; a reply stopped at the token cap, by the content filter or by a model refusal
/// is an explicit failure. The hosted <c>web_search</c> tool never appears here: this client only
/// ever writes <c>type: "function"</c> (<see cref="FunctionToolType"/>), asserted by its tests.
/// </summary>
public sealed class FoundryAgentClient(
    FoundryHttpJsonClient httpJsonClient,
    AiGatewayFoundryOptions foundryOptions,
    AiGatewayModelOptions modelOptions,
    IClock clock)
{
    public const string FunctionToolType = "function";
    public const string RoleName = "Agent";

    private static readonly string[] AllowedReasoningEfforts = ["none", "minimal", "low", "medium", "high"];

    public async Task<Result<AiConversationResult>> ConverseAsync(
        AiConversationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.AgentName))
        {
            return Result<AiConversationResult>.Failure("An agent conversation requires an agent name.");
        }

        if (string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            return Result<AiConversationResult>.Failure("An agent conversation requires a system prompt.");
        }

        if (request.Messages.Count == 0)
        {
            return Result<AiConversationResult>.Failure("An agent conversation requires at least one message.");
        }

        var finalSchema = ParseStrictSchema(request.FinalJsonSchema, "final answer");
        if (finalSchema.IsFailure)
        {
            return Result<AiConversationResult>.Failure(finalSchema.Error);
        }

        var tools = new List<AgentTool>(request.Tools.Count);
        foreach (var tool in request.Tools)
        {
            if (string.IsNullOrWhiteSpace(tool.Name) || !tool.Name.All(c => char.IsLetterOrDigit(c) || c is '_' or '-'))
            {
                return Result<AiConversationResult>.Failure($"Tool name '{tool.Name}' must be letters, digits, '_' or '-'.");
            }

            var parameters = ParseStrictSchema(tool.ParametersSchemaJson, $"tool '{tool.Name}'");
            if (parameters.IsFailure)
            {
                return Result<AiConversationResult>.Failure(parameters.Error);
            }

            tools.Add(new AgentTool(FunctionToolType, new AgentFunctionDefinition(tool.Name, tool.Description, parameters.Value, Strict: true)));
        }

        var model = modelOptions.Agent ?? modelOptions.Answer;

        string? reasoningEffort = null;
        if (!string.IsNullOrWhiteSpace(model.ReasoningEffort))
        {
            reasoningEffort = model.ReasoningEffort.Trim().ToLowerInvariant();
            if (!AllowedReasoningEfforts.Contains(reasoningEffort, StringComparer.Ordinal))
            {
                return Result<AiConversationResult>.Failure(
                    $"AiGateway:Models:{RoleName}:ReasoningEffort '{model.ReasoningEffort}' is not one of {string.Join("/", AllowedReasoningEfforts)}.");
            }
        }

        var route = FoundryOpenAiRoutes.ChatCompletions(foundryOptions, model.ModelId);
        var messages = new List<AgentChatMessage>(request.Messages.Count + 1) { new("system", request.SystemPrompt) };
        foreach (var message in request.Messages)
        {
            messages.Add(new AgentChatMessage(
                message.Role,
                message.Content,
                message.ToolCalls?.Select(c => new AgentToolCall(c.Id, FunctionToolType, new AgentFunctionCall(c.Name, c.ArgumentsJson))).ToList(),
                message.ToolCallId));
        }

        var hasTools = tools.Count > 0 && !request.ForceFinal;
        var body = new AgentChatCompletionRequest(
            Model: route.ModelInBody ? model.ModelId : null,
            Messages: messages,
            Tools: hasTools ? tools : null,
            // tool_choice and parallel_tool_calls are left at the provider's defaults (auto, on): an
            // explicit value is one more parameter a reasoning deployment can reject.
            ToolChoice: null,
            ParallelToolCalls: null,
            ResponseFormat: new ChatResponseFormat(
                "json_schema", new ChatJsonSchema("raffa_agent_" + SanitizeSchemaName(request.AgentName), Strict: true, finalSchema.Value)),
            MaxCompletionTokens: model.MaxCompletionTokens,
            Temperature: model.Temperature is { } temperature ? Math.Clamp(temperature, 0, FoundryChatCompletionsClient.MaxTemperature) : null,
            ReasoningEffort: reasoningEffort);

        var result = await httpJsonClient
            .PostAsync<AgentChatCompletionRequest, AgentChatCompletionResponse>(route.RelativeUrl, body, cancellationToken)
            .ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Result<AiConversationResult>.Failure(result.Error);
        }

        var choice = result.Value.Choices is { Count: > 0 } choices ? choices[0] : null;
        if (choice is null)
        {
            return Result<AiConversationResult>.Failure($"Foundry agent completion for deployment '{model.ModelId}' returned no choices.");
        }

        if (!string.IsNullOrWhiteSpace(choice.Message?.Refusal))
        {
            return Result<AiConversationResult>.Failure(
                $"Foundry agent completion for deployment '{model.ModelId}' was refused by the model: {choice.Message.Refusal}");
        }

        if (string.Equals(choice.FinishReason, "length", StringComparison.OrdinalIgnoreCase))
        {
            return Result<AiConversationResult>.Failure(
                $"Foundry agent completion for deployment '{model.ModelId}' stopped at max_completion_tokens " +
                $"({model.MaxCompletionTokens?.ToString() ?? "deployment default"}); raise AiGateway:Models:{RoleName}:MaxCompletionTokens.");
        }

        if (string.Equals(choice.FinishReason, "content_filter", StringComparison.OrdinalIgnoreCase))
        {
            return Result<AiConversationResult>.Failure(
                $"Foundry agent completion for deployment '{model.ModelId}' was stopped by the content filter.");
        }

        var usage = result.Value.Usage is { } u ? new AiTokenUsage(u.PromptTokens, u.CompletionTokens, u.PromptTokensDetails?.CachedTokens ?? 0) : null;
        var metadata = FoundryCallMetadataFactory.Build(
            model, request.PromptVersion, clock,
            request.AgentName + " " + request.SystemPrompt + " " + string.Join("\n", request.Messages.Select(m => m.Role + ":" + m.Content)),
            usage);

        var toolCalls = choice.Message?.ToolCalls;
        if (toolCalls is { Count: > 0 })
        {
            var calls = new List<AiToolCall>(toolCalls.Count);
            foreach (var call in toolCalls)
            {
                if (!string.Equals(call.Type, FunctionToolType, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(call.Function?.Name))
                {
                    return Result<AiConversationResult>.Failure(
                        $"Foundry agent completion for deployment '{model.ModelId}' returned a tool call that is not a function call.");
                }

                calls.Add(new AiToolCall(call.Id, call.Function.Name, string.IsNullOrWhiteSpace(call.Function.Arguments) ? "{}" : call.Function.Arguments));
            }

            return Result<AiConversationResult>.Success(new AiConversationResult(calls, null, metadata));
        }

        var content = choice.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            return Result<AiConversationResult>.Failure(
                $"Foundry agent completion for deployment '{model.ModelId}' returned neither tool calls nor message content.");
        }

        return Result<AiConversationResult>.Success(new AiConversationResult([], content, metadata));
    }

    private static Result<JsonElement> ParseStrictSchema(string schemaJson, string what)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return Result<JsonElement>.Failure($"The JSON schema of the {what} is empty.");
        }

        JsonElement element;
        try
        {
            using var document = JsonDocument.Parse(schemaJson);
            element = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return Result<JsonElement>.Failure($"The JSON schema of the {what} is not valid JSON: {ex.Message}");
        }

        var strictness = StrictJsonSchemaValidator.Validate(element);
        return strictness.IsFailure
            ? Result<JsonElement>.Failure($"The JSON schema of the {what} is not accepted by Azure structured outputs — {strictness.Error}")
            : Result<JsonElement>.Success(element);
    }

    private static string SanitizeSchemaName(string agentName) =>
        new(agentName.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray());
}
