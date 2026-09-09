namespace Contigo.Chat.Application.Capabilities;

/// <summary>
/// The R-SYS-03 "feature card" citation shape: "When Ask explains a Contigo capability, the
/// citation card is a **feature card** (`corpus: contigo`): title = capability, snippet = what it
/// does, `href` = route." (story us-01-capability-catalog AC-4). Sits alongside the tenant/market
/// citation shapes a later task adds (`Contigo.AiGateway.Contracts.AiCitation` for tenant
/// evidence) — this one always resolves from the static catalog, never a retrieval call.
/// </summary>
/// <param name="Corpus">Always <see cref="ContigoCorpus"/> for a feature citation — carried as a
/// field (not hard-coded by every caller) so a reply that mixes citation kinds can branch on it.</param>
/// <param name="Title">= <see cref="Capability.Title"/> (R-SYS-03 "title = capability").</param>
/// <param name="Subtitle">= <see cref="Capability.RoutePattern"/> — the route shown under the
/// title, e.g. on a citation card.</param>
/// <param name="Snippet">= <see cref="Capability.Description"/> (R-SYS-03 "snippet = what it does").</param>
/// <param name="Href">= <see cref="Capability.RoutePattern"/> (R-SYS-03 "href = route"). Deliberately
/// the catalog's own base pattern, not an id-scoped variant: <see cref="For"/> takes no
/// <c>RoutingContext</c> — building an id-scoped href is <see cref="CapabilityRouting"/>'s job.</param>
public sealed record FeatureCitation(string Corpus, string Title, string Subtitle, string Snippet, string Href)
{
    public const string ContigoCorpus = "contigo";

    public static FeatureCitation For(Capability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);

        return new FeatureCitation(
            ContigoCorpus,
            capability.Title,
            capability.RoutePattern,
            capability.Description,
            capability.RoutePattern);
    }
}
