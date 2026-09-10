using System.Text.Json;
using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Contracts;
using Contigo.AiGateway.Foundry.Prompts;
using Contigo.SharedKernel;

namespace Contigo.AiGateway.Foundry;

/// <summary>
/// `extract` role (ADR-004: schema-constrained structured extraction, one bounded stage at a
/// time). The caller's own <see cref="AiExtractionRequest.JsonSchema"/> is passed through verbatim
/// as the structured-output schema — this client never invents the domain shape, same contract
/// <see cref="IAiGateway.ExtractAsync"/>'s own doc comment already promises — but it does check the
/// schema is one Azure's strict mode will accept (<see cref="StrictJsonSchemaValidator"/>) and names
/// the offending path, because Azure's own answer to a non-strict schema is an opaque 400 on every
/// call of the stage.
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

        var strictness = StrictJsonSchemaValidator.Validate(schemaElement);
        if (strictness.IsFailure)
        {
            return Result<AiExtractionResult>.Failure(
                $"AiExtractionRequest.JsonSchema for stage '{request.StageName}' is not accepted by Azure " +
                $"structured outputs — {strictness.Error}");
        }

        var model = modelOptions.Extract;

        var completion = await chatClient.CompleteAsync(
                "Extract",
                model,
                ExtractPromptTemplate.SystemPrompt(request.StageName),
                request.DocumentText,
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
            request.StageName + " " + request.DocumentText + " " + request.JsonSchema,
            completion.Value.Usage);

        return Result<AiExtractionResult>.Success(new AiExtractionResult(completion.Value.Content, metadata));
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
