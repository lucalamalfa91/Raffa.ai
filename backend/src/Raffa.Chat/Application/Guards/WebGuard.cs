using System.Net;
using System.Text.RegularExpressions;
using Raffa.AiGateway.Contracts;

namespace Raffa.Chat.Application.Guards;

/// <summary>
/// ADR-030's own guard on a web-research summary, run before <see cref="NumericGuard"/> and
/// <see cref="GroundingGuard"/>: at least one source; every source is an absolute
/// <c>https</c> URL to a public host (never an IP address, <c>localhost</c> or a private name —
/// a hosted web search must not be turned into a probe of Raffa's own network); every inline
/// <c>[n]</c> marker is within the source list; and every URL the model wrote into the prose is
/// one of the sources (the persona is told to write none at all). Pure and synchronous.
/// </summary>
public static class WebGuard
{
    private static readonly Regex InlineCitationPattern = new(@"\[(\d+)\]", RegexOptions.Compiled);
    private static readonly Regex UrlInProsePattern = new(@"https?://[^\s)\]>""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] ForbiddenHostSuffixes = [".local", ".internal", ".localhost", ".lan", ".home", ".corp"];

    public static GuardVerdict Validate(string? summaryMarkdown, IReadOnlyList<AiWebSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (sources.Count == 0)
        {
            return GuardVerdict.Fail("the research role returned no web source at all (ADR-030: a summary without a source is not shown).");
        }

        foreach (var source in sources)
        {
            if (!IsAcceptableSource(source.Url, out var why))
            {
                return GuardVerdict.Fail($"web source '{source.Url}' is not acceptable: {why}");
            }
        }

        var markdown = summaryMarkdown ?? string.Empty;
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return GuardVerdict.Fail("the research role returned an empty summary.");
        }

        foreach (Match match in InlineCitationPattern.Matches(markdown))
        {
            var n = int.Parse(match.Groups[1].Value);
            if (n < 1 || n > sources.Count)
            {
                return GuardVerdict.Fail(
                    $"inline citation marker '[{n}]' names no web source — only {sources.Count} were returned by the search tool.");
            }
        }

        var known = sources.Select(s => Normalize(s.Url)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in UrlInProsePattern.Matches(markdown))
        {
            if (!known.Contains(Normalize(match.Value)))
            {
                return GuardVerdict.Fail(
                    $"the summary names a URL that is not one of the search tool's own sources ('{match.Value}').");
            }
        }

        return GuardVerdict.Ok;
    }

    /// <summary>Absolute <c>https</c>, a DNS host name (not an IP), never local/private.</summary>
    public static bool IsAcceptableSource(string? url, out string reason)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            reason = "not an absolute URL.";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            reason = "only https sources are accepted.";
            return false;
        }

        if (uri.HostNameType != UriHostNameType.Dns || IPAddress.TryParse(uri.Host, out _))
        {
            reason = "the host must be a DNS name, never an IP address.";
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host == "localhost" || !host.Contains('.', StringComparison.Ordinal)
            || ForbiddenHostSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.Ordinal)))
        {
            reason = "the host looks local or private.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static string Normalize(string url) => url.Trim().TrimEnd('/', '.', ',');
}
