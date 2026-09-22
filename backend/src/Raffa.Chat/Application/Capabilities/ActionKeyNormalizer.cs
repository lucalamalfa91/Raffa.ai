namespace Raffa.Chat.Application.Capabilities;

/// <summary>
/// Turns the action keys an `answer` call returned into the bare <see cref="CapabilityCatalog"/>
/// keys <see cref="CapabilityRouting"/> can resolve — before <c>Guards.GroundingGuard</c> ever sees
/// them. A Raffa feature item reaches the model as a pack item keyed <c>raffa:{capabilityKey}</c>
/// (<c>Raffa.Api.AskCopilotService.BuildFeatureCitationPackItem</c>), so a model following the
/// persona prompt's rule 7 naturally echoes <c>raffa:renewals</c> instead of <c>renewals</c>, or the
/// feature's own route (<c>/renewals</c>). Either used to fail the whole grounded answer over one
/// optional button.
///
/// <para>
/// An action is a convenience, never a claim: a key that still resolves to nothing here is dropped
/// (no button), not escalated into a guard violation. "Hrefs are never model-authored" (R-SYS-02)
/// is untouched — every surviving key is a real catalog key, and the href itself is still built
/// only by <see cref="CapabilityRouting"/>. Pure and synchronous.
/// </para>
/// </summary>
public static class ActionKeyNormalizer
{
    private const string RaffaPrefix = "raffa:";

    /// <summary>
    /// Trims each key, strips a leading <c>raffa:</c> corpus prefix, maps an exact catalog route
    /// (<c>/renewals</c>) to its capability key, and drops anything that still is not a catalog key
    /// (a <c>raffa:playbook:*</c> item, a free-form URL, an invented key). Order-preserving,
    /// duplicates folded; never <see langword="null"/>.
    /// </summary>
    public static IReadOnlyList<string> Normalize(IReadOnlyList<string>? keys)
    {
        if (keys is null || keys.Count == 0)
        {
            return [];
        }

        var normalized = new List<string>(keys.Count);
        foreach (var raw in keys)
        {
            if (ToCatalogKey(raw) is { } key && !normalized.Contains(key, StringComparer.Ordinal))
            {
                normalized.Add(key);
            }
        }

        return normalized;
    }

    /// <summary>
    /// <see cref="Normalize"/>, then drops every key whose route needs an object id
    /// <paramref name="context"/> does not carry — <see cref="CapabilityCatalog.ContractDetailKey"/>
    /// without a <see cref="RoutingContext.ContractId"/>, <see cref="CapabilityCatalog.DocumentsReviewKey"/>
    /// without a <see cref="RoutingContext.DocumentId"/>. <see cref="CapabilityRouting"/> throws for
    /// an unfilled placeholder (a caller contract violation), so a model naming Contract 360 on a
    /// portfolio-wide turn must lose that button here rather than fail the turn.
    /// </summary>
    public static IReadOnlyList<string> Routable(IReadOnlyList<string>? keys, RoutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Normalize(keys)
            .Where(key => key switch
            {
                CapabilityCatalog.ContractDetailKey => context.ContractId is not null,
                CapabilityCatalog.DocumentsReviewKey => context.DocumentId is not null,
                _ => true,
            })
            .ToList();
    }

    private static string? ToCatalogKey(string? raw)
    {
        var key = raw?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (key.StartsWith(RaffaPrefix, StringComparison.OrdinalIgnoreCase))
        {
            key = key[RaffaPrefix.Length..].Trim();
        }

        if (CapabilityCatalog.Find(key) is { } capability)
        {
            return capability.Key;
        }

        // A route the model copied from a feature item's href ("/renewals") — only an exact,
        // placeholder-free catalog route counts, never a prefix or a deep link it composed itself.
        var byRoute = CapabilityCatalog.All.FirstOrDefault(c =>
            !c.RoutePattern.Contains('{', StringComparison.Ordinal) &&
            string.Equals(c.RoutePattern, key, StringComparison.OrdinalIgnoreCase));
        if (byRoute is not null)
        {
            return byRoute.Key;
        }

        // Last: a casing slip ("Renewals") on an otherwise real key.
        return CapabilityCatalog.All
            .FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))?.Key;
    }
}
