using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Foundry.Prompts;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Foundry;

/// <summary>
/// `classify` role (ADR-004; ADR-024: "classify is reused for the document admission gate and for
/// the Ask domain gate with fixed label sets"). Reads a representative prefix of the document
/// (<see cref="AiGatewayFoundryOptions.ClassifyMaxInputChars"/>) — the verdict does not improve with
/// page 200 and the tokens would be billed for nothing. Determinism knobs (temperature, reasoning
/// effort) come from the role's configuration, see <see cref="AiModelSelection"/>.
/// </summary>
public sealed class FoundryClassifyClient(
    FoundryChatCompletionsClient chatClient,
    AiGatewayModelOptions modelOptions,
    AiGatewayFoundryOptions foundryOptions,
    IClock clock)
{
    private sealed record ClassifyPayload(string? DocumentType, double Confidence);

    public async Task<Result<AiClassificationResult>> ClassifyAsync(
        AiClassificationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentText))
        {
            return Result<AiClassificationResult>.Failure("Classification requires non-empty document text.");
        }

        var model = modelOptions.Classify;
        var maxChars = Math.Max(1, foundryOptions.ClassifyMaxInputChars);
        var documentText = request.DocumentText.Length <= maxChars
            ? request.DocumentText
            : request.DocumentText[..maxChars];

        var completion = await chatClient.CompleteAsync(
                "Classify",
                model,
                ClassifyPromptTemplate.SystemPrompt,
                documentText,
                schemaName: "raffa_document_classification",
                ClassifyPromptTemplate.Schema,
                cancellationToken)
            .ConfigureAwait(false);

        if (completion.IsFailure)
        {
            return Result<AiClassificationResult>.Failure(completion.Error);
        }

        ClassifyPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ClassifyPayload>(completion.Value.Content, FoundryJsonOptions.Web);
        }
        catch (JsonException ex)
        {
            return Result<AiClassificationResult>.Failure(
                $"Foundry classify response was not valid JSON: {ex.Message}");
        }

        if (payload is null ||
            !Enum.TryParse<AiDocumentType>(payload.DocumentType, ignoreCase: true, out var documentType))
        {
            return Result<AiClassificationResult>.Failure(
                $"Foundry classify response named an unrecognized document type: " +
                $"'{payload?.DocumentType}'.");
        }

        var confidence = Math.Clamp(payload.Confidence, 0, 1);

        var metadata = FoundryCallMetadataFactory.Build(
            model, ClassifyPromptTemplate.Version, clock, documentText, completion.Value.Usage);

        return Result<AiClassificationResult>.Success(
            new AiClassificationResult(documentType, confidence, metadata));
    }
}
