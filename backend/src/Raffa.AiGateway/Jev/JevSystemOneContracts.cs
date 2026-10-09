using System.Text.Json.Serialization;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Foundry;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Jev;

/// <summary>
/// TypeSafe's System One wire shape (<c>POST /v1/systemone</c> on api.typesafe.ai, and the identical
/// <c>POST /api/v1/systemone</c> on OpenRouter -- https://docs.typesafe.ai/api). One request asks
/// several typed <paramref name="Questions"/> over the same <paramref name="State"/>; every question
/// is evaluated independently, so asking them together changes no answer, only the cost and latency
/// (https://docs.typesafe.ai/cookbooks/parallel_questions).
/// </summary>
/// <param name="State">A string for one passage of text, or a JSON object whose named fields the
/// questions can refer to -- TypeSafe's own advice is an object whenever the state has parts.</param>
public sealed record JevSystemOneRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("state")] object State,
    [property: JsonPropertyName("questions")] IReadOnlyDictionary<string, JevQuestion> Questions);

/// <summary>One typed question. <paramref name="Type"/> is one of Jev's three primitives --
/// <c>"choice"</c> (pick one of <paramref name="Criteria"/>'s keys, at most 255), <c>"noul"</c>
/// (yes/no) or <c>"score"</c> (a position on an ordered rubric). <paramref name="Criteria"/> is
/// required for <c>"choice"</c> and omitted for the others.</summary>
public sealed record JevQuestion(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("instructions")] string Instructions,
    [property: JsonPropertyName("criteria")] IReadOnlyDictionary<string, string>? Criteria = null);

/// <summary>Token usage of one call. Output tokens are free; <see cref="Cost"/> is OpenRouter's USD
/// figure and is absent when the call goes to TypeSafe directly.</summary>
public sealed record JevUsage(
    [property: JsonPropertyName("input_tokens")] int InputTokens,
    [property: JsonPropertyName("output_tokens")] int OutputTokens,
    [property: JsonPropertyName("cost")] double? Cost = null);

/// <summary>One answer, flat as documented: <c>{type, choice, probabilities, confidence}</c> for a
/// Choice, <c>{type, noul}</c> for a Noul, <c>{type, score, legend, probabilities, confidence}</c>
/// for a Score. A Noul carries no confidence of its own -- its probability already is the signal.</summary>
public sealed record JevAnswer(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("choice")] string? Choice,
    [property: JsonPropertyName("probabilities")] IReadOnlyDictionary<string, double>? Probabilities,
    [property: JsonPropertyName("confidence")] double? Confidence,
    [property: JsonPropertyName("noul")] double? Noul,
    [property: JsonPropertyName("score")] double? Score);

/// <summary>Answers keyed like the request's questions, plus the model that actually served the call
/// (a versioned id such as <c>typesafe/jev-1.13-20260917</c>, not the alias that was asked for) and
/// the token usage.</summary>
public sealed record JevSystemOneResponse(
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("answers")] IReadOnlyDictionary<string, JevAnswer>? Answers,
    [property: JsonPropertyName("usage")] JevUsage? Usage);

/// <summary>What a Choice answer gives the caller: the option, TypeSafe's own
/// <c>confidence</c> and the whole distribution (kept for calibration, see
/// <see cref="JevHttpJsonClient.LogDecisions"/>).</summary>
public sealed record JevChoice(string Choice, double Confidence, IReadOnlyDictionary<string, double> Probabilities);

public static class JevAnswers
{
    /// <summary>Reads a Choice answer. A missing <c>confidence</c> reads as 0 -- it then fails every
    /// acceptance threshold, the safe direction (TypeSafe marks it optional on the wire).</summary>
    public static bool TryReadChoice(
        IReadOnlyDictionary<string, JevAnswer>? answers, string questionKey, out JevChoice? choice)
    {
        choice = null;
        if (answers is null || !answers.TryGetValue(questionKey, out var answer) ||
            string.IsNullOrWhiteSpace(answer.Choice))
        {
            return false;
        }

        choice = new JevChoice(
            answer.Choice,
            Math.Clamp(answer.Confidence ?? 0, 0, 1),
            answer.Probabilities ?? new Dictionary<string, double>());
        return true;
    }

    /// <summary>
    /// The call's reproducibility metadata: <see cref="AiCallMetadata.ModelId"/> is the configured
    /// model, <see cref="AiCallMetadata.ModelVersion"/> the versioned id the response reports as
    /// having answered (so a later alias move is visible in the audit), and
    /// <see cref="AiCallMetadata.Usage"/> the input/output tokens -- the figures a time/quality/cost
    /// comparison against Foundry needs.
    /// </summary>
    public static AiCallMetadata Metadata(
        string configuredModel, JevSystemOneResponse response, string promptVersion, IClock clock, string input) =>
        FoundryCallMetadataFactory.Build(
            new AiModelSelection(configuredModel, string.IsNullOrWhiteSpace(response.Model) ? configuredModel : response.Model),
            promptVersion,
            clock,
            input,
            response.Usage is { } usage ? new AiTokenUsage(usage.InputTokens, usage.OutputTokens) : null);
}
