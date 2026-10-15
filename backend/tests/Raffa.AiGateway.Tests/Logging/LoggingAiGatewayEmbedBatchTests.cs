using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.AiGateway.Logging;
using Raffa.AiGateway.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiGateway.Tests.Logging;

/// <summary>T-05 / R1-06: one audit row per embedding batch, not one per text.</summary>
public class LoggingAiGatewayEmbedBatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static (LoggingAiGateway Gateway, RecordingAuditWriter Audit, TenantContext Tenant) Create()
    {
        var inner = new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now));
        var audit = new RecordingAuditWriter();
        var tenant = new TenantContext();
        return (new LoggingAiGateway(inner, audit, tenant, new AiGatewayComplianceOptions()), audit, tenant);
    }

    [Fact]
    public async Task A_batch_of_three_writes_exactly_one_audit_entry_carrying_the_batch_size()
    {
        var (gateway, audit, tenant) = Create();

        using (tenant.BeginScope(TenantId.New()))
        {
            var result = await gateway.EmbedBatchAsync(["one", "two", "three"]);

            Assert.True(result.IsSuccess);
            Assert.Equal(3, result.Value.Vectors.Count);

            var entry = Assert.Single(audit.Written);
            Assert.Equal("ai.embedded", entry.Action);
            Assert.Contains("batchSize=3", entry.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain("two", entry.Detail, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_default_implementation_embeds_one_by_one_and_keeps_input_order()
    {
        var gateway = new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now));

        var batch = await ((IAiGateway)gateway).EmbedBatchAsync(["alpha", "beta"]);
        var alpha = await gateway.EmbedAsync(new AiEmbeddingRequest("alpha"));
        var beta = await gateway.EmbedAsync(new AiEmbeddingRequest("beta"));

        Assert.True(batch.IsSuccess);
        Assert.Equal(alpha.Value.Vector, batch.Value.Vectors[0]);
        Assert.Equal(beta.Value.Vector, batch.Value.Vectors[1]);
    }

    [Fact]
    public async Task An_empty_batch_fails_without_an_audit_row()
    {
        var (gateway, audit, tenant) = Create();

        using (tenant.BeginScope(TenantId.New()))
        {
            var result = await gateway.EmbedBatchAsync([]);

            Assert.True(result.IsFailure);
            Assert.Empty(audit.Written);
        }
    }
}
