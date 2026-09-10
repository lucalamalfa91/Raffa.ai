using System.Net;
using Raffa.AiGateway.Configuration;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Foundry;

/// <summary>
/// Bounded retry for every Foundry HTTP call (<see cref="AiGatewayResilienceOptions"/>): a
/// transient outcome — <c>429</c>, <c>500</c>, <c>502</c>, <c>503</c>, <c>504</c>, a connection
/// failure, or a per-request timeout — is retried up to <see cref="AiGatewayResilienceOptions.MaxRetries"/>
/// times, waiting the provider's own <c>Retry-After</c> when it sends one and an exponential,
/// jittered backoff otherwise. Every other response is handed back untouched for the caller to
/// read. Exhaustion is a <see cref="Result{T}"/> failure prefixed with
/// <see cref="AiGatewayErrors.UnavailablePrefix"/>, so the admission gate can answer a retryable
/// 503 instead of a 400. No third-party resilience package: this project's whole point is owning
/// the wire, and the fake handler in <c>Raffa.AiGateway.Tests</c> already exercises this shape.
/// </summary>
/// <param name="options">Retry counts and backoff bounds.</param>
/// <param name="delay">Wait implementation — <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
/// by default; tests inject a recorder that returns immediately.</param>
public sealed class FoundryRetryPolicy(
    AiGatewayResilienceOptions options, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    /// <summary>A provider-sent <c>Retry-After</c> is honoured up to this long; a larger value is
    /// capped so a throttled call cannot park the synchronous upload path for minutes.</summary>
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    /// <summary>
    /// Sends the request produced by <paramref name="requestFactory"/> (a factory, not an instance:
    /// an <see cref="HttpRequestMessage"/> cannot be re-sent, and each attempt re-attaches a token
    /// from <see cref="FoundryTokenProvider"/>'s cache). The returned response is owned by the caller.
    /// </summary>
    public async Task<Result<HttpResponseMessage>> SendAsync(
        HttpClient httpClient,
        Func<CancellationToken, Task<HttpRequestMessage>> requestFactory,
        CancellationToken cancellationToken)
    {
        var maxRetries = Math.Max(0, options.MaxRetries);
        string? lastOutcome = null;
        string? url = null;

        for (var attempt = 0; ; attempt++)
        {
            TimeSpan? retryAfter = null;
            HttpResponseMessage? response = null;

            try
            {
                using var request = await requestFactory(cancellationToken).ConfigureAwait(false);
                url ??= request.RequestUri?.ToString();
                response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception)
            {
                lastOutcome = $"{exception.GetType().Name}: {exception.Message}";
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient.Timeout surfaces as a cancellation that nobody asked for.
                lastOutcome = $"timed out after {httpClient.Timeout.TotalSeconds:0}s";
            }

            if (response is not null)
            {
                if (!IsTransient(response.StatusCode))
                {
                    return Result<HttpResponseMessage>.Success(response);
                }

                retryAfter = ReadRetryAfter(response);
                lastOutcome = $"{(int)response.StatusCode} {response.StatusCode}";
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(body))
                {
                    lastOutcome += ": " + Wire.AzureErrorEnvelope.Describe(body);
                }

                response.Dispose();
            }

            if (attempt >= maxRetries)
            {
                return Result<HttpResponseMessage>.Failure(
                    $"{AiGatewayErrors.UnavailablePrefix} '{url}' still failing after {attempt} " +
                    $"retr{(attempt == 1 ? "y" : "ies")} (last outcome: {lastOutcome}).");
            }

            await _delay(retryAfter ?? Backoff(attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The statuses Azure documents as retryable: throttling and server-side failures.</summary>
    public static bool IsTransient(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.TooManyRequests or
        HttpStatusCode.InternalServerError or
        HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.GatewayTimeout;

    /// <summary><c>Retry-After</c> as a delay (delta seconds or an HTTP date), capped at
    /// <see cref="MaxRetryAfter"/>; <see langword="null"/> when absent or unparseable.</summary>
    public static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        TimeSpan? value = header?.Delta ?? (header?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        if (value is not { } wait || wait < TimeSpan.Zero)
        {
            return null;
        }

        return wait > MaxRetryAfter ? MaxRetryAfter : wait;
    }

    private TimeSpan Backoff(int attempt)
    {
        var initial = Math.Max(1, options.InitialBackoffMilliseconds);
        var max = Math.Max(initial, options.MaxBackoffMilliseconds);
        var exponential = Math.Min(max, initial * Math.Pow(2, attempt));

        // ±20 % jitter so several throttled stages do not retry in lock-step.
        var jitter = 0.8 + (Random.Shared.NextDouble() * 0.4);
        return TimeSpan.FromMilliseconds(Math.Min(max, exponential * jitter));
    }
}
