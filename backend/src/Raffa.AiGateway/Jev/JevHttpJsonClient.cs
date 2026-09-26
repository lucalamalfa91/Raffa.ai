using System.Net.Http.Headers;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Foundry;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Jev;

/// <summary>
/// The one JSON-over-HTTP primitive every Jev call uses: a static OpenRouter API key (unlike
/// Foundry's per-request managed-identity token — <see cref="AiGatewayJevOptions.ApiKey"/> is a
/// Container Apps secret, ADR-011), the same bounded-retry policy Foundry calls already share
/// (<see cref="FoundryRetryPolicy"/> is a generic HTTP-transient retry, not Foundry-specific wire
/// logic — reused rather than duplicated for this pilot), and a non-success status turned into a
/// readable <see cref="Result{T}"/> failure carrying <see cref="AiGatewayErrors.UnavailablePrefix"/>
/// on the same transient outcomes Foundry treats as retryable, so <c>DocumentAdmissionGate</c>'s
/// existing 503-mapping needs no Jev-specific branch.
/// </summary>
public sealed class JevHttpJsonClient(
    HttpClient httpClient, AiGatewayJevOptions options, FoundryRetryPolicy? retryPolicy = null)
{
    private static readonly MediaTypeHeaderValue JsonContentType = new("application/json") { CharSet = "utf-8" };

    private readonly FoundryRetryPolicy _retryPolicy = retryPolicy ?? new FoundryRetryPolicy(new AiGatewayResilienceOptions());

    public async Task<Result<TResponse>> PostAsync<TRequest, TResponse>(
        string relativeUrl, TRequest body, CancellationToken cancellationToken)
        where TRequest : notnull
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            // A misconfigured pilot (Enabled: true, no key) must fail every call the same
            // explicit way, not fall through to an anonymous request OpenRouter would 401 on
            // with a less legible message.
            return Result<TResponse>.Failure(
                "AiGateway:Jev:ApiKey is not configured; the Jev-backed classify pilot cannot run.");
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(body, JevJsonOptions.Web);

        var sent = await _retryPolicy.SendAsync(
                httpClient,
                _ =>
                {
                    var message = new HttpRequestMessage(HttpMethod.Post, relativeUrl)
                    {
                        Content = new ByteArrayContent(payload) { Headers = { ContentType = JsonContentType } },
                    };
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

                    // OpenRouter's own recommended (optional) attribution headers -- ignored by
                    // the API if absent, harmless if present, never required for a call to succeed.
                    if (!string.IsNullOrWhiteSpace(options.HttpReferer))
                    {
                        message.Headers.TryAddWithoutValidation("HTTP-Referer", options.HttpReferer);
                    }

                    if (!string.IsNullOrWhiteSpace(options.AppTitle))
                    {
                        message.Headers.TryAddWithoutValidation("X-Title", options.AppTitle);
                    }

                    return Task.FromResult(message);
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (sent.IsFailure)
        {
            return Result<TResponse>.Failure(sent.Error);
        }

        using var response = sent.Value;
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return Result<TResponse>.Failure(
                $"Jev request to '{relativeUrl}' failed with {(int)response.StatusCode} " +
                $"{response.StatusCode}: {JevErrorEnvelope.Describe(responseText)}");
        }

        TResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<TResponse>(responseText, JevJsonOptions.Web);
        }
        catch (JsonException ex)
        {
            return Result<TResponse>.Failure(
                $"Jev response from '{relativeUrl}' was not valid JSON: {ex.Message}. " +
                "This client's response contract is unverified against a live account -- " +
                "see AiGatewayJevOptions's own doc comment.");
        }

        if (parsed is null)
        {
            return Result<TResponse>.Failure($"Jev response from '{relativeUrl}' parsed to null.");
        }

        return Result<TResponse>.Success(parsed);
    }
}
