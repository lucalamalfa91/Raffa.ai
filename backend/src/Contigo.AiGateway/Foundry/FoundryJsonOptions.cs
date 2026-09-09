using System.Text.Json;

namespace Contigo.AiGateway.Foundry;

/// <summary>
/// One shared <see cref="JsonSerializerOptions"/> instance for every Foundry wire call —
/// <see cref="JsonSerializerDefaults.Web"/> (camelCase, case-insensitive) matches both this
/// gateway's own structured-output schemas (already written camelCase, e.g.
/// <c>"canDetermine"</c>) and Azure's own camelCase response fields not covered by an explicit
/// <c>JsonPropertyName</c> (<see cref="Wire"/> DTOs use explicit names only where Azure's wire
/// shape is snake_case, e.g. <c>response_format</c>).
/// </summary>
internal static class FoundryJsonOptions
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
}
