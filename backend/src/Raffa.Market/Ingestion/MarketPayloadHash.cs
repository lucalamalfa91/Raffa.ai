using System.Security.Cryptography;
using System.Text;

namespace Raffa.Market.Ingestion;

/// <summary>
/// F7-T04: the idempotency key of a market record -- the SHA-256 (lowercase hex) of the canonical
/// payload JSON the ingestion job serializes a <see cref="Contracts.MarketDeal"/> to. Stored in
/// <c>market_record.payload_hash</c>; two runs over the same feed version and the same payload
/// produce the same hash, so the second run changes nothing.
/// </summary>
public static class MarketPayloadHash
{
    /// <summary>The 64-character lowercase hex digest of <paramref name="canonicalPayloadJson"/>.</summary>
    public static string Of(string canonicalPayloadJson) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayloadJson)));
}
