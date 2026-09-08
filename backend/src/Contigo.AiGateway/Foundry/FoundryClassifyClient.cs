using System.Text.Json;
using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Contracts;
using Contigo.AiGateway.Foundry.Prompts;
using Contigo.SharedKernel;

namespace Contigo.AiGateway.Foundry;

/// <summary>
/// `classify` role (ADR-004; ADR-024: "classify is reused for the document admission gate and for
/// the Ask domain gate with fixed label sets"). Temperature 0 — classification is the one role
/// where determinism, not creativity, is the goal.
/// </summary>
public sealed class FoundryClassifyClient(
    FoundryChatCompletionsClient chatClient, AiGatewayModelOptions modelOptions, IClock clock)
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

        var completion = await chatClient.CompleteAsync(
                model.ModelId,
                ClassifyPromptTemplate.SystemPrompt,
                request.DocumentText,
                temperature: 0,
                schemaName: "contigo_document_classification",
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
            payload = JsonSerializer.Deserialize<ClassifyPayload>(completion.Value, FoundryJsonOptions.Web);
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

        var metadata = FoundryCallMetadataFactory.Build(
            model, ClassifyPromptTemplate.Version, clock, request.DocumentText);

        return Result<AiClassificationResult>.Success(
            new AiClassificationResult(documentType, payload.Confidence, metadata));
    }
}
