using System.Diagnostics;
using System.Text.Json;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Telemetry;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Logging;

/// <summary>
/// <see cref="IAiGateway"/> decorator for the Super Admin playground (docs/architecture/admin-agent-playground-plan.md).
/// While a <see cref="PlaygroundTrace"/> is active it records, for each chat-role call, the exact request
/// the model received, the raw answer, the deployment, token usage and latency. With no trace active (every
/// normal request) it forwards straight to the inner gateway: one null check, no serialisation. Embedding
/// and OCR calls are forwarded unrecorded (vectors and page text are not useful in an inspector).
/// </summary>
public sealed class TraceRecordingAiGateway(IAiGateway inner) : IAiGateway
{
    /// <summary>The decorated gateway (the audit-logging one).</summary>
    public IAiGateway Inner => inner;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) =>
        RecordAsync("classify", "classify", request, () => inner.ClassifyAsync(request, cancellationToken), r => r.Metadata);

    public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) =>
        RecordAsync("extract", request.StageName, request, () => inner.ExtractAsync(request, cancellationToken), r => r.Metadata);

    public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
        inner.EmbedAsync(request, cancellationToken);

    public Task<Result<AiEmbeddingBatchResult>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
        inner.EmbedBatchAsync(texts, cancellationToken);

    public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) =>
        inner.OcrAsync(request, cancellationToken);

    public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) =>
        RecordAsync("answer", "answer", request, () => inner.AnswerAsync(request, cancellationToken), r => r.Metadata);

    public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default) =>
        RecordAsync("analyze", request.AgentName, request, () => inner.AnalyzeAsync(request, cancellationToken), r => r.Metadata);

    public Task<Result<AiResearchResult>> ResearchAsync(AiResearchRequest request, CancellationToken cancellationToken = default) =>
        RecordAsync("research", "web-researcher", request, () => inner.ResearchAsync(request, cancellationToken), r => r.Metadata);

    public Task<Result<AiConversationResult>> ConverseAsync(AiConversationRequest request, CancellationToken cancellationToken = default) =>
        RecordAsync("converse", request.AgentName, request, () => inner.ConverseAsync(request, cancellationToken), r => r.Metadata);

    private static async Task<Result<T>> RecordAsync<T>(
        string role, string name, object request, Func<Task<Result<T>>> call, Func<T, AiCallMetadata> metadata)
    {
        var trace = PlaygroundTrace.Current;
        if (trace is null)
        {
            return await call().ConfigureAwait(false);
        }

        var stopwatch = Stopwatch.StartNew();
        var result = await call().ConfigureAwait(false);
        stopwatch.Stop();

        if (result.IsFailure)
        {
            trace.Add("model_call", $"{role}:{name}", stopwatch.ElapsedMilliseconds, ok: false,
                input: Serialize(request), note: result.Error);
        }
        else
        {
            var meta = metadata(result.Value);
            trace.Add("model_call", $"{role}:{name}", stopwatch.ElapsedMilliseconds, ok: true,
                model: meta.ModelId, promptTokens: meta.Usage?.PromptTokens, completionTokens: meta.Usage?.CompletionTokens,
                cachedTokens: meta.Usage?.CachedTokens, input: Serialize(request), output: Serialize(result.Value),
                note: $"promptVersion={meta.PromptVersion} modelVersion={meta.ModelVersion}");
        }

        return result;
    }

    private static string Serialize(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        try
        {
            return JsonSerializer.Serialize(value, value.GetType(), Json);
        }
        catch (NotSupportedException)
        {
            return value.ToString() ?? string.Empty;
        }
    }
}
