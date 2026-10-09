using System.Diagnostics;
using System.Text.Json;
using Raffa.AiGateway.Agents;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Telemetry;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Tests.Agents;

public sealed record EchoInput(string Question, IReadOnlyList<string> Keys);

public sealed record EchoOutput(string Title, IReadOnlyList<string> CitationKeys);

/// <summary>The shared doubles of the runner tests: a gateway whose analyst/answer role is a
/// delegate, and the helpers that build a definition and a payload.</summary>
internal static class AgentTestKit
{
    public static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    public static AiCallMetadata Meta(string promptVersion) =>
        new("test-model", "2026-10", promptVersion, Now, "inputhash", new AiTokenUsage(10, 5));

    public static string Payload(string title = "ok", params string[] keys) =>
        JsonSerializer.Serialize(new EchoOutput(title, keys), AgentJson.Options);

    public static AgentDefinition Definition(
        string name = "echo-agent",
        AgentFailurePolicy policy = AgentFailurePolicy.Fail,
        TimeSpan? deadline = null,
        bool retry = true,
        string version = "echo-v1") =>
        AgentDefinition.ForOutput<EchoOutput>(
            name, version, "You are a test agent. Respond with strict JSON only.", deadline ?? TimeSpan.FromSeconds(5),
            policy, retryOnVerifierViolation: retry);

    public static AgentStep<EchoInput, EchoOutput> Step(
        AgentDefinition? definition = null,
        IStepVerifier<EchoInput, EchoOutput>? verifier = null,
        Func<EchoInput, EchoOutput?>? fallback = null) =>
        new(definition ?? Definition(), verifier, fallback);

    public static readonly EchoInput Input = new("question?", ["fact:1", "fact:2"]);

    /// <summary>Captures a stopped-span list for the <c>Raffa.Agents</c> source.</summary>
    public static ActivityListener Listen(List<Activity> sink)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AgentTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (sink)
                {
                    sink.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}

/// <summary>An <see cref="IAiGateway"/> whose analyst and answer roles are delegates; every other role
/// is out of scope for the runner and throws.</summary>
internal sealed class ScriptedGateway(
    Func<AiAnalysisRequest, CancellationToken, Task<Result<AiAnalysisResult>>>? analyze = null,
    Func<AiAnswerRequest, CancellationToken, Task<Result<AiAnswerResult>>>? answer = null) : IAiGateway
{
    private readonly object _lock = new();
    private int _inFlight;

    public List<AiAnalysisRequest> AnalyzeRequests { get; } = [];

    public List<AiAnswerRequest> AnswerRequests { get; } = [];

    public int MaxInFlight { get; private set; }

    public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default) =>
        Track(AnalyzeRequests, request, () =>
            analyze is null
                ? Task.FromResult(Result<AiAnalysisResult>.Success(
                    new AiAnalysisResult(AgentTestKit.Payload(), AgentTestKit.Meta(request.PromptVersion))))
                : analyze(request, cancellationToken));

    public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) =>
        Track(AnswerRequests, request, () =>
            answer is null
                ? Task.FromResult(Result<AiAnswerResult>.Success(
                    new AiAnswerResult(true, "answer", [], AgentTestKit.Meta(request.PromptVersion ?? "none"))))
                : answer(request, cancellationToken));

    private async Task<Result<T>> Track<TRequest, T>(List<TRequest> log, TRequest request, Func<Task<Result<T>>> call)
    {
        lock (_lock)
        {
            log.Add(request);
            _inFlight++;
            MaxInFlight = Math.Max(MaxInFlight, _inFlight);
        }

        try
        {
            return await call().ConfigureAwait(false);
        }
        finally
        {
            lock (_lock)
            {
                _inFlight--;
            }
        }
    }

    public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>Rejects the first <paramref name="rejections"/> outputs it sees with a named violation.</summary>
internal sealed class RejectingVerifier(int rejections, string violation = "key fact:9 is not in the input.")
    : IStepVerifier<EchoInput, EchoOutput>
{
    private int _seen;

    public string Name => "reject";

    public int Calls => _seen;

    public StepVerdict Verify(EchoInput input, EchoOutput output) =>
        Interlocked.Increment(ref _seen) <= rejections ? StepVerdict.Reject(violation) : StepVerdict.Pass;
}
