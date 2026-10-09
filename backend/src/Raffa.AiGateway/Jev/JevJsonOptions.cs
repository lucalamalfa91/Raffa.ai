using System.Text.Json;

namespace Raffa.AiGateway.Jev;

/// <summary>One shared <see cref="JsonSerializerOptions"/> for every Jev wire call — camelCase,
/// case-insensitive, the same convention <c>Foundry.FoundryJsonOptions</c> uses, kept as its own
/// instance rather than shared across the two vendor folders so a future change to one never
/// silently reaches the other.</summary>
internal static class JevJsonOptions
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
}
