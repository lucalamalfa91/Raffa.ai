using Raffa.AiGateway.Agents;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.AiGateway.Logging;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiGateway.Tests.Agents;

/// <summary>
/// Plan A-01 acceptance: "fan-out 200 ripetizioni senza eccezioni DbContext". The audit writer behind
/// <see cref="LoggingAiGateway"/> wraps a Scoped EF <c>DbContext</c>, which throws "A second operation
/// was started on this context instance before a previous operation completed" when two writes
/// overlap. <see cref="NonThreadSafeAuditWriter"/> reproduces exactly that contract (it throws, with
/// yields inside the write to widen the window) so a runner that let two audit writes overlap would
/// fail here the way it would against Postgres; Docker is not needed.
/// </summary>
public sealed class StepRunnerFanOutStressTests
{
    private const int Repetitions = 200;

    private sealed class NonThreadSafeAuditWriter : IAuditWriter
    {
        private int _active;

        public int Written;

        public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _active) != 1)
            {
                Interlocked.Decrement(ref _active);
                throw new InvalidOperationException(
                    "A second operation was started on this context instance before a previous operation completed.");
            }

            try
            {
                await Task.Yield();
                await Task.Delay(1, cancellationToken);
                Interlocked.Increment(ref Written);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private sealed record AnalystOutput(IReadOnlyList<Finding> Findings);

    private sealed record Finding(string Title, string Insight, string? LeverType, IReadOnlyList<string> CitationKeys);

    private sealed record AnalystInput(string Question, IReadOnlyList<FixtureItem> Items);

    private sealed record FixtureItem(string CitationKey, string Corpus, string Title, string Snippet);

    private static AgentStep<AnalystInput, AnalystOutput> Analyst(string name, AgentFailurePolicy policy = AgentFailurePolicy.Skip) =>
        new(AgentDefinition.ForOutput<AnalystOutput>(name, "council-v3", "You are an analyst.", TimeSpan.FromSeconds(20), policy));

    [Fact]
    public async Task The_audit_writer_double_does_reject_overlapping_writes()
    {
        // Control: without the gateway's serialization the double fails, so a green stress run means
        // the serialization held, not that the double is lenient.
        var writer = new NonThreadSafeAuditWriter();
        var tenantId = TenantId.New();
        var entry = new AuditEntry(tenantId, "t", "a", "r", "1", DateTimeOffset.UtcNow);

        var writes = Enumerable.Range(0, 8).Select(_ => writer.WriteAsync(entry));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.WhenAll(writes));
    }

    [Fact]
    public async Task Two_hundred_fan_outs_through_the_logging_gateway_raise_no_overlapping_context_use()
    {
        var writer = new NonThreadSafeAuditWriter();
        var tenant = new TenantContext();
        var fixture = new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions());
        var succeeded = 0;

        for (var i = 0; i < Repetitions; i++)
        {
            // One scope per repetition, as in production: a fresh logging gateway (and its lock) and a
            // fresh runner, sharing one tenant scope and one non-thread-safe "context".
            var gateway = new LoggingAiGateway(fixture, writer, tenant, new AiGatewayComplianceOptions());
            var runner = new StepRunner(gateway);

            using (tenant.BeginScope(TenantId.New()))
            {
                var input = new AnalystInput(
                    $"question {i}",
                    [new FixtureItem("fact:1", "tenant", "Supplier · Order", "Ends on 2028-04-10."),
                     new FixtureItem("calc:lever", "calc", "Lever", "Up to EUR 20700.")]);

                var results = await Task.WhenAll(
                    runner.RunAsync(Analyst("contract-analyst"), input),
                    runner.RunAsync(Analyst("market-analyst"), input),
                    runner.RunAsync(Analyst("pricing-analyst"), input),
                    runner.RunAsync(Analyst("terms-analyst"), input),
                    runner.RunAsync(Analyst("risk-analyst"), input));

                Assert.All(results, r => Assert.True(r.IsSuccess, r.Describe()));
                Assert.All(results, r => Assert.NotEmpty(r.Value!.Findings));
                succeeded += results.Length;
            }
        }

        Assert.Equal(Repetitions * 5, succeeded);
        // One audit row per call, none lost, none overlapping.
        Assert.Equal(Repetitions * 5, writer.Written);
    }

    [Fact]
    public async Task Two_hundred_homogeneous_fan_outs_with_a_cap_below_the_width_also_hold()
    {
        var writer = new NonThreadSafeAuditWriter();
        var tenant = new TenantContext();
        var fixture = new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions());
        var step = Analyst("contract-analyst", AgentFailurePolicy.Fail);

        for (var i = 0; i < Repetitions; i++)
        {
            var runner = new StepRunner(
                new LoggingAiGateway(fixture, writer, tenant, new AiGatewayComplianceOptions()),
                new AgentRunnerOptions { MaxParallelism = 3 });

            using (tenant.BeginScope(TenantId.New()))
            {
                var inputs = Enumerable.Range(0, 7)
                    .Select(n => new AnalystInput($"q{i}-{n}", [new FixtureItem($"fact:{n}", "tenant", "T", "S")]))
                    .ToList();

                var results = await runner.FanOutAsync(step, inputs);

                Assert.Equal(7, results.Count);
                Assert.All(results, r => Assert.True(r.IsSuccess, r.Describe()));
            }
        }

        Assert.Equal(Repetitions * 7, writer.Written);
    }
}
