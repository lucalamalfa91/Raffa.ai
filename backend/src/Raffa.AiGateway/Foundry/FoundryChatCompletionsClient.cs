using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Foundry.Wire;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Foundry;

/// <summary>What one structured chat completion produced: the JSON content plus the provider's
/// own bookkeeping.</summary>
/// <param name="Content">The assistant message content — JSON matching the requested schema.</param>
/// <param name="FinishReason">Azure's <c>finish_reason</c> (<c>stop</c> on the happy path).</param>
/// <param name="Usage">Token usage, when reported.</param>
public sealed record ChatCompletionOutcome(string Content, string? FinishReason, AiTokenUsage? Usage);

/// <summary>
/// The one chat-completions call shape every chat role (<c>classify</c>, <c>extract</c>,
/// <c>answer</c>) shares: system + user message, <c>response_format: json_schema</c> with
/// <c>strict: true</c>, and the role's configuration-driven knobs — <c>max_completion_tokens</c>,
/// an optional <c>temperature</c> (clamped to <see cref="MaxTemperature"/>, ADR-024) and an optional
/// <c>reasoning_effort</c> — each omitted from the JSON when unset, because the GPT-5.x family
/// rejects parameters it does not support rather than ignoring them. Route per
/// <see cref="FoundryOpenAiRoutes"/>. A completion that stopped at the token cap, a content-filter
/// stop or a model refusal is an explicit failure here, never "not valid JSON" three layers up.
/// </summary>
public sealed class FoundryChatCompletionsClient(
    FoundryHttpJsonClient httpJsonClient, AiGatewayFoundryOptions foundryOptions)
{
    /// <summary>ADR-024: "temperature &lt;= 0.2" — a ceiling applied whenever a temperature is sent.</summary>
    public const double MaxTemperature = 0.2;

    private static readonly string[] AllowedReasoningEfforts = ["none", "minimal", "low", "medium", "high"];

    public async Task<Result<ChatCompletionOutcome>> CompleteAsync(
        string role,
        AiModelSelection model,
        string systemPrompt,
        string userPrompt,
        string schemaName,
        JsonElement jsonSchema,
        CancellationToken cancellationToken)
    {
        string? reasoningEffort = null;
        if (!string.IsNullOrWhiteSpace(model.ReasoningEffort))
        {
            reasoningEffort = model.ReasoningEffort.Trim().ToLowerInvariant();
            if (!AllowedReasoningEfforts.Contains(reasoningEffort, StringComparer.Ordinal))
            {
                return Result<ChatCompletionOutcome>.Failure(
                    $"AiGateway:Models:{role}:ReasoningEffort '{model.ReasoningEffort}' is not one of " +
                    $"{string.Join("/", AllowedReasoningEfforts)}.");
            }
        }

        var route = FoundryOpenAiRoutes.ChatCompletions(foundryOptions, model.ModelId);

        var request = new ChatCompletionRequest(
            Model: route.ModelInBody ? model.ModelId : null,
            Messages:
            [
                new ChatMessage("system", systemPrompt),
                new ChatMessage("user", userPrompt),
            ],
            ResponseFormat: new ChatResponseFormat(
                "json_schema", new ChatJsonSchema(schemaName, Strict: true, jsonSchema)),
            MaxCompletionTokens: model.MaxCompletionTokens,
            Temperature: model.Temperature is { } temperature ? Math.Clamp(temperature, 0, MaxTemperature) : null,
            ReasoningEffort: reasoningEffort);

        var result = await httpJsonClient
            .PostAsync<ChatCompletionRequest, ChatCompletionResponse>(route.RelativeUrl, request, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<ChatCompletionOutcome>.Failure(result.Error);
        }

        var choice = result.Value.Choices is { Count: > 0 } choices ? choices[0] : null;
        var usage = result.Value.Usage is { } u ? new AiTokenUsage(u.PromptTokens, u.CompletionTokens) : null;

        if (choice is null)
        {
            return Result<ChatCompletionOutcome>.Failure(
                $"Foundry chat completion for deployment '{model.ModelId}' returned no choices.");
        }

        if (!string.IsNullOrWhiteSpace(choice.Message?.Refusal))
        {
            return Result<ChatCompletionOutcome>.Failure(
                $"Foundry chat completion for deployment '{model.ModelId}' was refused by the model: {choice.Message.Refusal}");
        }

        if (string.Equals(choice.FinishReason, "length", StringComparison.OrdinalIgnoreCase))
        {
            return Result<ChatCompletionOutcome>.Failure(
                $"Foundry chat completion for deployment '{model.ModelId}' stopped at max_completion_tokens " +
                $"({model.MaxCompletionTokens?.ToString() ?? "deployment default"}) before the JSON was complete; " +
                $"raise AiGateway:Models:{role}:MaxCompletionTokens or shorten the input.");
        }

        if (string.Equals(choice.FinishReason, "content_filter", StringComparison.OrdinalIgnoreCase))
        {
            return Result<ChatCompletionOutcome>.Failure(
                $"Foundry chat completion for deployment '{model.ModelId}' was stopped by the content filter.");
        }

        var content = choice.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            return Result<ChatCompletionOutcome>.Failure(
                $"Foundry chat completion for deployment '{model.ModelId}' returned no message content.");
        }

        return Result<ChatCompletionOutcome>.Success(new ChatCompletionOutcome(content, choice.FinishReason, usage));
    }
}
