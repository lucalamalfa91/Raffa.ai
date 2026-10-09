using System.Text;

namespace Raffa.AiGateway.Foundry;

/// <summary>
/// The one canonical form of a public web source URL, so the research client can tell that the
/// tool's <c>url_citation</c> annotation and the model's own <c>sources[]</c> entry name the same
/// page: <c>https</c>, lower-case host, no user-info, no default port, no fragment, no tracking
/// parameters (<c>utm_*</c>, <c>gclid</c>, <c>fbclid</c>, <c>msclkid</c>), no trailing slash on the
/// path. Pure; <see langword="null"/> for anything that is not an absolute http(s) URL.
/// </summary>
public static class WebSourceUrl
{
    private static readonly string[] TrackingParameters = ["gclid", "fbclid", "msclkid", "mc_cid", "mc_eid"];

    public static string? Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }

        var builder = new StringBuilder("https://").Append(uri.Host.ToLowerInvariant());

        // The scheme's own default port (:80 on http, :443 on https) is dropped; any other is kept.
        if (!uri.IsDefaultPort)
        {
            builder.Append(':').Append(uri.Port);
        }

        builder.Append(uri.AbsolutePath.TrimEnd('/'));

        var query = string.Join('&', uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair => !IsTracking(pair)));
        if (query.Length > 0)
        {
            builder.Append('?').Append(query);
        }

        return builder.ToString();
    }

    private static bool IsTracking(string pair)
    {
        var name = pair.Split('=', 2)[0];
        return name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
            || TrackingParameters.Contains(name, StringComparer.OrdinalIgnoreCase);
    }
}
