using System.Security.Cryptography;
using System.Text;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Foundry;

/// <summary>
/// Builds <see cref="AiCallMetadata"/> for every Foundry per-role client, the same SHA-256
/// content-hash convention <see cref="Fixtures.FixtureAiGateway"/>'s own private
/// <c>BuildMetadata</c> helpers use (ADR-011 "Assumptions": input hash = a content hash of the
/// retrieved evidence/prompt, never the input itself) — shared here once instead of duplicated
/// across five per-role client classes. Carries the provider's token usage when the role reports one.
/// </summary>
public static class FoundryCallMetadataFactory
{
    public static AiCallMetadata Build(
        AiModelSelection model, string promptVersion, IClock clock, string input, AiTokenUsage? usage = null)
    {
        var inputHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
        return new AiCallMetadata(model.ModelId, model.ModelVersion, promptVersion, clock.UtcNow, inputHash, usage);
    }

    /// <summary>Byte-input twin, for the <c>ocr</c> role whose input is already bytes (mirrors
    /// <c>FixtureAiGateway</c>'s own two-overload split).</summary>
    public static AiCallMetadata Build(
        AiModelSelection model, string promptVersion, IClock clock, ReadOnlySpan<byte> input)
    {
        var inputHash = Convert.ToHexString(SHA256.HashData(input));
        return new AiCallMetadata(model.ModelId, model.ModelVersion, promptVersion, clock.UtcNow, inputHash);
    }
}
