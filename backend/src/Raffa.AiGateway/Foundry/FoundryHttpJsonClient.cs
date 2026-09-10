using System.Net.Http.Headers;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Foundry.Wire;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Foundry;

/// <summary>
/// The one JSON-over-HTTP primitive every Azure OpenAI role client shares: serialize the request
/// once, send it through <see cref="FoundryRetryPolicy"/> with a fresh bearer token per attempt
/// (<see cref="FoundryTokenProvider"/>, managed identity — never a key, ADR-011), turn a
/// non-success status into a readable <see cref="Result{T}"/> failure (the Azure error envelope's
/// <c>code: message</c>, so <c>content_filter</c> or <c>invalid_json_schema</c> is visible in the
/// stage's error detail), and deserialize the success body. Hand-serialized
/// <see cref="System.Text.Json"/> over a bare <see cref="HttpClient"/> on purpose: no provider SDK
/// in this project beyond <c>Azure.Identity</c>, so the wire shape is exactly what the tests assert.
/// </summary>
public sealed class FoundryHttpJsonClient(
    HttpClient httpClient,
    FoundryTokenProvider tokenProvider,
    AiGatewayFoundryOptions foundryOptions,
    FoundryRetryPolicy? retryPolicy = null)
{
    private static readonly MediaTypeHeaderValue JsonContentType = new("application/json") { CharSet = "utf-8" };

    private readonly FoundryRetryPolicy _retryPolicy = retryPolicy ?? new FoundryRetryPolicy(new AiGatewayResilienceOptions());

    public async Task<Result<TResponse>> PostAsync<TRequest, TResponse>(
        string relativeUrl, TRequest body, CancellationToken cancellationToken)
        where TRequest : notnull
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(body, FoundryJsonOptions.Web);

        var sent = await _retryPolicy.SendAsync(
                httpClient,
                async token =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, relativeUrl)
                    {
                        Content = new ByteArrayContent(payload) { Headers = { ContentType = JsonContentType } },
                    };
                    await AttachAuthAsync(request, token).ConfigureAwait(false);
                    return request;
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
                $"Foundry request to '{relativeUrl}' failed with {(int)response.StatusCode} " +
                $"{response.StatusCode}: {AzureErrorEnvelope.Describe(responseText)}");
        }

        TResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<TResponse>(responseText, FoundryJsonOptions.Web);
        }
        catch (JsonException ex)
        {
            return Result<TResponse>.Failure(
                $"Foundry response from '{relativeUrl}' was not valid JSON: {ex.Message}");
        }

        if (parsed is null)
        {
            return Result<TResponse>.Failure($"Foundry response from '{relativeUrl}' parsed to null.");
        }

        return Result<TResponse>.Success(parsed);
    }

    private async Task AttachAuthAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (!string.IsNullOrWhiteSpace(foundryOptions.ProjectName))
        {
            // Informational per-project attribution (AiGatewayFoundryOptions.ProjectName's own doc
            // comment) — not a documented REST contract, ignored by the service, harmless.
            request.Headers.TryAddWithoutValidation("x-ms-foundry-project", foundryOptions.ProjectName);
        }
    }
}
