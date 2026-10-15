using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Raffa.AiFlows.CapabilityGaps.Agents;
using Raffa.AiFlows.CapabilityGaps.Investigation;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Jev;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiFlows.Tests.CapabilityGaps.Investigation;

/// <summary>F4-T02: the capability check's bulkhead — at most <c>MaxConcurrentChecks</c> run at
/// once; the rest are shed (audited as <c>busy</c>) rather than queued, and the slot is released.</summary>
public sealed class CapabilityCheckRunnerBulkheadTests
{
    private const string QuestionPayload =
        """{"rationale":"t","verdict":"question","confidence":"high","knownGapKey":"","nearestCapabilityKey":"","feature":{"key":"","titleEn":"","titleIt":"","operationEn":"","operationIt":"","descriptionEn":"","descriptionIt":""},"alternativeQuestions":[]}""";

    private static (CapabilityCheckRunner Runner, BlockingGateway Gateway, List<AuditEntry> Audit) Build(int limit)
    {
        var gateway = new BlockingGateway();
        var audit = new List<AuditEntry>();
        var options = new GapInvestigationOptions { MaxConcurrentChecks = limit };
        var monitor = StaticGapInvestigationOptions.Monitor(options);

        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext, TenantContext>();
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<IAuditWriter>(new CollectingAudit(audit));
        services.AddScoped(_ => new CapabilityInvestigator(
            gateway,
            monitor,
            new AiGatewayJevOptions(),
            new JevVerdictClient(
                new JevHttpJsonClient(new HttpClient(), new AiGatewayJevOptions()),
                new AiGatewayJevOptions(),
                SystemClock.Instance)));
        var provider = services.BuildServiceProvider();

        return (new CapabilityCheckRunner(provider.GetRequiredService<IServiceScopeFactory>(), monitor, NullLogger<CapabilityCheckRunner>.Instance), gateway, audit);
    }

    private static CapabilityCheckRequest Request() => new(TenantId.New(), "quanto spendo?", [], 1, null, null, "turn-1");

    [Fact]
    public async Task Checks_beyond_the_bound_are_shed_and_audited_busy()
    {
        var (runner, gateway, audit) = Build(limit: 1);

        var first = runner.Start(Request());
        await gateway.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var shed = await runner.Start(Request());
        Assert.Null(shed);
        Assert.Equal(1, gateway.Calls);
        Assert.Contains(audit, a => a.Detail!.Contains("outcome=busy", StringComparison.Ordinal));

        gateway.Release.SetResult();
        Assert.Null(await first);
        Assert.Contains(audit, a => a.Detail!.Contains("outcome=question", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_slot_is_released_when_a_check_finishes()
    {
        var (runner, gateway, _) = Build(limit: 1);
        gateway.Release.SetResult();

        await runner.Start(Request());
        await runner.Start(Request());

        Assert.Equal(2, gateway.Calls);
    }

    [Fact]
    public async Task A_zero_bound_disables_the_bulkhead()
    {
        var (runner, gateway, _) = Build(limit: 0);
        gateway.Release.SetResult();

        await Task.WhenAll(runner.Start(Request()), runner.Start(Request()), runner.Start(Request()));

        Assert.Equal(3, gateway.Calls);
    }

    private sealed class CollectingAudit(List<AuditEntry> sink) : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            lock (sink)
            {
                sink.Add(entry);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class BlockingGateway : IAiGateway
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return Result<AiAnalysisResult>.Success(new AiAnalysisResult(
                QuestionPayload, new AiCallMetadata("scripted", "1", request.PromptVersion, DateTimeOffset.UtcNow, "hash")));
        }
    }
}
