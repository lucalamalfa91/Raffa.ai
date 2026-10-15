using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Raffa.AiFlows.CapabilityGaps.Investigation;
using Raffa.Chat.Application.Reply;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Api.Tests;

/// <summary>F4-T02: on shutdown the host waits (within its budget) for background follow-ups.</summary>
public sealed class CapabilityFollowUpAppenderShutdownTests
{
    private static CapabilityFollowUpAppender Appender()
    {
        var scopes = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var runner = new CapabilityCheckRunner(
            scopes, StaticGapInvestigationOptions.Monitor(new GapInvestigationOptions()), NullLogger<CapabilityCheckRunner>.Instance);
        return new CapabilityFollowUpAppender(scopes, runner, NullLogger<CapabilityFollowUpAppender>.Instance);
    }

    [Fact]
    public async Task StopAsync_waits_for_a_pending_background_append()
    {
        var appender = Appender();
        var check = new TaskCompletionSource<CopilotReply?>(TaskCreationOptions.RunContinuationsAsynchronously);
        appender.AppendWhenDone(check.Task, TenantId.New(), "u", EntityId.New(), EntityId.New());

        var stop = ((IHostedService)appender).StopAsync(CancellationToken.None);
        await Task.Delay(100);
        Assert.False(stop.IsCompleted);

        check.SetResult(null);
        await stop.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task StopAsync_gives_up_when_the_shutdown_budget_is_cancelled()
    {
        var appender = Appender();
        var check = new TaskCompletionSource<CopilotReply?>();
        appender.AppendWhenDone(check.Task, TenantId.New(), "u", EntityId.New(), EntityId.New());

        using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await ((IHostedService)appender).StopAsync(budget.Token).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task StopAsync_returns_at_once_when_nothing_is_pending()
        => await ((IHostedService)Appender()).StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
}
