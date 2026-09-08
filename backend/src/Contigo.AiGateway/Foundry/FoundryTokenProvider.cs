using Azure.Core;

namespace Contigo.AiGateway.Foundry;

/// <summary>
/// Acquires Microsoft Entra ID bearer tokens for calling the shared Azure AI services account
/// (ADR-008) — chat/embeddings and Document Intelligence alike, since both ride the same
/// <c>*.cognitiveservices.azure.com</c> resource on one account. Task E13/F01/US01/T02
/// (foundry-gateway): "auth via DefaultAzureCredential (managed identity), never a key" — this
/// type is the one place that <see cref="TokenCredential.GetTokenAsync"/> is ever called from, so
/// every Foundry HTTP call (<see cref="FoundryHttpJsonClient"/>, <see cref="FoundryOcrClient"/>)
/// shares one token cache instead of re-authenticating per call.
///
/// Depends on the abstract <see cref="TokenCredential"/>, not the concrete
/// <see cref="Azure.Identity.DefaultAzureCredential"/> — <see cref="ServiceCollectionExtensions
/// .AddAiGatewayModule"/> is the only place that constructs the real managed-identity credential;
/// every unit test in <c>Contigo.AiGateway.Tests.Foundry</c> substitutes a fake
/// <see cref="TokenCredential"/> that never performs the environment/IMDS probing
/// <see cref="Azure.Identity.DefaultAzureCredential.GetTokenAsync"/> does — "no live Azure in unit
/// tests" per that task's own coding objective.
/// </summary>
/// <param name="credential">
/// The credential to authenticate with — <see cref="Azure.Identity.DefaultAzureCredential"/> in
/// production (managed identity on Container Apps, developer sign-in locally), a fake in tests.
/// </param>
public sealed class FoundryTokenProvider(TokenCredential credential)
{
    /// <summary>
    /// Resource-audience scope for the whole Cognitive Services multi-service account (Document
    /// Intelligence, Azure OpenAI-compatible chat/embeddings, and every other capability on the
    /// same account) — tied to the endpoint's own <c>*.cognitiveservices.azure.com</c> hostname,
    /// not the newer, model-catalog-scoped <c>https://ai.azure.com/.default</c> audience, because
    /// Document Intelligence is not itself a Foundry Models catalog entry and needs the classic
    /// Cognitive Services audience to authorize.
    /// </summary>
    private const string Scope = "https://cognitiveservices.azure.com/.default";

    /// <summary>
    /// Refresh an already-cached token this far before its actual expiry, so a request never
    /// races a token that is valid when read but expired by the time it reaches Azure.
    /// </summary>
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);

    private readonly object _lock = new();
    private AccessToken? _cached;

    /// <summary>
    /// Returns a valid bearer token string, fetching or refreshing it first only when the cached
    /// one is missing or within <see cref="RefreshSkew"/> of expiry. Not thread-hardened beyond a
    /// simple lock around the cache read/write — a redundant concurrent refresh would still return
    /// a correct token, just without perfect de-duplication, which is an acceptable trade-off for
    /// this gateway's call volume (never called in a hot per-token-scoped loop).
    /// </summary>
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        AccessToken? cached;
        lock (_lock)
        {
            cached = _cached;
        }

        if (cached is { } token && token.ExpiresOn > DateTimeOffset.UtcNow + RefreshSkew)
        {
            return token.Token;
        }

        var fresh = await credential
            .GetTokenAsync(new TokenRequestContext([Scope]), cancellationToken)
            .ConfigureAwait(false);

        lock (_lock)
        {
            _cached = fresh;
        }

        return fresh.Token;
    }
}
