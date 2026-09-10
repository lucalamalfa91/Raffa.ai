using System.Text.Json.Serialization;

namespace Contigo.AiGateway.Foundry.Wire;

/// <summary>
/// Azure AI Document Intelligence <c>documentModels/{model}:analyze</c> long-running-operation
/// wire shapes (<see cref="FoundryOcrClient"/>, api-version 2024-11-30). The submit call itself has
/// no interesting response body (202 + an <c>Operation-Location</c> header, read directly off
/// <see cref="System.Net.Http.HttpResponseMessage.Headers"/>); these types cover the request body
/// and the polled operation-status body.
/// </summary>
public sealed record DocumentIntelligenceAnalyzeRequest(
    [property: JsonPropertyName("base64Source")] string Base64Source);

/// <summary>A character range into <see cref="DocumentIntelligenceAnalyzeResult.Content"/>, in the
/// units the request's <c>stringIndexType</c> selected (<see cref="FoundryOcrClient"/> asks for
/// <c>utf16CodeUnit</c>, i.e. .NET <see cref="string"/> indices).</summary>
public sealed record DocumentIntelligenceSpan(
    [property: JsonPropertyName("offset")] int Offset,
    [property: JsonPropertyName("length")] int Length);

/// <summary>One page of the analyze result. Its text is not carried inline: it is the
/// concatenation of <see cref="Spans"/> sliced out of the top-level <c>content</c>.</summary>
public sealed record DocumentIntelligencePage(
    [property: JsonPropertyName("pageNumber")] int PageNumber,
    [property: JsonPropertyName("spans")] IReadOnlyList<DocumentIntelligenceSpan>? Spans = null);

/// <summary>
/// <c>content</c> is the whole document's recognized text in reading order with <b>no page
/// delimiter of its own</b>; page boundaries exist only as <see cref="DocumentIntelligencePage.Spans"/>
/// offsets into it (Microsoft Learn, "Analyze document response": "All elements specify their
/// position in the reader order via spans within this content string").
/// </summary>
public sealed record DocumentIntelligenceAnalyzeResult(
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("pages")] IReadOnlyList<DocumentIntelligencePage>? Pages,
    [property: JsonPropertyName("stringIndexType")] string? StringIndexType = null);

public sealed record DocumentIntelligenceError(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("innererror")] AzureInnerError? InnerError = null);

/// <summary><c>status</c> is one of <c>notStarted</c>/<c>running</c>/<c>succeeded</c>/<c>failed</c>.</summary>
public sealed record DocumentIntelligenceOperationStatus(
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("analyzeResult")] DocumentIntelligenceAnalyzeResult? AnalyzeResult,
    [property: JsonPropertyName("error")] DocumentIntelligenceError? Error);
