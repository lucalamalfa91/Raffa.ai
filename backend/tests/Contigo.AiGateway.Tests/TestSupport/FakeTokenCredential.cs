using Azure.Core;

namespace Contigo.AiGateway.Tests.TestSupport;

/// <summary>
/// Fake <see cref="TokenCredential"/> for every Foundry test — never
/// <see cref="Azure.Identity.DefaultAzureCredential"/>, which probes real environment/managed-
/// identity/CLI sources on <c>GetTokenAsync</c>. Task E13/F01/US01/T02: "no live Azure in unit
/// tests" — this type is what makes that true for every <see cref="FoundryTokenProvider"/>-based
/// test without needing network access or a real credential of any kind.
/// </summary>
public sealed class FakeTokenCredential(string token = "fake-foundry-token") : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        new(token, DateTimeOffset.UtcNow.AddHours(1));

    public override ValueTask<AccessToken> GetTokenAsync(
        TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        new(GetToken(requestContext, cancellationToken));
}
