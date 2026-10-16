using System.Net;
using System.Text;
using System.Text.Json;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.JevBenchmark.Tests.Harness;
using Raffa.SharedKernel;

namespace Raffa.JevBenchmark.Tests.Tests;

internal sealed class FakeBenchClock : IBenchClock
{
    public double NowMs { get; private set; }

    public void Advance(double ms) => NowMs += ms;
}

/// <summary>Scripted Foundry: classify and analyze return what the test says and advance the fake clock by a fixed latency. No network.</summary>
internal sealed class FakeFoundryGateway(
    FakeBenchClock clock,
    double latencyMs = 100,
    Func<string, (AiDocumentType Type, double Confidence)>? classify = null,
    string? analyzePayload = null,
    string? failWith = null) : IAiGateway
{
    public int ClassifyCalls { get; private set; }

    public int AnalyzeCalls { get; private set; }

    private static AiCallMetadata Metadata(string prompt) =>
        new("gpt-test", "v1", prompt, DateTimeOffset.UnixEpoch, "hash", new AiTokenUsage(100, 10));

    public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default)
    {
        ClassifyCalls++;
        clock.Advance(latencyMs);
        if (failWith is not null)
        {
            return Task.FromResult(Result<AiClassificationResult>.Failure(failWith));
        }

        var (type, confidence) = (classify ?? (_ => (AiDocumentType.Other, 0.5)))(request.DocumentText);
        return Task.FromResult(Result<AiClassificationResult>.Success(new AiClassificationResult(type, confidence, Metadata("foundry-classify-v3"))));
    }

    public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        AnalyzeCalls++;
        clock.Advance(latencyMs);
        return Task.FromResult(failWith is not null
            ? Result<AiAnalysisResult>.Failure(failWith)
            : Result<AiAnalysisResult>.Success(new AiAnalysisResult(analyzePayload ?? "{}", Metadata("gaps-v1"))));
    }

    public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

/// <summary>Scripted Jev transport (never a real socket): answers every request with the same status and body.</summary>
internal sealed class FakeJevTransport(FakeBenchClock clock, double latencyMs, HttpStatusCode status, string body) : HttpMessageHandler
{
    public int Calls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        clock.Advance(latencyMs);
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private static string Choice(string choice, double confidence) =>
        JsonSerializer.Serialize(new { type = "choice", choice, probabilities = new Dictionary<string, double> { [choice] = confidence }, confidence });

    /// <summary>A System One answer for the classify role (both option orders agree on <paramref name="choice"/>).</summary>
    public static string ClassifyBody(string choice, double confidence, double cost = 0.0002) =>
        $"{{\"model\":\"typesafe/jev-1.13-20260917\",\"answers\":{{\"documentType\":{Choice(choice, confidence)},\"documentTypeReversed\":{Choice(choice, confidence)}}},\"usage\":{{\"input_tokens\":500,\"output_tokens\":5,\"cost\":{cost.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}}}";

    /// <summary>A System One answer for the investigator's operation question.</summary>
    public static string OperationBody(string operation, double confidence = 0.95, double cost = 0.0003) =>
        $"{{\"model\":\"typesafe/jev-1.13-20260917\",\"answers\":{{\"operation\":{Choice(operation, confidence)},\"operationReversed\":{Choice(operation, confidence)}}},\"usage\":{{\"input_tokens\":900,\"output_tokens\":6,\"cost\":{cost.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}}}";
}

internal static class Samples
{
    /// <summary>Foundry `analyst` payload of a valid "gap" verdict (same shape the investigator's own tests use).</summary>
    public static string GapPayload() => JsonSerializer.Serialize(new
    {
        rationale = "test",
        verdict = "gap",
        confidence = "high",
        knownGapKey = "",
        nearestCapabilityKey = "portfolio",
        feature = new
        {
            key = "cfo-spend-report",
            titleEn = "Spend reports for management",
            titleIt = "Report di spesa per il management",
            operationEn = "generate a spend report for management",
            operationIt = "generare un report di spesa per il management",
            descriptionEn = "Generate a periodic spend report for management.",
            descriptionIt = "Generare un report periodico sulla spesa per il management.",
        },
        alternativeQuestions = Array.Empty<string>(),
    });

    public static string QuestionPayload() => JsonSerializer.Serialize(new
    {
        rationale = "test",
        verdict = "question",
        confidence = "high",
        knownGapKey = "",
        nearestCapabilityKey = "",
        feature = new { key = "", titleEn = "", titleIt = "", operationEn = "", operationIt = "", descriptionEn = "", descriptionIt = "" },
        alternativeQuestions = Array.Empty<string>(),
    });

    public static LabelledCase ClassifyCase(string id = "c1", string expected = "Invoice") =>
        new(BenchTracks.Classify, id, "Some document text", expected, "en", "synthetic");

    public static LabelledCase GapCase(string id = "g1", string expected = "gap") =>
        new(BenchTracks.Gaps, id, "puoi scrivere un report per riportare l'andamento del 2026 al CFO?", expected, "it", "gap-request");

    public static CaseObservation Obs(
        string arm, string caseId, int rep, string expected, string? predicted, double latencyMs = 100,
        bool error = false, bool timedOut = false, string? fallback = null, string? servedBy = null,
        IReadOnlyList<CallRecord>? calls = null) =>
        new(BenchTracks.Classify, arm, caseId, rep, expected, error ? null : predicted, null, latencyMs, error, timedOut, fallback, servedBy, null, null, calls ?? []);
}
