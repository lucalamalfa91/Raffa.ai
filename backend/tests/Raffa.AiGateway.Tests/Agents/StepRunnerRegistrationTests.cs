using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Raffa.AiGateway.Agents;
using Raffa.AiGateway.Logging;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiGateway.Tests.Agents;

/// <summary>Plan A-01: the runner is Scoped with the gateway it wraps, so the tenant scope, the audit
/// decorator and its write lock are the request's own; definitions are plain immutable objects.</summary>
public sealed class StepRunnerRegistrationTests
{
    private sealed class NoOpAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<IAuditWriter, NoOpAuditWriter>();
        services.AddAiGatewayModule();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Fact]
    public void The_runner_resolves_scoped_over_the_logging_gateway()
    {
        using var provider = Build();
        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var a1 = scopeA.ServiceProvider.GetRequiredService<StepRunner>();
        var a2 = scopeA.ServiceProvider.GetRequiredService<StepRunner>();
        var b = scopeB.ServiceProvider.GetRequiredService<StepRunner>();

        Assert.Same(a1, a2);
        Assert.NotSame(a1, b);
        Assert.IsType<LoggingAiGateway>(scopeA.ServiceProvider.GetRequiredService<IAiGateway>());
        // The options are one immutable singleton.
        Assert.Same(
            provider.GetRequiredService<AgentRunnerOptions>(),
            scopeA.ServiceProvider.GetRequiredService<AgentRunnerOptions>());
    }

    [Fact]
    public async Task A_resolved_runner_runs_a_step_over_the_fixture_gateway_inside_a_tenant_scope()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<StepRunner>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var step = AgentTestKit.Step(AgentTestKit.Definition("contract-analyst", AgentFailurePolicy.Skip));

        using (tenant.BeginScope(TenantId.New()))
        {
            // The fixture double answers an "...-analyst" with {"findings":[...]}: a lenient payload
            // for this step's type (as the council's own deserialization is), but a real call through
            // the logging gateway, which stamps the metadata with the step's version.
            var result = await runner.RunAsync(step, new EchoInput("q", []));
            Assert.Equal(StepStatus.Succeeded, result.Status);
            Assert.Equal("echo-v1", result.Metadata!.PromptVersion);
        }
    }
}
