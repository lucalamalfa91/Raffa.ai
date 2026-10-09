namespace Raffa.SharedKernel.Market;

/// <summary>
/// Structured, internal provenance of one market number (F7-T11 / F7-D04, decision D5): where the
/// figure comes from, how many comparables stand behind it and as of when. Travels with every
/// market figure (<c>MarketNote</c>, <see cref="MarketPriceMatch"/>, <see cref="MarketPriceEstimate"/>,
/// the <c>GET /api/market/records/{id}</c> payload) so code and tests read these fields instead of
/// parsing the free-text provenance label with a regex.
///
/// <para>
/// <b>Never rendered to the user by itself.</b> Market data is presented as market data (D5): no
/// "demo" badge and no environment gate is derived from <see cref="IsDemo"/>. Whether a label — or a
/// real source name — is shown at all is a configuration switch (<c>Market:Provenance</c>,
/// <c>Raffa.Market.Contracts.MarketProvenanceOptions</c>), so a real source can be switched on later
/// without rewriting the data.
/// </para>
/// </summary>
/// <param name="SourceClass">Machine-readable class of the source, one of <see cref="MarketSourceClasses"/>.</param>
/// <param name="IsDemo">True when the figure comes from a synthetic corpus. Internal metadata only —
/// see the type remarks.</param>
/// <param name="N">Comparables behind the figure (sample size); <c>0</c> when the figure is an
/// estimate with no sample.</param>
/// <param name="AsOf">When the underlying data was last refreshed (the record's own date, never "now").</param>
/// <param name="Unit">What the figure is priced per (e.g. <c>"per user / year"</c>), when the corpus says.</param>
/// <param name="Source">Source identifier (e.g. the provider name or <c>"mock"</c>), internal.</param>
public sealed record MarketProvenanceInfo(
    string SourceClass,
    bool IsDemo,
    int N,
    DateTimeOffset AsOf,
    string? Unit,
    string Source);

/// <summary>Known <see cref="MarketProvenanceInfo.SourceClass"/> values.</summary>
public static class MarketSourceClasses
{
    /// <summary>Synthetic corpus (the checked-in mock feed).</summary>
    public const string Mock = "mock";

    /// <summary>Official list price.</summary>
    public const string ListPrice = "list_price";

    /// <summary>Public procurement data (Consip, ANAC, TED...).</summary>
    public const string PublicProcurement = "public_procurement";

    /// <summary>Anonymised pool of closed deals from tenants.</summary>
    public const string TenantPool = "tenant_pool";

    /// <summary>Third-party data provider.</summary>
    public const string Provider = "provider";

    /// <summary>A band converted from another market record.</summary>
    public const string Converted = "converted";

    /// <summary>A model estimate, with no sample behind it.</summary>
    public const string AiEstimate = "ai_estimate";

    /// <summary>A figure built from records of more than one class (e.g. a bundle).</summary>
    public const string Mixed = "mixed";
}
