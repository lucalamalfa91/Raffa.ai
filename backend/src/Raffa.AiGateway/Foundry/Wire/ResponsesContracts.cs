using System.Text.Json;
using System.Text.Json.Serialization;

namespace Raffa.AiGateway.Foundry.Wire;

/// <summary>
/// The Azure OpenAI <b>Responses</b> API (`openai/v1/responses`) wire shape the research role uses
/// (ADR-030). This is the only request type in the gateway that carries a <c>tools</c> member, and
/// the only tool it can carry is the hosted web search: <see cref="ResponsesTool.Type"/> is written
/// by <see cref="FoundryResearchClient"/> alone. <c>ChatCompletionRequest</c> (every other role)
/// still has no such property at all, so the R-AI-03 compliance test on the answer role stands.
/// </summary>
public sealed record ResponsesTool(
    [property: JsonPropertyName("type")] string Type);

public sealed record ResponsesTextFormat(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("strict")] bool Strict,
    [property: JsonPropertyName("schema")] JsonElement Schema);

public sealed record ResponsesText(
    [property: JsonPropertyName("format")] ResponsesTextFormat Format);

public sealed record ResponsesRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("instructions")] string Instructions,
    [property: JsonPropertyName("input")] string Input,
    [property: JsonPropertyName("tools")] IReadOnlyList<ResponsesTool> Tools,
    [property: JsonPropertyName("text")] ResponsesText Text,
    [property: JsonPropertyName("max_output_tokens")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? MaxOutputTokens);

public sealed record ResponsesAnnotation(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("title")] string? Title);

public sealed record ResponsesContent(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("annotations")] IReadOnlyList<ResponsesAnnotation>? Annotations);

public sealed record ResponsesOutputItem(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("content")] IReadOnlyList<ResponsesContent>? Content);

public sealed record ResponsesUsage(
    [property: JsonPropertyName("input_tokens")] int InputTokens,
    [property: JsonPropertyName("output_tokens")] int OutputTokens);

public sealed record ResponsesResponse(
    [property: JsonPropertyName("output")] IReadOnlyList<ResponsesOutputItem>? Output,
    [property: JsonPropertyName("usage")] ResponsesUsage? Usage,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("model")] string? Model);
