using System.Text;
using System.Text.Json;
using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Contracts;
using Contigo.AiGateway.Foundry.Prompts;
using Contigo.SharedKernel;

namespace Contigo.AiGateway.Foundry;

/// <summary>
/// `answer` role (ADR-004 amendment / ADR-024: structured JSON, no tools, no grounding,
/// temperature &lt;= 0.2 whenever one is sent — see <see cref="AiModelSelection.Temperature"/>).
/// Grounds in whichever of <see cref="AiAnswerRequest.PackJson"/> (ADR-024's context-pack shape)
/// and/or <see cref="AiAnswerRequest.Evidence"/> (the pre-existing evidence-list shape
/// <c>Contigo.Chat.Application.RagAnswerService</c> still sends) the caller supplied — see
/// <see cref="AiAnswerRequest"/>'s own doc comment for why both are supported side by side.
///
/// Mirrors <c>Fixtures.FixtureAiGateway.AnswerAsync</c>'s own "abstain rather than call the model
/// on truly empty input" short-circuit: nothing to ground in (<see cref="AiAnswerRequest.PackJson"/>
/// blank AND <see cref="AiAnswerRequest.Evidence"/> empty) never reaches Foundry at all.
/// </summary>
public sealed class FoundryAnswerClient(
    FoundryChatCompletionsClient chatClient,
    AiGatewayModelOptions modelOptions,
    IClock clock)
{
    private sealed record AnswerPayload(
        bool CanDetermine,
        string? AnswerMarkdown,
        IReadOnlyList<string>? CitationKeys,
        IReadOnlyList<string>? ActionKeys,
        string? AbstainReason,
        IReadOnlyList<string>? FollowUps);

    public async Task<Result<AiAnswerResult>> AnswerAsync(
        AiAnswerRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return Result<AiAnswerResult>.Failure("A question is required.");
        }

        var model = modelOptions.Answer;
        var hasPack = !string.IsNullOrWhiteSpace(request.PackJson);

        if (!hasPack && request.Evidence.Count == 0)
        {
            // Same "authorized retrieval found nothing -> cannot determine" rule as the fixture
            // and as ADR-011 (authorization before retrieval): an empty pack and empty evidence
            // both mean there is genuinely nothing to answer from — abstain without spending a
            // model call on it.
            var abstained = new AiAnswerResult(
                CanDetermine: false,
                Answer: null,
                Citations: [],
                Metadata: FoundryCallMetadataFactory.Build(
                    model, AnswerPersonaPrompt.Version, clock, request.Question),
                AnswerMarkdown: null,
                CitationKeys: [],
                ActionKeys: [],
                AbstainReason: "No evidence or context pack was supplied to ground an answer in.",
                FollowUps: []);

            return Result<AiAnswerResult>.Success(abstained);
        }

        var systemPrompt = string.IsNullOrWhiteSpace(request.SystemPrompt)
            ? AnswerPersonaPrompt.DefaultSystemPrompt
            : request.SystemPrompt;

        var userPrompt = BuildUserPrompt(request);

        var completion = await chatClient.CompleteAsync(
                "Answer",
                model,
                systemPrompt,
                userPrompt,
                schemaName: "contigo_ask_answer",
                AnswerPersonaPrompt.Schema,
                cancellationToken)
            .ConfigureAwait(false);

        if (completion.IsFailure)
        {
            return Result<AiAnswerResult>.Failure(completion.Error);
        }

        AnswerPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<AnswerPayload>(completion.Value.Content, FoundryJsonOptions.Web);
        }
        catch (JsonException ex)
        {
            return Result<AiAnswerResult>.Failure($"Foundry answer response was not valid JSON: {ex.Message}");
        }

        if (payload is null)
        {
            return Result<AiAnswerResult>.Failure("Foundry answer response parsed to null.");
        }

        var metadata = FoundryCallMetadataFactory.Build(
            model, AnswerPersonaPrompt.Version, clock, userPrompt, completion.Value.Usage);

        // Legacy Citations stays meaningful only for the evidence-only path: it is a resolved
        // {documentId, page, section} pointer, which only Evidence carries — CitationKeys is the
        // ADR-024 replacement for the pack path, resolved by the caller that owns the pack.
        IReadOnlyList<AiCitation> citations = !payload.CanDetermine || hasPack
            ? []
            : [.. request.Evidence.Select(e => new AiCitation(e.DocumentId, e.Page, e.Section))];

        var result = new AiAnswerResult(
            CanDetermine: payload.CanDetermine,
            Answer: payload.CanDetermine ? payload.AnswerMarkdown : null,
            Citations: citations,
            Metadata: metadata,
            AnswerMarkdown: payload.AnswerMarkdown,
            CitationKeys: payload.CitationKeys ?? [],
            ActionKeys: payload.ActionKeys ?? [],
            AbstainReason: payload.AbstainReason,
            FollowUps: payload.FollowUps ?? []);

        return Result<AiAnswerResult>.Success(result);
    }

    private static string BuildUserPrompt(AiAnswerRequest request)
    {
        var builder = new StringBuilder();
        builder.Append("Question: ").AppendLine(request.Question);

        if (!string.IsNullOrWhiteSpace(request.PackJson))
        {
            builder.AppendLine().AppendLine("Context pack (JSON):").AppendLine(request.PackJson);
        }

        if (request.Evidence.Count > 0)
        {
            builder.AppendLine().AppendLine("Evidence:");
            foreach (var evidence in request.Evidence)
            {
                builder.Append("- [").Append(evidence.DocumentId).Append(']');
                if (evidence.Page is { } page)
                {
                    builder.Append(" p.").Append(page);
                }

                if (!string.IsNullOrWhiteSpace(evidence.Section))
                {
                    builder.Append(" §").Append(evidence.Section);
                }

                builder.Append(": ").AppendLine(evidence.Text);
            }
        }

        return builder.ToString();
    }
}
