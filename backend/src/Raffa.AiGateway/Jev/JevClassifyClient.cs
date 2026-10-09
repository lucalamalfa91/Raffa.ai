using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
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
    private const string ReversedQuestionKey = "documentTypeReversed";

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

        const string instructions =
            "Classify this business document into exactly one of the given types. The " +
            "document may be written in any language; classify it on its meaning, never " +
            "on its language or a file name. Decide by what the document does, not by " +
            "its title.";

        var questions = new Dictionary<string, JevQuestion>
        {
            [QuestionKey] = new("choice", instructions, criteria),
        };

        if (options.CheckOptionOrder)
        {
            // Same question, options in the opposite order, same request: see
            // AiGatewayJevOptions.CheckOptionOrder.
            questions[ReversedQuestionKey] = new("choice", instructions, JevOptionOrder.Reversed(criteria));
        }

        var wireRequest = new JevSystemOneRequest(options.Model, documentText, questions);

        var sent = await httpClient
            .PostAsync<JevSystemOneRequest, JevSystemOneResponse>(options.Endpoint, wireRequest, cancellationToken)
            .ConfigureAwait(false);

        if (sent.IsFailure)
        {
            return Result<AiClassificationResult>.Failure(sent.Error);
        }

        httpClient.LogDecisions("classify", sent.Value);

        if (!JevAnswers.TryReadChoice(sent.Value.Answers, QuestionKey, out var answer) ||
            !Enum.TryParse<AiDocumentType>(answer!.Choice, ignoreCase: true, out var documentType))
        {
            return Result<AiClassificationResult>.Failure(
                $"Jev classify response carried no usable '{QuestionKey}' answer " +
                $"(choice: '{answer?.Choice}'). A Choice answer is {{type, choice, probabilities, confidence}} " +
                "-- https://docs.typesafe.ai/api#choice-answer.");
        }

        var confidence = answer.Confidence;
        if (options.CheckOptionOrder)
        {
            // Both orders must name the same type. A disagreement is the order bias speaking, so the
            // result is reported as "not sure at all" (confidence 0) and the gateway decorator hands
            // the document to Foundry.
            confidence = JevAnswers.TryReadChoice(sent.Value.Answers, ReversedQuestionKey, out var reversed) &&
                         string.Equals(reversed!.Choice, answer.Choice, StringComparison.OrdinalIgnoreCase)
                ? Math.Min(answer.Confidence, reversed.Confidence)
                : 0;
        }

        return Result<AiClassificationResult>.Success(new AiClassificationResult(
            documentType,
            confidence,
            JevAnswers.Metadata(options.Model, sent.Value, Version, clock, documentText)));
    }
}
