using Contigo.AiGateway.Configuration;

namespace Contigo.AiGateway.Foundry;

/// <summary>
/// Which Azure OpenAI data-plane surface a chat/embeddings call uses, decided once from
/// <see cref="AiGatewayFoundryOptions.OpenAiApiVersion"/>:
/// <list type="bullet">
/// <item>unset — the GA <c>openai/v1/{operation}</c> route: the deployment travels in the request
/// body (<c>model</c>), there is no <c>api-version</c>, and structured outputs,
/// <c>max_completion_tokens</c> and <c>reasoning_effort</c> are all supported;</item>
/// <item>a date — the classic <c>openai/deployments/{deployment}/{operation}?api-version=</c>
/// route. Anything older than <see cref="MinimumDateBasedApiVersion"/> is refused: it predates
/// <c>response_format: json_schema</c> and every call would fail with a 400 (which is exactly what
/// the original <c>2024-06-01</c> constant did).</item>
/// </list>
/// </summary>
public static class FoundryOpenAiRoutes
{
    /// <summary>First api-version that accepts <c>response_format: json_schema</c> (Microsoft Learn,
    /// "How to use structured outputs": "API version 2024-08-01-preview is the first version that
    /// supports structured outputs"). The GA choice is <c>2024-10-21</c>.</summary>
    public const string MinimumDateBasedApiVersion = "2024-08-01";

    /// <summary>The resolved route.</summary>
    /// <param name="RelativeUrl">Relative to the account endpoint.</param>
    /// <param name="ModelInBody">Whether the deployment name must be sent as the body's <c>model</c>.</param>
    public sealed record Route(string RelativeUrl, bool ModelInBody);

    public static Route ChatCompletions(AiGatewayFoundryOptions options, string deployment) =>
        Build(options, deployment, "chat/completions");

    public static Route Embeddings(AiGatewayFoundryOptions options, string deployment) =>
        Build(options, deployment, "embeddings");

    private static Route Build(AiGatewayFoundryOptions options, string deployment, string operation)
    {
        var apiVersion = options.OpenAiApiVersion?.Trim();
        if (string.IsNullOrEmpty(apiVersion))
        {
            return new Route($"openai/v1/{operation}", ModelInBody: true);
        }

        // ISO dates compare lexicographically, and a "-preview" suffix sorts after the bare date,
        // so "2024-08-01-preview" and every later version pass while "2024-06-01" is refused.
        if (string.CompareOrdinal(apiVersion, MinimumDateBasedApiVersion) < 0)
        {
            throw new InvalidOperationException(
                $"AiGateway:OpenAiApiVersion '{apiVersion}' predates structured outputs " +
                $"(minimum {MinimumDateBasedApiVersion}; GA 2024-10-21). Unset it to use the " +
                "openai/v1 route, or configure 2024-10-21 or later.");
        }

        return new Route(
            $"openai/deployments/{Uri.EscapeDataString(deployment)}/{operation}?api-version={Uri.EscapeDataString(apiVersion)}",
            ModelInBody: false);
    }
}
