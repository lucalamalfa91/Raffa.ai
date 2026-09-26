using System.Text.Json;
using System.Text.Json.Serialization;

namespace Raffa.AiGateway.Jev;

/// <summary>
/// OpenRouter's error envelope (OpenAI-compatible convention): <c>{"error":{"code":...,
/// "message":"..."}}</c>. Mirrors <c>Foundry.Wire.AzureErrorEnvelope</c>'s own "best-effort,
/// one-line, never throws" contract for the identical reason — turning an opaque non-success body
/// into a readable failure text. <see cref="JevError.Code"/> is read as a raw
/// <see cref="JsonElement"/> because OpenRouter's own error responses report it as either a string
/// or a number depending on the upstream provider.
/// </summary>
public sealed record JevErrorEnvelope(
    [property: JsonPropertyName("error")] JevError? Error)
{
    public static string Describe(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return "(empty response body)";
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<JevErrorEnvelope>(responseText, JevJsonOptions.Web);
            var error = envelope?.Error;
            if (error is not null && (error.Code is not null || error.Message is not null))
            {
                var code = error.Code is { ValueKind: JsonValueKind.String or JsonValueKind.Number }
                    ? error.Code.Value.ToString()
                    : "error";
                return $"{code}: {error.Message ?? "(no message)"}";
            }
        }
        catch (JsonException)
        {
            // Not the expected envelope (a proxy's HTML error page, plain text ...) -- fall through.
        }

        var trimmed = responseText.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500] + "…";
    }
}

public sealed record JevError(
    [property: JsonPropertyName("code")] JsonElement? Code,
    [property: JsonPropertyName("message")] string? Message);
