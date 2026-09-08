using System.Text.Json;
using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Contracts;
using Contigo.AiGateway.Foundry.Prompts;
using Contigo.SharedKernel;

namespace Contigo.AiGateway.Foundry;

/// <summary>
/// `extract` role (ADR-004: schema-constrained structured extraction, one bounded stage at a
/// time). The caller's own <see cref="AiExtractionRequest.JsonSchema"/> is parsed and passed
/// through verbatim as the structured-output schema — this client never invents or validates the
/// domain shape, same contract <see cref="IAiGateway.ExtractAsync"/>'s own doc comment already
/// promises. Temperature 0 — extraction, like classification, wants determinism.
/// </summary>
public sealed class FoundryExtractClient(
    FoundryChatCompletionsClient chatClient, AiGatewayModelOptions modelOptions, IClock clock)
{
    public async Task<Result<AiExtractionResult>> ExtractAsync(
        AiExtractionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentText))
        {
            return Result<AiExtractionResult>.Failure("Extraction requires non-empty document text.");
        }

        if (string.IsNullOrWhiteSpace(request.JsonSchema))
        {
            return Result<AiExtractionResult>.Failure(
                "Extraction requires a target JSON schema (spec §7.3: schema-constrained output).");
        }

        JsonElement schemaElement;
        try
        {
            using var schemaDocument = JsonDocument.Parse(request.JsonSchema);
            schemaElement = schemaDocument.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return Result<AiExtractionResult>.Failure(
                $"AiExtractionRequest.JsonSchema was not valid JSON: {ex.Message}");
        }

        var model = modelOptions.Extract;

        var completion = await chatClient.CompleteAsync(
                model.ModelId,
                ExtractPromptTemplate.SystemPrompt(request.StageName),
                request.DocumentText,
                temperature: 0,
                schemaName: "contigo_extraction_" + SanitizeSchemaName(request.StageName),
                schemaElement,
                cancellationToken)
            .ConfigureAwait(false);

        if (completion.IsFailure)
        {
            return Result<AiExtractionResult>.Failure(completion.Error);
        }

        var metadata = FoundryCallMetadataFactory.Build(
            model,
            ExtractPromptTemplate.Version,
            clock,
            request.StageName + " " + request.DocumentText + " " + request.JsonSchema);

        return Result<AiExtractionResult>.Success(new AiExtractionResult(completion.Value, metadata));
    }

    /// <summary>Azure's structured-output schema <c>name</c> is restricted to
    /// letters/digits/underscore/hyphen — the caller's free-form <c>StageName</c>
    /// (<see cref="AiExtractionRequest.StageName"/>'s own doc comment: "caller-owned stage label")
    /// is not guaranteed to satisfy that, so this maps every other character to <c>_</c>.</summary>
    private static string SanitizeSchemaName(string stageName)
    {
        var chars = stageName.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray();
        return new string(chars);
    }
}
