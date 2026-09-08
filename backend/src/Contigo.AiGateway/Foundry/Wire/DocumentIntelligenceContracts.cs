using System.Text.Json.Serialization;

namespace Contigo.AiGateway.Foundry.Wire;

/// <summary>
/// Azure AI Document Intelligence <c>documentModels/{model}:analyze</c> long-running-operation
/// wire shapes (<see cref="FoundryOcrClient"/>). The submit call itself has no interesting
/// response body (202 + an <c>Operation-Location</c> header, read directly off
/// <see cref="System.Net.Http.HttpResponseMessage.Headers"/> — no DTO needed for that part); these
/// types cover the request body and the polled operation-status body.
/// </summary>
public sealed record DocumentIntelligenceAnalyzeRequest(
    [property: JsonPropertyName("base64Source")] string Base64Source);

public sealed record DocumentIntelligencePage(
    [property: JsonPropertyName("pageNumber")] int PageNumber);

/// <summary>
/// <c>content</c> concatenates every page's recognized text with a form-feed (<c>\f</c>) page
/// break between pages — the same convention <see cref="Fixtures.FixtureAiGateway"/>'s own
/// fixture OCR already uses for its deterministic multi-page splitting.
/// </summary>
public sealed record DocumentIntelligenceAnalyzeResult(
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("pages")] IReadOnlyList<DocumentIntelligencePage>? Pages);

public sealed record DocumentIntelligenceError(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("message")] string? Message);

/// <summary><c>status</c> is one of <c>notStarted</c>/<c>running</c>/<c>succeeded</c>/<c>failed</c>.</summary>
public sealed record DocumentIntelligenceOperationStatus(
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("analyzeResult")] DocumentIntelligenceAnalyzeResult? AnalyzeResult,
    [property: JsonPropertyName("error")] DocumentIntelligenceError? Error);
