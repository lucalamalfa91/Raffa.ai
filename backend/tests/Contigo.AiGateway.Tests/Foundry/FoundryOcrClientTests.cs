using System.Net;
using System.Text;
using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Contracts;
using Contigo.AiGateway.Foundry;
using Contigo.AiGateway.Tests.TestSupport;

namespace Contigo.AiGateway.Tests.Foundry;

/// <summary>
/// Proves task E13/F01/US01/T02's `ocr` role against Azure AI Document Intelligence's
/// long-running-operation contract: submit (202 + <c>Operation-Location</c>) then poll until a
/// terminal status, full-document page map, page count in metadata, and the ADR-017 page-budget
/// safety mechanism ("fail visibly... never silently truncate").
/// </summary>
public class FoundryOcrClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri FoundryBaseAddress = new("https://fake-foundry.example.com/");
    private const string OperationLocation =
        "https://fake-foundry.example.com/documentintelligence/documentModels/prebuilt-read/analyzeResults/abc123?api-version=2024-11-30";

    private static (FoundryOcrClient Client, FakeHttpMessageHandler Handler) CreateClient(
        AiGatewayOcrOptions? ocrOptions = null, params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
    {
        var handler = new FakeHttpMessageHandler(responses);
        var httpClient = new HttpClient(handler) { BaseAddress = FoundryBaseAddress };
        var foundryOptions = new AiGatewayFoundryOptions
        {
            Endpoint = FoundryBaseAddress.ToString(),
            DocumentIntelligenceConnection = "conn-docint-contigo-dev",
        };
        var tokenProvider = new FoundryTokenProvider(new FakeTokenCredential());
        var client = new FoundryOcrClient(
            httpClient,
            tokenProvider,
            foundryOptions,
            new AiGatewayModelOptions(),
            ocrOptions ?? new AiGatewayOcrOptions(),
            new FixedClock(Now),
            pollInterval: TimeSpan.Zero);

        return (client, handler);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> SubmitAccepted() =>
        FakeHttpMessageHandler.Json(HttpStatusCode.Accepted, "{}", ("Operation-Location", OperationLocation));

    private static Func<HttpRequestMessage, HttpResponseMessage> Poll(string status, string? analyzeResultJson = null) =>
        FakeHttpMessageHandler.Json(
            HttpStatusCode.OK,
            analyzeResultJson is null
                ? $$"""{"status":"{{status}}"}"""
                : $$"""{"status":"{{status}}","analyzeResult":{{analyzeResultJson}}}""");

    [Fact]
    public async Task Ocr_submits_then_polls_once_and_returns_a_page_map_on_success()
    {
        var (client, handler) = CreateClient(
            responses:
            [
                SubmitAccepted(),
                Poll("succeeded", """{"content":"page one\fpage two","pages":[{"pageNumber":1},{"pageNumber":2}]}"""),
            ]);

        var content = Encoding.UTF8.GetBytes("irrelevant raw bytes — the fake handler ignores them");
        var result = await client.OcrAsync(
            new AiOcrRequest("contract.pdf", "application/pdf", content), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Pages.Count);
        Assert.Equal(1, result.Value.Pages[0].PageNumber);
        Assert.Equal("page one", result.Value.Pages[0].Text);
        Assert.Equal("page two", result.Value.Pages[1].Text);
        Assert.Equal("prebuilt-read", result.Value.Metadata.ModelId);
        Assert.Equal(Now, result.Value.Metadata.RespondedAtUtc);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Contains(
            "/documentintelligence/documentModels/prebuilt-read:analyze", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.Equal(OperationLocation, handler.Requests[1].RequestUri!.ToString());

        foreach (var request in handler.Requests)
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(
                "conn-docint-contigo-dev",
                Assert.Single(request.Headers.GetValues("x-ms-document-intelligence-connection")));
        }
    }

    [Fact]
    public async Task Ocr_polls_more_than_once_while_the_operation_is_still_running()
    {
        var (client, handler) = CreateClient(
            responses:
            [
                SubmitAccepted(),
                Poll("running"),
                Poll("running"),
                Poll("succeeded", """{"content":"only page","pages":[{"pageNumber":1}]}"""),
            ]);

        var result = await client.OcrAsync(
            new AiOcrRequest("contract.pdf", "application/pdf", "bytes"u8.ToArray()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, handler.Requests.Count); // 1 submit + 3 polls
        Assert.Equal("only page", Assert.Single(result.Value.Pages).Text);
    }

    [Fact]
    public async Task Ocr_fails_visibly_when_the_page_budget_is_exceeded_instead_of_truncating()
    {
        var (client, _) = CreateClient(
            new AiGatewayOcrOptions { MaxPagesPerDocument = 1 },
            SubmitAccepted(),
            Poll("succeeded", """{"content":"page one\fpage two","pages":[{"pageNumber":1},{"pageNumber":2}]}"""));

        var result = await client.OcrAsync(
            new AiOcrRequest("huge.pdf", "application/pdf", "bytes"u8.ToArray()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("budget", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ocr_fails_when_the_analysis_terminates_as_failed()
    {
        var (client, _) = CreateClient(
            responses:
            [
                SubmitAccepted(),
                FakeHttpMessageHandler.Json(
                    HttpStatusCode.OK,
                    """{"status":"failed","error":{"code":"InvalidContent","message":"unreadable file"}}"""),
            ]);

        var result = await client.OcrAsync(
            new AiOcrRequest("bad.pdf", "application/pdf", "bytes"u8.ToArray()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("InvalidContent", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ocr_fails_when_the_submission_itself_is_rejected()
    {
        var (client, _) = CreateClient(
            responses: [FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}")]);

        var result = await client.OcrAsync(
            new AiOcrRequest("contract.pdf", "application/pdf", "bytes"u8.ToArray()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("401", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ocr_fails_when_the_submission_has_no_Operation_Location_header()
    {
        var (client, _) = CreateClient(
            responses: [FakeHttpMessageHandler.Json(HttpStatusCode.Accepted, "{}")]);

        var result = await client.OcrAsync(
            new AiOcrRequest("contract.pdf", "application/pdf", "bytes"u8.ToArray()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("Operation-Location", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ocr_fails_without_content_and_never_calls_Foundry()
    {
        var (client, handler) = CreateClient(responses: [SubmitAccepted()]);

        var result = await client.OcrAsync(
            new AiOcrRequest("empty.pdf", "application/pdf", ReadOnlyMemory<byte>.Empty), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(handler.Requests);
    }
}
