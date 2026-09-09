using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Foundry;

namespace Contigo.AiGateway.Tests.TestSupport;

/// <summary>
/// <see cref="FoundryRetryPolicy"/> instances whose waits complete immediately, so a test that
/// scripts a transient status never sleeps through the real backoff.
/// </summary>
public static class TestRetryPolicies
{
    public static FoundryRetryPolicy NoDelay(int maxRetries = 3) =>
        new(new AiGatewayResilienceOptions { MaxRetries = maxRetries }, (_, _) => Task.CompletedTask);

    /// <summary>Records every requested wait instead of sleeping.</summary>
    public static (FoundryRetryPolicy Policy, List<TimeSpan> Delays) Recording(int maxRetries = 3)
    {
        var delays = new List<TimeSpan>();
        var policy = new FoundryRetryPolicy(
            new AiGatewayResilienceOptions { MaxRetries = maxRetries },
            (wait, _) =>
            {
                delays.Add(wait);
                return Task.CompletedTask;
            });
        return (policy, delays);
    }
}
