using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Foundry;
using Raffa.AiGateway.Foundry.Prompts;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Jev;

/// <summary>
/// The `classify` role's Jev pilot backend (see <see cref="AiGatewayJevOptions"/>'s own doc
/// comment for scope and the unverified-contract caveat). Asks exactly one Jev "choice" question
/// over the same fixed <see cref="AiDocumentType"/> taxonomy and the same document-text prefix cap
/// Foundry's own <see cref="FoundryClassifyClient"/> uses, so a result recorded here is comparable
/// to a Foundry result for the same document -- this pilot's whole point.
/// </summary>
public sealed class JevClassifyClient(JevHttpJsonClient httpClient, AiGatewayJevOptions options, IClock clock)
{
    private const string QuestionKey = "documentType";

    /// <summary>Bump when the question text/criteria below changes -- recorded in every
    /// <see cref="AiCallMetadata.PromptVersion"/> this client produces, the same convention
    /// <see cref="ClassifyPromptTemplate.Version"/> follows for the Foundry path.</summary>
    public const string Version = "jev-classify-v1";

    public async Task<Result<AiClassificationResult>> ClassifyAsync(
        AiClassificationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentText))
        {
            return Result<AiClassificationResult>.Failure("Classification requires non-empty document text.");
        }

        var maxChars = Math.Max(1, options.ClassifyMaxInputChars);
        var documentText = request.DocumentText.Length <= maxChars
            ? request.DocumentText
            : request.DocumentText[..maxChars];

        var criteria = ClassifyPromptTemplate.Labels.ToDictionary(
            label => label,
            label => ClassifyPromptTemplate.Glosses[Enum.Parse<AiDocumentType>(label)]);

        var wireRequest = new JevSystemOneRequest(
            Model: options.Model,
            State: documentText,
            Questions: new Dictionary<string, JevQuestion>
            {
                [QuestionKey] = new(
                    Type: "choice",
                    Instructions:
                        "Classify this business document into exactly one of the given types. The " +
                        "document may be written in any language; classify it on its meaning, never " +
                        "on its language or a file name. Decide by what the document does, not by " +
                        "its title.",
                    Criteria: criteria),
            });

        var sent = await httpClient
            .PostAsync<JevSystemOneRequest, JevSystemOneResponse>(options.Endpoint, wireRequest, cancellationToken)
            .ConfigureAwait(false);

        if (sent.IsFailure)
        {
            return Result<AiClassificationResult>.Failure(sent.Error);
        }

        var answers = sent.Value.Answers;
        if (answers is null || !answers.TryGetValue(QuestionKey, out var answer))
        {
            return Result<AiClassificationResult>.Failure(
                $"Jev classify response carried no '{QuestionKey}' answer. This client's response " +
                "contract is unverified against a live account -- see AiGatewayJevOptions's own " +
                "doc comment.");
        }

        if (!JevAnswerReader.TryReadChoice(answer, out var choice, out var confidence) ||
            !Enum.TryParse<AiDocumentType>(choice, ignoreCase: true, out var documentType))
        {
            return Result<AiClassificationResult>.Failure(
                $"Jev classify response named an unrecognized or unparseable document type: '{choice}'.");
        }

        var model = new AiModelSelection(options.Model, options.Model);
        var metadata = FoundryCallMetadataFactory.Build(model, Version, clock, documentText);

        return Result<AiClassificationResult>.Success(
            new AiClassificationResult(documentType, Math.Clamp(confidence, 0, 1), metadata));
    }
}
