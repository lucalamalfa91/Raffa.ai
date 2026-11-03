using System.Text;
using System.Text.Json;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// A one-line, non-secret description of an access token's claims, for diagnosing why Microsoft Graph
/// refused the workload identity (401/403): the audience it was issued for, the tenant, the object id of
/// the identity and the application roles it carries. Only those five claims are read; the token string
/// itself is never returned, logged or stored.
/// </summary>
internal static class TokenClaimsSummary
{
    public static string Describe(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2)
            {
                return "token not a JWT";
            }

            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            var root = document.RootElement;

            string Text(string name) =>
                root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "-";

            var roles = root.TryGetProperty("roles", out var rolesElement) && rolesElement.ValueKind == JsonValueKind.Array
                ? string.Join("|", rolesElement.EnumerateArray().Select(role => role.GetString()))
                : "none";

            return $"token aud={Text("aud")} tid={Text("tid")} oid={Text("oid")} roles=[{roles}]";
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            return "token claims unreadable";
        }
    }
}
