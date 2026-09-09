using System.Net;
using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Foundry;
using Contigo.AiGateway.Tests.TestSupport;

namespace Contigo.AiGateway.Tests.Foundry;

/// <summary>Proves <see cref="FoundryRetryPolicy"/>: which outcomes are retried, how long it
/// waits, and the <see cref="AiGatewayErrors.UnavailablePrefix"/> failure on exhaustion.</summary>
public class FoundryRetryPolicyTests
{
    private static readonly Uri BaseAddress = new("https://fake-foundry.example.com/");

    private static Func<CancellationToken, Task<HttpRequestMessage>> Get() =>
        _ => Task.FromResult(new HttpRequestMessage(HttpMethod.Get, "probe"));

    [Fact]
    public async Task Waits_the_Retry_After_of_a_429_then_returns_the_next_success()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.Json(HttpStatusCode.TooManyRequests, """{"error":{"code":"429","message":"Requests to the ChatCompletions_Create Operation have exceeded token rate limit"}}""", ("Retry-After", "2")),
            FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler) { BaseAddress = BaseAddress };
        var (policy, delays) = TestRetryPolicies.Recording();

        var result = await policy.SendAsync(httpClient, Get(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.Value.StatusCode);
        Assert.Equal([TimeSpan.FromSeconds(2)], delays);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Retries_server_errors_with_a_growing_backoff()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{}"),
            FakeHttpMessageHandler.Json(HttpStatusCode.BadGateway, "{}"),
            FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler) { BaseAddress = BaseAddress };
        var (policy, delays) = TestRetryPolicies.Recording();

        var result = await policy.SendAsync(httpClient, Get(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, delays.Count);
        Assert.InRange(delays[0].TotalMilliseconds, 400, 600);
        Assert.InRange(delays[1].TotalMilliseconds, 800, 1200);
    }

    [Fact]
    public async Task Gives_up_after_the_configured_retries_with_the_unavailable_prefix()
    {
        var handler = new FakeHttpMessageHandler(FakeHttpMessageHandler.Json(HttpStatusCode.ServiceUnavailable, "{}"));
        using var httpClient = new HttpClient(handler) { BaseAddress = BaseAddress };
        var policy = TestRetryPolicies.NoDelay(maxRetries: 3);

        var result = await policy.SendAsync(httpClient, Get(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith(AiGatewayErrors.UnavailablePrefix, result.Error, StringComparison.Ordinal);
        Assert.Contains("503", result.Error, StringComparison.Ordinal);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task Does_not_retry_a_client_error()
    {
        var handler = new FakeHttpMessageHandler(FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, "{}"));
        using var httpClient = new HttpClient(handler) { BaseAddress = BaseAddress };
        var policy = TestRetryPolicies.NoDelay();

        var result = await policy.SendAsync(httpClient, Get(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.Value.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Retries_a_connection_failure()
    {
        var attempts = 0;
        var handler = new FakeHttpMessageHandler(
            _ =>
            {
                attempts++;
                throw new HttpRequestException("connection reset");
            },
            FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler) { BaseAddress = BaseAddress };
        var policy = TestRetryPolicies.NoDelay();

        var result = await policy.SendAsync(httpClient, Get(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, attempts);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Propagates_the_callers_own_cancellation_instead_of_retrying_it()
    {
        // The handler reports the cancellation the way HttpClient does when the caller's token is
        // already cancelled; the policy must not mistake it for a timeout and retry.
        var handler = new FakeHttpMessageHandler(_ => throw new TaskCanceledException("The operation was canceled."));
        using var httpClient = new HttpClient(handler) { BaseAddress = BaseAddress };
        var policy = TestRetryPolicies.NoDelay();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => policy.SendAsync(httpClient, Get(), cancelled.Token));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Retries_a_timeout_shaped_cancellation_nobody_asked_for()
    {
        // HttpClient.Timeout surfaces as a TaskCanceledException while the caller's own token is
        // still live: that is a transient outcome, not the caller's decision.
        var handler = new FakeHttpMessageHandler(
            _ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."),
            FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler) { BaseAddress = BaseAddress };
        var policy = TestRetryPolicies.NoDelay();

        var result = await policy.SendAsync(httpClient, Get(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void A_huge_Retry_After_is_capped()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("Retry-After", "600");

        Assert.Equal(FoundryRetryPolicy.MaxRetryAfter, FoundryRetryPolicy.ReadRetryAfter(response));
    }

    [Fact]
    public void Transient_statuses_are_exactly_throttling_and_server_failures()
    {
        Assert.True(FoundryRetryPolicy.IsTransient(HttpStatusCode.TooManyRequests));
        Assert.True(FoundryRetryPolicy.IsTransient(HttpStatusCode.InternalServerError));
        Assert.True(FoundryRetryPolicy.IsTransient(HttpStatusCode.GatewayTimeout));
        Assert.False(FoundryRetryPolicy.IsTransient(HttpStatusCode.BadRequest));
        Assert.False(FoundryRetryPolicy.IsTransient(HttpStatusCode.Unauthorized));
        Assert.False(FoundryRetryPolicy.IsTransient(HttpStatusCode.NotFound));
    }

    [Fact]
    public void Defaults_keep_the_worst_case_inside_the_synchronous_upload_path()
    {
        var options = new AiGatewayResilienceOptions();

        Assert.Equal(3, options.MaxRetries);
        Assert.Equal(500, options.InitialBackoffMilliseconds);
        Assert.Equal(8_000, options.MaxBackoffMilliseconds);
        Assert.Equal(180, options.RequestTimeoutSeconds);
    }
}
