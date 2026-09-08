using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Contracts;
using Contigo.AiGateway.Foundry.Wire;
using Contigo.SharedKernel;

namespace Contigo.AiGateway.Foundry;

/// <summary>
/// `ocr` role (ADR-017) against Azure AI Document Intelligence's
/// <c>documentModels/{model}:analyze</c> long-running operation: the initial POST returns 202 +
/// an <c>Operation-Location</c> polling URL (no useful response body of its own), so this client
/// polls that URL until the analysis reaches a terminal state, then reconstructs a page map from
/// <c>analyzeResult.content</c> — the whole document's text with a form-feed (<c>\f</c>) page
/// break between pages, the same convention <see cref="Fixtures.FixtureAiGateway"/>'s own fixture
/// OCR already uses for its deterministic multi-page splitting.
///
/// Does not use <see cref="FoundryHttpJsonClient"/> — that type's "parse the JSON body or fail"
/// contract has no room for the 202 + header + poll-loop shape this role needs, so this client
/// owns <see cref="HttpClient"/>/<see cref="FoundryTokenProvider"/> directly.
/// </summary>
public sealed class FoundryOcrClient(
    HttpClient httpClient,
    FoundryTokenProvider tokenProvider,
    AiGatewayFoundryOptions foundryOptions,
    AiGatewayModelOptions modelOptions,
    AiGatewayOcrOptions ocrOptions,
    IClock clock,
    TimeSpan? pollInterval = null)
{
    private const string ApiVersion = "2024-11-30";
    private const string PromptVersion = "foundry-ocr-v1";

    /// <summary>Bounded so a stuck/failed operation on the provider side fails this call visibly
    /// instead of polling forever (ADR-017: "fail visibly... never silently truncate" extends to
    /// never silently hanging either).</summary>
    private const int MaxPollAttempts = 30;

    /// <summary>Defaults to a real, small delay in production; tests inject
    /// <see cref="TimeSpan.Zero"/> so a multi-poll fake-handler test runs instantly (task
    /// E13/F01/US01/T02: "no live Azure in unit tests" extends to "no slow unit tests either").</summary>
    private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(500);

    public async Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken)
    {
        if (request.Content.IsEmpty)
        {
            return Result<AiOcrResult>.Failure("Ocr requires non-empty document content.");
        }

        var model = modelOptions.Ocr;
        var analyzeUrl =
            $"documentintelligence/documentModels/{Uri.EscapeDataString(model.ModelId)}:analyze" +
            $"?api-version={ApiVersion}";

        using var submitRequest = new HttpRequestMessage(HttpMethod.Post, analyzeUrl)
        {
            Content = JsonContent.Create(
                new DocumentIntelligenceAnalyzeRequest(Convert.ToBase64String(request.Content.Span)),
                options: FoundryJsonOptions.Web),
        };

        await AttachAuthAsync(submitRequest, cancellationToken).ConfigureAwait(false);

        using var submitResponse = await httpClient.SendAsync(submitRequest, cancellationToken).ConfigureAwait(false);

        if (submitResponse.StatusCode != HttpStatusCode.Accepted)
        {
            var body = await submitResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return Result<AiOcrResult>.Failure(
                $"Foundry Document Intelligence analyze submission for model '{model.ModelId}' failed " +
                $"with {(int)submitResponse.StatusCode} {submitResponse.StatusCode}: {body}");
        }

        var operationLocation = submitResponse.Headers.TryGetValues("Operation-Location", out var values)
            ? values.FirstOrDefault()
            : null;

        if (string.IsNullOrWhiteSpace(operationLocation))
        {
            return Result<AiOcrResult>.Failure(
                "Foundry Document Intelligence analyze submission did not return an " +
                "Operation-Location header.");
        }

        var pollResult = await PollUntilTerminalAsync(operationLocation, cancellationToken).ConfigureAwait(false);
        if (pollResult.IsFailure)
        {
            return Result<AiOcrResult>.Failure(pollResult.Error);
        }

        var status = pollResult.Value;

        if (string.Equals(status.Status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return Result<AiOcrResult>.Failure(
                $"Foundry Document Intelligence analysis failed: {status.Error?.Code} {status.Error?.Message}");
        }

        var analyzeResult = status.AnalyzeResult;
        var reportedPageCount = analyzeResult?.Pages?.Count ?? 0;
        var content = analyzeResult?.Content ?? string.Empty;
        var pages = SplitIntoPages(content, reportedPageCount);

        if (pages.Count > ocrOptions.MaxPagesPerDocument)
        {
            return Result<AiOcrResult>.Failure(
                $"OCR page budget exceeded: document '{request.FileName}' has {pages.Count} pages, " +
                $"configured maximum is {ocrOptions.MaxPagesPerDocument} (ADR-017: fail visibly, " +
                "never silently truncate).");
        }

        var metadata = FoundryCallMetadataFactory.Build(model, PromptVersion, clock, request.Content.Span);

        return Result<AiOcrResult>.Success(new AiOcrResult(pages, metadata));
    }

    private async Task<Result<DocumentIntelligenceOperationStatus>> PollUntilTerminalAsync(
        string operationLocation, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxPollAttempts; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
            }

            using var pollRequest = new HttpRequestMessage(HttpMethod.Get, operationLocation);
            await AttachAuthAsync(pollRequest, cancellationToken).ConfigureAwait(false);

            using var pollResponse = await httpClient.SendAsync(pollRequest, cancellationToken).ConfigureAwait(false);
            var pollBody = await pollResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!pollResponse.IsSuccessStatusCode)
            {
                return Result<DocumentIntelligenceOperationStatus>.Failure(
                    $"Foundry Document Intelligence polling failed with {(int)pollResponse.StatusCode} " +
                    $"{pollResponse.StatusCode}: {pollBody}");
            }

            DocumentIntelligenceOperationStatus? status;
            try
            {
                status = JsonSerializer.Deserialize<DocumentIntelligenceOperationStatus>(
                    pollBody, FoundryJsonOptions.Web);
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
        }

        return Result<DocumentIntelligenceOperationStatus>.Failure(
            $"Foundry Document Intelligence analysis did not complete after {MaxPollAttempts} polling attempts.");
    }

    /// <summary>Falls back to treating the whole string as a single page when the form-feed split
    /// does not agree with the reported page count — a provider response-shape drift should
    /// degrade to "one big page", never silently drop text.</summary>
    private static IReadOnlyList<AiOcrPage> SplitIntoPages(string content, int reportedPageCount)
    {
        var pageTexts = content.Split('\f');

        if (reportedPageCount > 0 && pageTexts.Length != reportedPageCount)
        {
            return [new AiOcrPage(1, content)];
        }

        var pages = new List<AiOcrPage>(pageTexts.Length);
        for (var i = 0; i < pageTexts.Length; i++)
        {
            pages.Add(new AiOcrPage(i + 1, pageTexts[i]));
        }

        return pages;
    }

    private async Task AttachAuthAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

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
