namespace Contigo.AiGateway.Configuration;

/// <summary>
/// Retry/timeout policy for every Foundry HTTP call (<see cref="Foundry.FoundryRetryPolicy"/>).
/// Azure OpenAI answers a token-per-minute burst with <c>429</c> plus a <c>Retry-After</c>
/// header, and a shared account occasionally returns <c>5xx</c> under load; without a bounded
/// retry a single throttled call failed a whole extraction stage. Bound from
/// <c>AiGateway:Resilience</c>; the defaults keep the worst case (three retries at the maximum
/// backoff) well inside the synchronous upload path's ingress timeout.
/// </summary>
public sealed class AiGatewayResilienceOptions
{
    /// <summary>Conventional configuration section path for binding this options object.</summary>
    public const string SectionName = "AiGateway:Resilience";

    /// <summary>Retries after the first attempt on a transient outcome (429, 500, 502, 503, 504,
    /// a connection failure, a timeout). 0 disables retries.</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>First backoff when the provider sends no <c>Retry-After</c>; doubles per attempt.</summary>
    public int InitialBackoffMilliseconds { get; init; } = 500;

    /// <summary>Ceiling on the exponential backoff (a provider-sent <c>Retry-After</c> may exceed it
    /// up to <see cref="Foundry.FoundryRetryPolicy.MaxRetryAfter"/>).</summary>
    public int MaxBackoffMilliseconds { get; init; } = 8_000;

    /// <summary>Per-request <see cref="System.Net.Http.HttpClient.Timeout"/>. A frontier model
    /// returning a long extraction stage can take well over the framework's 100-second default.</summary>
    public int RequestTimeoutSeconds { get; init; } = 180;
}
