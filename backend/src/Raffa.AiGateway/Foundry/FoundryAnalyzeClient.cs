using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Foundry;

/// <summary>
/// The `analyst` role: a strict-JSON chat completion over caller-supplied evidence and a
/// caller-supplied persona (see <see cref="AiAnalysisRequest"/>). Runs on
/// <see cref="AiGatewayModelOptions.Analyst"/> when configured, else on the `answer` deployment —
/// no new infrastructure is required to turn the negotiation council on.
/// </summary>
public sealed class FoundryAnalyzeClient(
    FoundryChatCompletionsClient chatClient, AiGatewayModelOptions modelOptions, IClock clock)
{
    public async Task<Result<AiAnalysisResult>> AnalyzeAsync(
        AiAnalysisRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AgentName))
        {
            return Result<AiAnalysisResult>.Failure("Analysis requires an agent name.");
        }

        if (string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            return Result<AiAnalysisResult>.Failure("Analysis requires a system prompt.");
        }

        if (string.IsNullOrWhiteSpace(request.InputJson))
        {
            return Result<AiAnalysisResult>.Failure("Analysis requires a non-empty JSON input.");
        }

        if (string.IsNullOrWhiteSpace(request.JsonSchema))
        {
            return Result<AiAnalysisResult>.Failure("Analysis requires a target JSON schema.");
        }

        JsonElement schemaElement;
        try
        {
            using var schemaDocument = JsonDocument.Parse(request.JsonSchema);
            schemaElement = schemaDocument.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return Result<AiAnalysisResult>.Failure($"AiAnalysisRequest.JsonSchema was not valid JSON: {ex.Message}");
        }

        var strictness = StrictJsonSchemaValidator.Validate(schemaElement);
        if (strictness.IsFailure)
        {
            return Result<AiAnalysisResult>.Failure(
                $"AiAnalysisRequest.JsonSchema for agent '{request.AgentName}' is not accepted by Azure structured outputs — {strictness.Error}");
        }

        var model = modelOptions.Analyst ?? modelOptions.Answer;

        var completion = await chatClient.CompleteAsync(
                "Analyst",
                model,
                request.SystemPrompt,
                request.InputJson,
                schemaName: "raffa_analysis_" + SanitizeSchemaName(request.AgentName),
                schemaElement,
                cancellationToken)
            .ConfigureAwait(false);

        if (completion.IsFailure)
        {
            return Result<AiAnalysisResult>.Failure(completion.Error);
        }

        var metadata = FoundryCallMetadataFactory.Build(
            model,
            request.PromptVersion,
            clock,
            request.AgentName + " " + request.SystemPrompt + " " + request.InputJson,
            completion.Value.Usage);

        return Result<AiAnalysisResult>.Success(new AiAnalysisResult(completion.Value.Content, metadata));
    }

    private static string SanitizeSchemaName(string agentName)
    {
        var chars = agentName.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray();
        return new string(chars);
    }
}
