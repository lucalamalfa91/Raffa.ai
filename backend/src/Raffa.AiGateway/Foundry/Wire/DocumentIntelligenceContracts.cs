using System.Text.Json.Serialization;

namespace Raffa.AiGateway.Foundry.Wire;

/// <summary>
/// Azure AI Document Intelligence <c>documentModels/{model}:analyze</c> long-running-operation
/// wire shapes (<see cref="FoundryOcrClient"/>, api-version 2024-11-30). The submit call itself has
/// no interesting response body (202 + an <c>Operation-Location</c> header, read directly off
/// <see cref="System.Net.Http.HttpResponseMessage.Headers"/>); these types cover the request body
/// and the polled operation-status body. ADR-017 w18: the same operation shape now backs two
/// separate calls — <c>prebuilt-read</c> (text, via <see cref="DocumentIntelligencePage.Spans"/>)
/// and <c>prebuilt-layout</c> (geometry, via <see cref="DocumentIntelligencePage.Words"/>) — so one
/// <see cref="DocumentIntelligencePage"/> shape covers both and <see cref="FoundryOcrClient.MapPages"/>
/// merges the two results by <see cref="DocumentIntelligencePage.PageNumber"/>.
/// </summary>
public sealed record DocumentIntelligenceAnalyzeRequest(
    [property: JsonPropertyName("base64Source")] string Base64Source);

/// <summary>A character range into <see cref="DocumentIntelligenceAnalyzeResult.Content"/>, in the
/// units the request's <c>stringIndexType</c> selected (<see cref="FoundryOcrClient"/> asks for
/// <c>utf16CodeUnit</c>, i.e. .NET <see cref="string"/> indices).</summary>
public sealed record DocumentIntelligenceSpan(
    [property: JsonPropertyName("offset")] int Offset,
    [property: JsonPropertyName("length")] int Length);

/// <summary>
/// One recognized word within a page (<c>prebuilt-layout</c> only — <c>prebuilt-read</c> does not
/// return this array). <see cref="Polygon"/> is the word's pixel-space bounding polygon exactly as
/// Azure returns it on the wire: a flat, clockwise list of (x, y) pairs — 8 numbers for the common
/// quadrilateral case (x1,y1,x2,y2,x3,y3,x4,y4) — kept as a raw number list rather than reshaped
/// into point objects here, so this project stays a thin wire twin (ADR-002) instead of owning
/// geometry math that belongs to whichever later consumer builds a box from it.
/// </summary>
public sealed record DocumentIntelligenceWord(
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("polygon")] IReadOnlyList<double>? Polygon);

/// <summary>One page of the analyze result. Its text is not carried inline: it is the
/// concatenation of <see cref="Spans"/> sliced out of the top-level <c>content</c>
/// (<c>prebuilt-read</c>). <see cref="Words"/> (<c>prebuilt-layout</c>, ADR-017 w18) carries the
/// same page's word-level geometry instead — a page populates whichever fields the model that
/// produced it reports; <see cref="FoundryOcrClient.MapPages"/> is what merges one page's text
/// with the other's geometry.</summary>
public sealed record DocumentIntelligencePage(
    [property: JsonPropertyName("pageNumber")] int PageNumber,
    [property: JsonPropertyName("spans")] IReadOnlyList<DocumentIntelligenceSpan>? Spans = null,
    [property: JsonPropertyName("words")] IReadOnlyList<DocumentIntelligenceWord>? Words = null);

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
