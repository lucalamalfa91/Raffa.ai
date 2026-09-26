using System.Text.Json;
using System.Text.Json.Serialization;

namespace Raffa.AiGateway.Jev;

/// <summary>
/// OpenRouter's System One (Decisions) API wire shape, reconstructed from published documentation
/// and third-party write-ups (see <see cref="Configuration.AiGatewayJevOptions"/>'s own doc comment
/// for why this contract is unverified against a live call). One request may ask several typed
/// <paramref name="Questions"/> over the same <paramref name="State"/>; this pilot only ever asks
/// one (the document-type "choice").
/// </summary>
public sealed record JevSystemOneRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("questions")] IReadOnlyDictionary<string, JevQuestion> Questions);

/// <summary>One typed question. <paramref name="Type"/> is one of Jev's three primitives --
/// <c>"choice"</c> (pick one of <paramref name="Criteria"/>'s keys), <c>"noul"</c> (yes/no), or
/// <c>"score"</c> (a position on an ordered rubric) -- this pilot uses only <c>"choice"</c>.
/// <paramref name="Criteria"/> is required for <c>"choice"</c>, omitted for the other two.</summary>
public sealed record JevQuestion(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("instructions")] string Instructions,
    [property: JsonPropertyName("criteria")] IReadOnlyDictionary<string, string>? Criteria = null);

/// <summary>Answers, keyed the same as the request's <see cref="JevSystemOneRequest.Questions"/>.
/// Each value is kept as a raw <see cref="JsonElement"/> -- see
/// <see cref="JevAnswerReader.TryReadChoice"/> for why this pilot does not bind a strongly-typed
/// answer shape.</summary>
public sealed record JevSystemOneResponse(
    [property: JsonPropertyName("answers")] IReadOnlyDictionary<string, JsonElement>? Answers);

/// <summary>
/// Defensive reader for one "choice" answer. Two response shapes are both attested in the sources
/// this pilot was built from -- <c>answers.&lt;key&gt;.choice</c>/<c>.confidence</c> directly, or
/// nested one level deeper under <c>answers.&lt;key&gt;.route.choice</c>/<c>.confidence</c> -- so
/// this reads whichever level actually carries the fields instead of assuming one, and reports
/// exactly what was found (or not) when neither shape matches, per this pilot's "fail loud, not
/// silently wrong" rule for an unverified contract.
/// </summary>
public static class JevAnswerReader
{
    public static bool TryReadChoice(JsonElement answer, out string? choice, out double confidence)
    {
        choice = null;
        confidence = 0;

        var node = answer;
        if (answer.ValueKind == JsonValueKind.Object &&
            answer.TryGetProperty("route", out var route) &&
            route.ValueKind == JsonValueKind.Object)
        {
            node = route;
        }

        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("choice", out var choiceElement))
        {
            return false;
        }

        choice = choiceElement.ValueKind == JsonValueKind.String ? choiceElement.GetString() : null;
        if (choice is null)
        {
            return false;
        }

        if (node.TryGetProperty("confidence", out var confidenceElement) &&
            confidenceElement.ValueKind == JsonValueKind.Number)
        {
            confidence = confidenceElement.GetDouble();
        }

        return true;
    }
}
