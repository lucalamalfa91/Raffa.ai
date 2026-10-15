using System.Text.Json;
using Raffa.AiFlows.CapabilityGaps.Investigation;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Jev;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.CapabilityGaps.Investigation;

/// <summary>INV-06: earlier turns reach the investigator only when <c>ContextTurns</c> is on.</summary>
public sealed class CapabilityInvestigatorContextTests
{
    private const string QuestionPayload =
        """{"rationale":"t","verdict":"question","confidence":"high","knownGapKey":"","nearestCapabilityKey":"","feature":{"key":"","titleEn":"","titleIt":"","operationEn":"","operationIt":"","descriptionEn":"","descriptionIt":""},"alternativeQuestions":[]}""";

    private static readonly string[] History = ["first", "  second  ", "", "third"];

    private static async Task<string> InputSentAsync(int contextTurns, IReadOnlyList<string>? turns)
    {
        var gateway = new CapturingGateway();
        var jev = new AiGatewayJevOptions();
        var investigator = new CapabilityInvestigator(
            gateway,
            new GapInvestigationOptions { ContextTurns = contextTurns },
            jev,
            new JevVerdictClient(new JevHttpJsonClient(new HttpClient(), jev), jev, SystemClock.Instance));

        await investigator.InvestigateAsync("e per l'altro fornitore?", [], CancellationToken.None, turns);
        return gateway.Input;
    }

    [Fact]
    public void Context_is_off_by_default()
    {
        Assert.Equal(0, new GapInvestigationOptions().ContextTurns);
    }

    [Fact]
    public async Task Off_the_input_is_byte_identical_whether_or_not_turns_are_supplied()
    {
        var without = await InputSentAsync(0, null);
        var with = await InputSentAsync(0, History);

        Assert.Equal(without, with);
        Assert.DoesNotContain("recentUserQuestions", with, StringComparison.Ordinal);
    }

    [Fact]
    public async Task On_the_last_n_non_blank_turns_are_sent_oldest_first()
    {
        using var json = JsonDocument.Parse(await InputSentAsync(2, History));

        var turns = json.RootElement.GetProperty("recentUserQuestions").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(["second", "third"], turns);
    }

    [Fact]
    public async Task On_without_turns_the_input_is_the_plain_one()
    {
        Assert.Equal(await InputSentAsync(0, null), await InputSentAsync(3, null));
        Assert.Equal(await InputSentAsync(0, null), await InputSentAsync(3, ["  "]));
    }

    [Fact]
    public void Long_turns_are_cut()
    {
        var selected = CapabilityInvestigator.SelectContext([new string('x', 1000)], 1)!;

        Assert.Equal(CapabilityInvestigator.MaxContextTurnLength, selected[0].Length);
    }

    private sealed class CapturingGateway : IAiGateway
    {
        public string Input { get; private set; } = string.Empty;

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            Input = request.InputJson;
            return Task.FromResult(Result<AiAnalysisResult>.Success(new AiAnalysisResult(
                QuestionPayload, new AiCallMetadata("scripted", "1", request.PromptVersion, DateTimeOffset.UtcNow, "hash"))));
        }
    }
}
