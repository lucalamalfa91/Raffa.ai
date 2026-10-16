using System.Text.Json;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Jev;
using Raffa.SharedKernel;

namespace Raffa.JevBenchmark.Tests.Harness;

/// <summary>
/// Decorator around the real Foundry gateway that records, per call, the latency, success and token
/// usage of the two roles the benchmark exercises (classify, analyze). Everything else passes through
/// untouched. Both arms use it, so the Foundry share of an arm's cost and latency is measured the same
/// way (in arm B it only sees the calls Jev did not serve: the fallbacks and the gap descriptions).
/// </summary>
public sealed class MeteringAiGateway(IAiGateway inner, CallLedger ledger, IBenchClock clock) : IAiGateway
{
    public async Task<Result<AiClassificationResult>> ClassifyAsync(
        AiClassificationRequest request, CancellationToken cancellationToken = default)
    {
        var start = clock.NowMs;
        var result = await inner.ClassifyAsync(request, cancellationToken).ConfigureAwait(false);
        Record("classify", start, result.IsSuccess, result.IsSuccess ? result.Value.Metadata : null, result.IsFailure ? result.Error : null);
        return result;
    }

    public async Task<Result<AiAnalysisResult>> AnalyzeAsync(
        AiAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var start = clock.NowMs;
        var result = await inner.AnalyzeAsync(request, cancellationToken).ConfigureAwait(false);
        Record("analyze", start, result.IsSuccess, result.IsSuccess ? result.Value.Metadata : null, result.IsFailure ? result.Error : null);
        return result;
    }

    public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) =>
        inner.ExtractAsync(request, cancellationToken);

    public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
        inner.EmbedAsync(request, cancellationToken);

    public Task<Result<AiEmbeddingBatchResult>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
        inner.EmbedBatchAsync(texts, cancellationToken);

    public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) =>
        inner.AnswerAsync(request, cancellationToken);

    public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) =>
        inner.OcrAsync(request, cancellationToken);

    public Task<Result<AiResearchResult>> ResearchAsync(AiResearchRequest request, CancellationToken cancellationToken = default) =>
        inner.ResearchAsync(request, cancellationToken);

    private void Record(string operation, double start, bool success, AiCallMetadata? metadata, string? error) =>
        ledger.Add(new CallRecord(
            "foundry",
            operation,
            clock.NowMs - start,
            success,
            success,
            null,
            metadata?.Usage?.PromptTokens ?? 0,
            metadata?.Usage?.CompletionTokens ?? 0,
            null,
            error));
}

/// <summary>
/// Sits under the Jev <see cref="HttpClient"/> and records every HTTP attempt (retries included):
/// status, latency, the token usage and OpenRouter <c>cost</c> the response reports, and whether the
/// body carried a usable choice. This is the only place the real Jev cost and the number of attempts
/// are visible, because the gateway contract exposes tokens but neither cost nor retries.
/// </summary>
public sealed class RecordingJevHandler(HttpMessageHandler transport, CallLedger ledger, IBenchClock clock)
    : DelegatingHandler(transport)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var start = clock.NowMs;
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            ledger.Add(new CallRecord("jev", "decision", clock.NowMs - start, false, false, null, 0, 0, null, ex.GetType().Name + ": " + ex.Message));
            throw;
        }

        var body = response.Content is null ? "" : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var success = (int)response.StatusCode is >= 200 and < 300;
        var usable = false;
        var inputTokens = 0;
        var outputTokens = 0;
        double? cost = null;

        if (success)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<JevSystemOneResponse>(body, Json);
                usable = parsed?.Answers is { Count: > 0 } answers && answers.Values.Any(a => !string.IsNullOrWhiteSpace(a.Choice));
                inputTokens = parsed?.Usage?.InputTokens ?? 0;
                outputTokens = parsed?.Usage?.OutputTokens ?? 0;
                cost = parsed?.Usage?.Cost;
            }
            catch (JsonException)
            {
                usable = false;
            }
        }

        ledger.Add(new CallRecord(
            "jev",
            "decision",
            clock.NowMs - start,
            success,
            usable,
            (int)response.StatusCode,
            inputTokens,
            outputTokens,
            cost,
            success ? null : $"HTTP {(int)response.StatusCode}"));

        // The body has been buffered by ReadAsStringAsync, so the downstream reader can read it again.
        return response;
    }
}

/// <summary>The transport of the baseline arm's (never used) Jev client: reaching it is a harness bug, so it throws loudly.</summary>
public sealed class ForbiddenJevTransport : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "The baseline arm (Jev disabled) attempted a Jev HTTP call. This is a benchmark wiring bug: arm A must never reach Jev.");
}
