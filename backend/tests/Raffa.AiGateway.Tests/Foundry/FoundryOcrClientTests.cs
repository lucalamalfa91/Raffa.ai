using System.Net;
using System.Text;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Foundry;
using Raffa.AiGateway.Tests.TestSupport;

namespace Raffa.AiGateway.Tests.Foundry;

/// <summary>
/// Proves the `ocr` role against Azure AI Document Intelligence's documented 2024-11-30
/// long-running-operation contract: submit (202 + <c>Operation-Location</c>) then poll until a
/// terminal status honouring <c>Retry-After</c>, the page map derived from <c>pages[].spans</c>
/// over the concatenated <c>content</c> (the real response carries no page delimiter), page count
/// in metadata, transient-status retries, and the ADR-017 page-budget safety mechanism ("fail
/// visibly... never silently truncate").
/// </summary>
public class FoundryOcrClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri FoundryBaseAddress = new("https://fake-foundry.example.com/");
    private const string OperationLocation =
        "https://fake-foundry.example.com/documentintelligence/documentModels/prebuilt-read/analyzeResults/abc123?api-version=2024-11-30";

    private static (FoundryOcrClient Client, FakeHttpMessageHandler Handler, List<TimeSpan> Delays) CreateClient(
        AiGatewayOcrOptions? ocrOptions = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
    {
        var handler = new FakeHttpMessageHandler(responses);
        var httpClient = new HttpClient(handler) { BaseAddress = FoundryBaseAddress };
        var foundryOptions = new AiGatewayFoundryOptions
        {
            Endpoint = FoundryBaseAddress.ToString(),
            DocumentIntelligenceConnection = "conn-docint-raffa-dev",
        };
        var tokenProvider = new FoundryTokenProvider(new FakeTokenCredential());
        var delays = new List<TimeSpan>();
        var client = new FoundryOcrClient(
            httpClient,
            tokenProvider,
            foundryOptions,
            new AiGatewayModelOptions(),
            ocrOptions ?? new AiGatewayOcrOptions(),
            new FixedClock(Now),
            TestRetryPolicies.NoDelay(),
            delay ?? ((wait, _) =>
            {
                delays.Add(wait);
                return Task.CompletedTask;
            }));

        return (client, handler, delays);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> SubmitAccepted(params (string Name, string Value)[] extraHeaders) =>
        FakeHttpMessageHandler.Json(
            HttpStatusCode.Accepted, "{}", [("Operation-Location", OperationLocation), .. extraHeaders]);

    private static Func<HttpRequestMessage, HttpResponseMessage> Poll(
        string status, string? analyzeResultJson = null, params (string Name, string Value)[] extraHeaders) =>
        FakeHttpMessageHandler.Json(
            HttpStatusCode.OK,
            analyzeResultJson is null
                ? $$"""{"status":"{{status}}"}"""
                : $$"""{"status":"{{status}}","analyzeResult":{{analyzeResultJson}}}""",
            extraHeaders);

    /// <summary>
    /// The `prebuilt-layout` leg every successful <c>OcrAsync</c> call now also makes (ADR-017
    /// w18). Appended to every read-focused test below with a well-formed "no geometry" response —
    /// succeeds, reports no words — so those tests keep proving what they always proved (the text
    /// path) without depending on <see cref="FakeHttpMessageHandler"/>'s clamp-to-last-response
    /// behaviour for a call they are not about. Tests that DO care about geometry queue their own
    /// layout response instead of using this helper.
    /// </summary>
    private static Func<HttpRequestMessage, HttpResponseMessage>[] NoLayoutGeometry() =>
        [SubmitAccepted(), Poll("succeeded")];

    /// <summary>The real analyze-result shape: one concatenated <c>content</c> (no delimiter of its
    /// own — pages are joined with a newline here only because Read emits one between lines) and a
    /// <c>spans</c> offset/length per page in UTF-16 code units.</summary>
    private static string AnalyzeResult(params string[] pageTexts)
    {
        var content = string.Join("\n", pageTexts);
        var pages = new List<object>();
        var offset = 0;
        for (var i = 0; i < pageTexts.Length; i++)
        {
            pages.Add(new { pageNumber = i + 1, spans = new[] { new { offset, length = pageTexts[i].Length } } });
            offset += pageTexts[i].Length + 1;
        }

        return JsonSerializer.Serialize(new { apiVersion = "2024-11-30", modelId = "prebuilt-read", stringIndexType = "utf16CodeUnit", content, pages });
    }

    [Fact]
    public async Task Ocr_submits_then_polls_and_maps_pages_from_spans_over_the_content()
    {
        var (client, handler, _) = CreateClient(
            responses:
            [
                SubmitAccepted(),
                Poll("succeeded", AnalyzeResult("CONTRATTO QUADRO DI FORNITURA tra Rossi Software S.r.l. e Raffa Demo", "Il canone annuo è di EUR 48.000,00 — pagamento a 30 giorni.")),
                .. NoLayoutGeometry(),
            ]);

        var content = Encoding.UTF8.GetBytes("irrelevant raw bytes — the fake handler ignores them");
        var result = await client.OcrAsync(
            new AiOcrRequest("contract.pdf", "application/pdf", content), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(2, result.Value.Pages.Count);
        Assert.Equal(1, result.Value.Pages[0].PageNumber);
        Assert.Equal("CONTRATTO QUADRO DI FORNITURA tra Rossi Software S.r.l. e Raffa Demo", result.Value.Pages[0].Text);
        Assert.Equal(2, result.Value.Pages[1].PageNumber);
        Assert.Equal("Il canone annuo è di EUR 48.000,00 — pagamento a 30 giorni.", result.Value.Pages[1].Text);
        Assert.Equal("prebuilt-read", result.Value.Metadata.ModelId);
        Assert.Equal(Now, result.Value.Metadata.RespondedAtUtc);

        // 1 submit + 1 poll for `prebuilt-read`, then the same shape again for `prebuilt-layout`.
        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        var submitUrl = handler.Requests[0].RequestUri!.ToString();
        Assert.Contains("/documentintelligence/documentModels/prebuilt-read:analyze", submitUrl, StringComparison.Ordinal);
        Assert.Contains("_overload=analyzeDocument", submitUrl, StringComparison.Ordinal);
        Assert.Contains("api-version=2024-11-30", submitUrl, StringComparison.Ordinal);
        Assert.Contains("stringIndexType=utf16CodeUnit", submitUrl, StringComparison.Ordinal);
        Assert.Contains("\"base64Source\"", handler.RequestBodies[0]!, StringComparison.Ordinal);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.Equal(OperationLocation, handler.Requests[1].RequestUri!.ToString());

        foreach (var request in handler.Requests)
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(
                "conn-docint-raffa-dev",
                Assert.Single(request.Headers.GetValues("x-ms-document-intelligence-connection")));
        }
    }

    [Fact]
    public async Task Ocr_keeps_a_blank_page_in_place_so_page_numbers_stay_aligned()
    {
        var analyzeResult = JsonSerializer.Serialize(new
        {
            content = "first page text",
            pages = new object[]
            {
                new { pageNumber = 1, spans = new[] { new { offset = 0, length = 15 } } },
                new { pageNumber = 2, spans = Array.Empty<object>() },
            },
        });
        var (client, _, _) = CreateClient(responses: [SubmitAccepted(), Poll("succeeded", analyzeResult), .. NoLayoutGeometry()]);

        var result = await client.OcrAsync(
            new AiOcrRequest("scan.pdf", "application/pdf", "bytes"u8.ToArray()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Pages.Count);
        Assert.Equal("first page text", result.Value.Pages[0].Text);
        Assert.Equal(2, result.Value.Pages[1].PageNumber);
        Assert.Equal(string.Empty, result.Value.Pages[1].Text);
    }

    [Fact]
    public async Task Ocr_polls_more_than_once_while_the_operation_is_still_running()
    {
        var (client, handler, delays) = CreateClient(
            responses:
            [
                SubmitAccepted(),
                Poll("notStarted"),
                Poll("running"),
                Poll("succeeded", AnalyzeResult("only page")),
                .. NoLayoutGeometry(),
            ]);

        var result = await client.OcrAsync(
            new AiOcrRequest("contract.pdf", "application/pdf", "bytes"u8.ToArray()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(6, handler.Requests.Count); // 1 submit + 3 polls (read), then 1 submit + 1 poll (layout)
        Assert.Equal("only page", Assert.Single(result.Value.Pages).Text);
        Assert.Equal(4, delays.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), delays[0]);
        Assert.Equal(TimeSpan.FromSeconds(2), delays[1]);
        Assert.Equal(TimeSpan.FromSeconds(4), delays[2]);
        Assert.Equal(TimeSpan.FromSeconds(1), delays[3]); // layout's own poll loop restarts at the initial interval
    }

    [Fact]
    public async Task Ocr_waits_the_services_own_Retry_After_when_it_sends_one()
    {
        var (client, _, delays) = CreateClient(
            responses:
            [
                SubmitAccepted(("Retry-After", "3")),
                Poll("running", null, ("Retry-After", "7")),
                Poll("succeeded", AnalyzeResult("done")),
                .. NoLayoutGeometry(),
            ]);

        var result = await client.OcrAsync(
            new AiOcrRequest("contract.pdf", "application/pdf", "bytes"u8.ToArray()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        // The trailing 1s is the layout leg's own poll wait (no Retry-After on that submission).
        Assert.Equal([TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(1)], delays);
    }

    [Fact]
    public async Task Ocr_retries_a_throttled_submission_before_giving_up()
    {
        var (client, handler, _) = CreateClient(
            responses:
            [
                FakeHttpMessageHandler.Json(HttpStatusCode.TooManyRequests, """{"error":{"code":"429","message":"slow down"}}"""),
                SubmitAccepted(),
                Poll("succeeded", AnalyzeResult("page")),
                .. NoLayoutGeometry(),
            ]);

        var result = await client.OcrAsync(
            new AiOcrRequest("contract.pdf", "application/pdf", "bytes"u8.ToArray()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, handler.Requests.Count); // 429 + submit + poll (read), then submit + poll (layout)
    }

    [Fact]
    public async Task Ocr_fails_visibly_when_the_poll_budget_is_exhausted()
    {
        var (client, handler, _) = CreateClient(
            new AiGatewayOcrOptions { PollTimeoutSeconds = 1 },
            delay: (_, ct) => Task.Delay(TimeSpan.FromMilliseconds(350), ct),
            SubmitAccepted(),
            Poll("running"));

        var result = await client.OcrAsync(
            new AiOcrRequest("slow.pdf", "application/pdf", "bytes"u8.ToArray()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("did not complete within", result.Error, StringComparison.Ordinal);
        Assert.True(handler.Requests.Count >= 3);
    }

    [Fact]
    public async Task Ocr_fails_visibly_when_the_page_budget_is_exceeded_instead_of_truncating()
    {
        var (client, _, _) = CreateClient(
            new AiGatewayOcrOptions { MaxPagesPerDocument = 1 },
            null,
            SubmitAccepted(),
            Poll("succeeded", AnalyzeResult("page one", "page two")));

        var result = await client.OcrAsync(
            new AiOcrRequest("huge.pdf", "application/pdf", "bytes"u8.ToArray()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("budget", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ocr_fails_when_the_analysis_terminates_as_failed()
    {
        var (client, _, _) = CreateClient(
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
        var (client, handler, _) = CreateClient(
            responses: [FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, """{"error":{"code":"401","message":"no token"}}""")]);

        var result = await client.OcrAsync(
            new AiOcrRequest("contract.pdf", "application/pdf", "bytes"u8.ToArray()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("401", result.Error, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Ocr_fails_when_the_submission_has_no_Operation_Location_header()
    {
        var (client, _, _) = CreateClient(
            responses: [FakeHttpMessageHandler.Json(HttpStatusCode.Accepted, "{}")]);

        var result = await client.OcrAsync(
            new AiOcrRequest("contract.pdf", "application/pdf", "bytes"u8.ToArray()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("Operation-Location", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ocr_fails_without_content_and_never_calls_Foundry()
    {
        var (client, handler, _) = CreateClient(responses: [SubmitAccepted()]);

        var result = await client.OcrAsync(
            new AiOcrRequest("empty.pdf", "application/pdf", ReadOnlyMemory<byte>.Empty), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(handler.Requests);
    }

    /// <summary>ADR-017 w18 AC-1/AC-2/AC-3 end to end: `prebuilt-layout` is called alongside
    /// `prebuilt-read`, and its `words`/`polygon` geometry attaches to the matching page by page
    /// number without disturbing that page's text.</summary>
    [Fact]
    public async Task Ocr_also_calls_prebuilt_layout_and_attaches_word_and_polygon_geometry_by_page_number()
    {
        var layoutResult = JsonSerializer.Serialize(new
        {
            pages = new object[]
            {
                new
                {
                    pageNumber = 1,
                    words = new object[]
                    {
                        new { content = "CONTRATTO", polygon = new[] { 1.0, 1.0, 2.0, 1.0, 2.0, 2.0, 1.0, 2.0 } },
                        new { content = "QUADRO", polygon = new[] { 2.1, 1.0, 3.0, 1.0, 3.0, 2.0, 2.1, 2.0 } },
                    },
                },
                new { pageNumber = 2, words = Array.Empty<object>() },
            },
        });

        var (client, handler, _) = CreateClient(
            responses:
            [
                SubmitAccepted(),
                Poll("succeeded", AnalyzeResult("first page", "second page")),
                SubmitAccepted(),
                Poll("succeeded", layoutResult),
            ]);

        var result = await client.OcrAsync(
            new AiOcrRequest("contract.pdf", "application/pdf", "bytes"u8.ToArray()), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(2, result.Value.Pages.Count);

        var page1 = result.Value.Pages[0];
        Assert.Equal("first page", page1.Text);
        Assert.NotNull(page1.Words);
        Assert.Equal(2, page1.Words!.Count);
        Assert.Equal("CONTRATTO", page1.Words[0].Text);
        Assert.Equal([1.0, 1.0, 2.0, 1.0, 2.0, 2.0, 1.0, 2.0], page1.Words[0].Polygon);
        Assert.Equal("QUADRO", page1.Words[1].Text);

        var page2 = result.Value.Pages[1];
        Assert.Equal("second page", page2.Text); // the text path is untouched by geometry
        Assert.Null(page2.Words); // reported with an empty words array ⇒ still a null box, not []

        Assert.Equal(4, handler.Requests.Count);
        var layoutSubmitUrl = handler.Requests[2].RequestUri!.ToString();
        Assert.Contains("/documentintelligence/documentModels/prebuilt-layout:analyze", layoutSubmitUrl, StringComparison.Ordinal);
        Assert.Contains("api-version=2024-11-30", layoutSubmitUrl, StringComparison.Ordinal);
    }

    /// <summary>ADR-017 w18 AC-4, at the whole-call level: the text path `Classify`/`Extract` depend
    /// on must never fail because the geometry-only, best-effort `prebuilt-layout` call did.</summary>
    [Fact]
    public async Task Ocr_still_succeeds_with_text_only_when_the_layout_call_fails()
    {
        var (client, handler, _) = CreateClient(
            responses:
            [
                SubmitAccepted(),
                Poll("succeeded", AnalyzeResult("page one")),
                FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, """{"error":{"code":"401","message":"no token"}}"""),
            ]);

        var result = await client.OcrAsync(
            new AiOcrRequest("contract.pdf", "application/pdf", "bytes"u8.ToArray()), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var page = Assert.Single(result.Value.Pages);
        Assert.Equal("page one", page.Text);
        Assert.Null(page.Words);
        Assert.Equal(3, handler.Requests.Count); // read submit + read poll + layout submit (fails, swallowed)
    }

    [Fact]
    public void MapPages_refuses_duplicate_page_numbers_rather_than_guessing()
    {
        var analyzeResult = JsonSerializer.Deserialize<Raffa.AiGateway.Foundry.Wire.DocumentIntelligenceAnalyzeResult>(
            """{"content":"abcdef","pages":[{"pageNumber":1,"spans":[{"offset":0,"length":3}]},{"pageNumber":1,"spans":[{"offset":3,"length":3}]}]}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var mapped = FoundryOcrClient.MapPages(analyzeResult);

        Assert.True(mapped.IsFailure);
        Assert.Contains("duplicate page numbers", mapped.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void MapPages_clamps_a_span_that_overruns_the_content()
    {
        var analyzeResult = JsonSerializer.Deserialize<Raffa.AiGateway.Foundry.Wire.DocumentIntelligenceAnalyzeResult>(
            """{"content":"abc","pages":[{"pageNumber":1,"spans":[{"offset":1,"length":50}]}]}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var mapped = FoundryOcrClient.MapPages(analyzeResult);

        Assert.True(mapped.IsSuccess);
        Assert.Equal("bc", Assert.Single(mapped.Value).Text);
    }

    /// <summary>Tests required (task E23/F01/US01/T01): "MapPages widens geometry and tolerates
    /// null". This one proves the widening — a word's `content`/`polygon` from a separate
    /// `prebuilt-layout` result lands on <see cref="AiOcrPage.Words"/> for the matching page number,
    /// without disturbing that page's `prebuilt-read`-derived text.</summary>
    [Fact]
    public void MapPages_carries_words_and_polygon_geometry_from_a_separate_layout_result()
    {
        var analyzeResult = JsonSerializer.Deserialize<Raffa.AiGateway.Foundry.Wire.DocumentIntelligenceAnalyzeResult>(
            """{"content":"ab","pages":[{"pageNumber":1,"spans":[{"offset":0,"length":2}]}]}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var layoutResult = JsonSerializer.Deserialize<Raffa.AiGateway.Foundry.Wire.DocumentIntelligenceAnalyzeResult>(
            """{"pages":[{"pageNumber":1,"words":[{"content":"ab","polygon":[0,0,1,0,1,1,0,1]}]}]}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var mapped = FoundryOcrClient.MapPages(analyzeResult, layoutResult);

        Assert.True(mapped.IsSuccess);
        var page = Assert.Single(mapped.Value);
        Assert.Equal("ab", page.Text);
        var word = Assert.Single(page.Words!);
        Assert.Equal("ab", word.Text);
        Assert.Equal([0.0, 0, 1, 0, 1, 1, 0, 1], word.Polygon);
    }

    /// <summary>Tests required: "... and tolerates null" — the pre-w18 one-argument call shape (no
    /// layout result at all) still compiles and maps every page to a null box, never an error.
    /// </summary>
    [Fact]
    public void MapPages_tolerates_a_missing_layout_result_and_maps_every_page_to_a_null_box()
    {
        var analyzeResult = JsonSerializer.Deserialize<Raffa.AiGateway.Foundry.Wire.DocumentIntelligenceAnalyzeResult>(
            """{"content":"ab","pages":[{"pageNumber":1,"spans":[{"offset":0,"length":2}]}]}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var mapped = FoundryOcrClient.MapPages(analyzeResult);

        Assert.True(mapped.IsSuccess);
        Assert.Null(Assert.Single(mapped.Value).Words);
    }

    /// <summary>AC-4: a page the layout result has no entry for (a rasterised-before-this-lands
    /// page, or one `prebuilt-layout` simply did not report) still maps to a null box, never an
    /// error, even while a sibling page on the very same call does have geometry.</summary>
    [Fact]
    public void MapPages_maps_a_page_missing_from_the_layout_result_to_a_null_box()
    {
        var analyzeResult = JsonSerializer.Deserialize<Raffa.AiGateway.Foundry.Wire.DocumentIntelligenceAnalyzeResult>(
            """{"content":"ab\ncd","pages":[{"pageNumber":1,"spans":[{"offset":0,"length":2}]},{"pageNumber":2,"spans":[{"offset":3,"length":2}]}]}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var layoutResult = JsonSerializer.Deserialize<Raffa.AiGateway.Foundry.Wire.DocumentIntelligenceAnalyzeResult>(
            """{"pages":[{"pageNumber":1,"words":[{"content":"ab","polygon":[0,0,1,0,1,1,0,1]}]}]}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var mapped = FoundryOcrClient.MapPages(analyzeResult, layoutResult);

        Assert.True(mapped.IsSuccess);
        Assert.NotNull(mapped.Value[0].Words);
        Assert.Null(mapped.Value[1].Words);
    }
}
