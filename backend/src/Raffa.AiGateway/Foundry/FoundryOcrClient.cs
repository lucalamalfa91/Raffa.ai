using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Foundry.Wire;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Foundry;

/// <summary>
/// `ocr` role (ADR-017, amended 2026-09-09: every PDF and image goes through Azure AI Document
/// Intelligence <c>prebuilt-read</c>) against the documented 2024-11-30 long-running-operation
/// contract: <c>POST documentModels/{model}:analyze</c> with the document as <c>base64Source</c>
/// (202 + <c>Operation-Location</c>), then <c>GET</c> that URL until <c>status</c> is
/// <c>succeeded</c>/<c>failed</c>, waiting the service's own <c>Retry-After</c> when present and an
/// exponential interval otherwise, inside <see cref="AiGatewayOcrOptions.PollTimeoutSeconds"/>.
///
/// The page map comes from <c>analyzeResult.pages[].spans</c> sliced out of the top-level
/// <c>content</c> (requested in <c>utf16CodeUnit</c> so offsets are .NET string indices): the API
/// carries no page delimiter of its own, so splitting <c>content</c> on any character would collapse
/// every multi-page document into one page and every citation onto page 1. The ADR-017 page budget
/// is enforced on the page count the service actually reports.
/// </summary>
public sealed class FoundryOcrClient(
    HttpClient httpClient,
    FoundryTokenProvider tokenProvider,
    AiGatewayFoundryOptions foundryOptions,
    AiGatewayModelOptions modelOptions,
    AiGatewayOcrOptions ocrOptions,
    IClock clock,
    FoundryRetryPolicy? retryPolicy = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private const string ApiVersion = "2024-11-30";
    private const string PromptVersion = "foundry-ocr-v2";

    private static readonly MediaTypeHeaderValue JsonContentType = new("application/json") { CharSet = "utf-8" };

    private readonly FoundryRetryPolicy _retryPolicy = retryPolicy ?? new FoundryRetryPolicy(new AiGatewayResilienceOptions());
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken)
    {
        if (request.Content.IsEmpty)
        {
            return Result<AiOcrResult>.Failure("Ocr requires non-empty document content.");
        }

        var model = modelOptions.Ocr;
        var analyzeUrl =
            $"documentintelligence/documentModels/{Uri.EscapeDataString(model.ModelId)}:analyze" +
            $"?_overload=analyzeDocument&api-version={ApiVersion}&stringIndexType=utf16CodeUnit";

        // Base64 once; the retry policy's factory re-wraps the same bytes per attempt.
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new DocumentIntelligenceAnalyzeRequest(Convert.ToBase64String(request.Content.Span)),
            FoundryJsonOptions.Web);

        var submitted = await _retryPolicy.SendAsync(
                httpClient,
                async token =>
                {
                    var submitRequest = new HttpRequestMessage(HttpMethod.Post, analyzeUrl)
                    {
                        Content = new ByteArrayContent(payload) { Headers = { ContentType = JsonContentType } },
                    };
                    await AttachAuthAsync(submitRequest, token).ConfigureAwait(false);
                    return submitRequest;
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (submitted.IsFailure)
        {
            return Result<AiOcrResult>.Failure(submitted.Error);
        }

        string operationLocation;
        TimeSpan? firstWait;
        using (var submitResponse = submitted.Value)
        {
            if (submitResponse.StatusCode != HttpStatusCode.Accepted)
            {
                var body = await submitResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return Result<AiOcrResult>.Failure(
                    $"Foundry Document Intelligence analyze submission for model '{model.ModelId}' failed " +
                    $"with {(int)submitResponse.StatusCode} {submitResponse.StatusCode}: {AzureErrorEnvelope.Describe(body)}");
            }

            var location = submitResponse.Headers.TryGetValues("Operation-Location", out var values)
                ? values.FirstOrDefault()
                : null;

            if (string.IsNullOrWhiteSpace(location))
            {
                return Result<AiOcrResult>.Failure(
                    "Foundry Document Intelligence analyze submission did not return an " +
                    "Operation-Location header.");
            }

            operationLocation = location;
            firstWait = FoundryRetryPolicy.ReadRetryAfter(submitResponse);
        }

        var pollResult = await PollUntilTerminalAsync(operationLocation, firstWait, cancellationToken).ConfigureAwait(false);
        if (pollResult.IsFailure)
        {
            return Result<AiOcrResult>.Failure(pollResult.Error);
        }

        var status = pollResult.Value;
        if (string.Equals(status.Status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return Result<AiOcrResult>.Failure(
                $"Foundry Document Intelligence analysis failed: {status.Error?.Code} {status.Error?.Message}".TrimEnd());
        }

        var pages = MapPages(status.AnalyzeResult);
        if (pages.IsFailure)
        {
            return Result<AiOcrResult>.Failure(pages.Error);
        }

        if (pages.Value.Count > ocrOptions.MaxPagesPerDocument)
        {
            return Result<AiOcrResult>.Failure(
                $"OCR page budget exceeded: document '{request.FileName}' has {pages.Value.Count} pages, " +
                $"configured maximum is {ocrOptions.MaxPagesPerDocument} (ADR-017: fail visibly, " +
                "never silently truncate).");
        }

        var metadata = FoundryCallMetadataFactory.Build(model, PromptVersion, clock, request.Content.Span);
        return Result<AiOcrResult>.Success(new AiOcrResult(pages.Value, metadata));
    }

    /// <summary>
    /// One <see cref="AiOcrPage"/> per reported page, in page order, each page's text being the
    /// concatenation of its spans sliced from <c>content</c>. A page with no spans (a blank scan)
    /// yields an empty page rather than being dropped, so page numbers stay aligned with the
    /// document; offsets are clamped into <c>content</c> so a malformed span can never throw.
    /// </summary>
    public static Result<IReadOnlyList<AiOcrPage>> MapPages(DocumentIntelligenceAnalyzeResult? analyzeResult)
    {
        var content = analyzeResult?.Content ?? string.Empty;
        var reported = analyzeResult?.Pages ?? [];

        if (reported.Count == 0)
        {
            return Result<IReadOnlyList<AiOcrPage>>.Success([]);
        }

        var ordered = reported.OrderBy(p => p.PageNumber).ToList();
        if (ordered.Select(p => p.PageNumber).Distinct().Count() != ordered.Count)
        {
            return Result<IReadOnlyList<AiOcrPage>>.Failure(
                "Foundry Document Intelligence returned duplicate page numbers; refusing to guess a page map.");
        }

        var pages = new List<AiOcrPage>(ordered.Count);
        foreach (var page in ordered)
        {
            var builder = new System.Text.StringBuilder();
            foreach (var span in (page.Spans ?? []).OrderBy(s => s.Offset))
            {
                var start = Math.Clamp(span.Offset, 0, content.Length);
                var end = Math.Clamp(span.Offset + Math.Max(0, span.Length), start, content.Length);
                if (end > start)
                {
                    if (builder.Length > 0)
                    {
                        builder.Append('\n');
                    }

                    builder.Append(content, start, end - start);
                }
            }

            pages.Add(new AiOcrPage(page.PageNumber, builder.ToString()));
        }

        return Result<IReadOnlyList<AiOcrPage>>.Success(pages);
    }

    private async Task<Result<DocumentIntelligenceOperationStatus>> PollUntilTerminalAsync(
        string operationLocation, TimeSpan? firstWait, CancellationToken cancellationToken)
    {
        var budget = TimeSpan.FromSeconds(Math.Max(1, ocrOptions.PollTimeoutSeconds));
        var initial = TimeSpan.FromMilliseconds(Math.Max(1, ocrOptions.InitialPollIntervalMilliseconds));
        var max = TimeSpan.FromMilliseconds(Math.Max(ocrOptions.InitialPollIntervalMilliseconds, ocrOptions.MaxPollIntervalMilliseconds));
        var interval = initial;
        var stopwatch = Stopwatch.StartNew();
        var nextWait = firstWait ?? interval;
        var attempts = 0;

        while (true)
        {
            await _delay(nextWait, cancellationToken).ConfigureAwait(false);
            attempts++;

            var polled = await _retryPolicy.SendAsync(
                    httpClient,
                    async token =>
                    {
                        var pollRequest = new HttpRequestMessage(HttpMethod.Get, operationLocation);
                        await AttachAuthAsync(pollRequest, token).ConfigureAwait(false);
                        return pollRequest;
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (polled.IsFailure)
            {
                return Result<DocumentIntelligenceOperationStatus>.Failure(polled.Error);
            }

            TimeSpan? retryAfter;
            string pollBody;
            using (var pollResponse = polled.Value)
            {
                pollBody = await pollResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!pollResponse.IsSuccessStatusCode)
                {
                    return Result<DocumentIntelligenceOperationStatus>.Failure(
                        $"Foundry Document Intelligence polling failed with {(int)pollResponse.StatusCode} " +
                        $"{pollResponse.StatusCode}: {AzureErrorEnvelope.Describe(pollBody)}");
                }

                retryAfter = FoundryRetryPolicy.ReadRetryAfter(pollResponse);
            }

            DocumentIntelligenceOperationStatus? status;
            try
            {
                status = JsonSerializer.Deserialize<DocumentIntelligenceOperationStatus>(pollBody, FoundryJsonOptions.Web);
            }
            catch (JsonException ex)
            {
                return Result<DocumentIntelligenceOperationStatus>.Failure(
                    $"Foundry Document Intelligence polling response was not valid JSON: {ex.Message}");
            }

            if (status is null)
            {
                return Result<DocumentIntelligenceOperationStatus>.Failure(
                    "Foundry Document Intelligence polling response parsed to null.");
            }

            if (string.Equals(status.Status, "succeeded", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status.Status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                return Result<DocumentIntelligenceOperationStatus>.Success(status);
            }

            if (stopwatch.Elapsed >= budget)
            {
                return Result<DocumentIntelligenceOperationStatus>.Failure(
                    $"Foundry Document Intelligence analysis did not complete within {budget.TotalSeconds:0}s " +
                    $"({attempts} polls; last status '{status.Status}'). Raise AiGateway:Ocr:PollTimeoutSeconds " +
                    "or lower AiGateway:Ocr:MaxPagesPerDocument.");
            }

            interval = interval * 2 > max ? max : interval * 2;
            nextWait = retryAfter ?? interval;
        }
    }

    private async Task AttachAuthAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Informational per-project attribution (AiGatewayFoundryOptions' own doc comment) — not a
        // documented REST contract, ignored by the service, harmless.
        if (!string.IsNullOrWhiteSpace(foundryOptions.ProjectName))
        {
            request.Headers.TryAddWithoutValidation("x-ms-foundry-project", foundryOptions.ProjectName);
        }

        if (!string.IsNullOrWhiteSpace(foundryOptions.DocumentIntelligenceConnection))
        {
            request.Headers.TryAddWithoutValidation(
                "x-ms-document-intelligence-connection", foundryOptions.DocumentIntelligenceConnection);
        }
    }
}
