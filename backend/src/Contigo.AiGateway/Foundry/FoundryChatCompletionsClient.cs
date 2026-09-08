using System.Text.Json;
using Contigo.AiGateway.Foundry.Wire;
using Contigo.SharedKernel;

namespace Contigo.AiGateway.Foundry;

/// <summary>
/// Shared chat-completions caller behind `classify`/`extract`/`answer` (all three are, on the
/// wire, one system message + one user message + a JSON-schema <c>response_format</c> against an
/// Azure OpenAI-compatible deployment — only the prompt/schema/temperature differ per role). Each
/// per-role client (<see cref="FoundryClassifyClient"/>, <see cref="FoundryExtractClient"/>,
/// <see cref="FoundryAnswerClient"/>) owns its own prompt/schema/temperature and result parsing;
/// this type owns only the one HTTP shape all three share, so that shape (and the "no tools/
/// tool_choice/data_sources field exists on the request type" compliance guarantee —
/// <see cref="Wire.ChatCompletionRequest"/>'s own doc comment) is implemented exactly once.
/// </summary>
public sealed class FoundryChatCompletionsClient(FoundryHttpJsonClient httpJsonClient)
{
    /// <summary>Stable Azure OpenAI data-plane GA api-version (date-versioned, per Azure's own
    /// REST versioning convention).</summary>
    private const string ApiVersion = "2024-06-01";

    public async Task<Result<string>> CompleteAsync(
        string deploymentId,
        string systemPrompt,
        string userPrompt,
        double temperature,
        string schemaName,
        JsonElement jsonSchema,
        CancellationToken cancellationToken)
    {
        var request = new ChatCompletionRequest(
            Messages:
            [
                new ChatMessage("system", systemPrompt),
                new ChatMessage("user", userPrompt),
            ],
            Temperature: temperature,
            ResponseFormat: new ChatResponseFormat(
                "json_schema", new ChatJsonSchema(schemaName, Strict: true, jsonSchema)));

        var relativeUrl =
            $"openai/deployments/{Uri.EscapeDataString(deploymentId)}/chat/completions?api-version={ApiVersion}";

        var result = await httpJsonClient
            .PostAsync<ChatCompletionRequest, ChatCompletionResponse>(relativeUrl, request, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<string>.Failure(result.Error);
        }

        var content = result.Value.Choices is { Count: > 0 } choices ? choices[0].Message?.Content : null;

        return string.IsNullOrWhiteSpace(content)
            ? Result<string>.Failure(
                $"Foundry chat completion for deployment '{deploymentId}' returned no message content.")
            : Result<string>.Success(content);
    }
}
