using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Contigo.AiGateway.Configuration;
using Contigo.SharedKernel;

namespace Contigo.AiGateway.Foundry;

/// <summary>
/// Shared low-level POST-JSON-get-JSON caller for the two Foundry roles that fit a plain
/// request/response shape (chat completions via <see cref="FoundryChatCompletionsClient"/>,
/// embeddings via <see cref="FoundryEmbedClient"/>). <see cref="FoundryOcrClient"/> does not use
/// this type — Document Intelligence's long-running-operation contract (202 + a polled
/// <c>Operation-Location</c>) needs raw <see cref="HttpResponseMessage"/>/header access this
/// type's "parse the JSON body or fail" contract deliberately does not expose.
///
/// Authenticates every call via <see cref="FoundryTokenProvider"/> (Microsoft Entra ID / managed
/// identity — task E13/F01/US01/T02: "auth via DefaultAzureCredential..., never a key"), never an
/// API key header. Never throws on a non-success response — every failure becomes a
/// <see cref="Result{T}.Failure"/>, this codebase's own convention for expected failures.
/// </summary>
public sealed class FoundryHttpJsonClient(
    HttpClient httpClient, FoundryTokenProvider tokenProvider, AiGatewayFoundryOptions foundryOptions)
{
    public async Task<Result<TResponse>> PostAsync<TRequest, TResponse>(
        string relativeUrl, TRequest body, CancellationToken cancellationToken)
        where TRequest : notnull
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, relativeUrl)
        {
            Content = JsonContent.Create(body, options: FoundryJsonOptions.Web),
        };

        var token = await tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (!string.IsNullOrWhiteSpace(foundryOptions.ProjectName))
        {
            request.Headers.TryAddWithoutValidation("x-ms-foundry-project", foundryOptions.ProjectName);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return Result<TResponse>.Failure(
                $"Foundry request to '{relativeUrl}' failed with {(int)response.StatusCode} " +
                $"{response.StatusCode}: {responseText}");
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
}
