using System.Text.Json;
using System.Text.Json.Serialization;

namespace Raffa.AiGateway.Foundry.Wire;

/// <summary>
/// The error envelope both Azure OpenAI and Document Intelligence return on a non-success status:
/// <c>{"error":{"code":"...","message":"...","innererror":{"code":"..."}}}</c>. Parsed only to turn
/// an opaque body into a readable failure text (<c>content_filter</c>, <c>429</c>,
/// <c>invalid_json_schema</c>, <c>InvalidContent</c> ...).
/// </summary>
public sealed record AzureErrorEnvelope(
    [property: JsonPropertyName("error")] AzureError? Error)
{
    /// <summary>
    /// Best-effort, one-line description of an error body: <c>code: message</c> when the envelope
    /// parses, the trimmed raw text otherwise. Never throws.
    /// </summary>
    public static string Describe(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return "(empty response body)";
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<AzureErrorEnvelope>(responseText, FoundryJsonOptions.Web);
            var error = envelope?.Error;
            if (error is not null && (error.Code is not null || error.Message is not null))
            {
                var inner = error.InnerError?.Code is { } innerCode ? $" ({innerCode})" : string.Empty;
                return $"{error.Code ?? "error"}{inner}: {error.Message ?? "(no message)"}";
            }
        }
        catch (JsonException)
        {
            // Not an Azure envelope (an HTML error page from a proxy, plain text ...) — fall through.
        }

        var trimmed = responseText.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500] + "…";
    }
}

public sealed record AzureError(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("innererror")] AzureInnerError? InnerError);

public sealed record AzureInnerError(
    [property: JsonPropertyName("code")] string? Code);
