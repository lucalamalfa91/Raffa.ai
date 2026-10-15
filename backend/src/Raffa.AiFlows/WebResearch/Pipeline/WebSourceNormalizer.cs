using System.Text.RegularExpressions;
using Raffa.AiFlows.WebResearch.Configuration;
using Raffa.AiGateway.Contracts;

namespace Raffa.AiFlows.WebResearch.Pipeline;

/// <summary>One stage of the web flow after the research call (A-09): a pure, deterministic function from
/// an immutable input record to an immutable output record, named like a runner step. The research call
/// itself is the composer's (ADR-030: the one caller of <c>ResearchAsync</c>), and the runner has no
/// research role, so these stages are the steps that can run beside it; each is stateless.</summary>
public interface IWebStage<in TIn, out TOut>
{
    string Name { get; }

    TOut Run(TIn input);
}

/// <summary>The settings the normaliser reads, snapshotted from <see cref="WebResearchOptions"/>.</summary>
public sealed record WebSourcePolicy(
    IReadOnlyList<string> DeniedDomains,
    bool StripTrackingParameters,
    IReadOnlyDictionary<string, int> DomainTiers,
    int MaxSourceTier)
{
    /// <summary>Tier of a domain absent from <see cref="DomainTiers"/>.</summary>
    public const int UnlistedTier = 3;

    /// <summary>No source dropped, no URL changed: today's behaviour.</summary>
    public static WebSourcePolicy None { get; } = new([], false, new Dictionary<string, int>(), 0);

    public static WebSourcePolicy From(WebResearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new WebSourcePolicy(
            options.DeniedDomains ?? [],
            options.StripTrackingParameters,
            options.DomainTiers ?? [],
            options.MaxSourceTier);
    }

    public bool IsNoOp => DeniedDomains.Count == 0 && !StripTrackingParameters && MaxSourceTier <= 0;
}

public sealed record WebSourceNormalizerInput(string SummaryMarkdown, IReadOnlyList<AiWebSource> Sources, WebSourcePolicy Policy);

/// <param name="Dropped">Sources removed by the deny-list or the tier policy.</param>
/// <param name="Merged">Sources folded into an earlier one because they became the same URL.</param>
public sealed record WebSourceNormalizerOutput(string SummaryMarkdown, IReadOnlyList<AiWebSource> Sources, int Dropped, int Merged);

/// <summary>
/// F3-T08 — the source normaliser: the deny-list and tier policy of the sources (F3-D08), and the
/// tracking-parameter strip. The sources reach it already reconciled with the summary's <c>[n]</c>
/// markers (F3-T02), so every change renumbers the markers to match: a source folded into another is
/// cited under the survivor's number, a dropped source's markers are removed (the sentence then carries no
/// marker, which the figure check treats as unbacked). With the default policy it returns its input
/// untouched.
/// </summary>
public sealed partial class WebSourceNormalizer : IWebStage<WebSourceNormalizerInput, WebSourceNormalizerOutput>
{
    private static readonly string[] TrackingPrefixes = ["utm_", "mc_", "pk_", "_hs"];

    private static readonly HashSet<string> TrackingNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "gclid", "fbclid", "msclkid", "dclid", "yclid", "igshid", "ref_src", "mkt_tok",
    };

    public string Name => "web-source-normalizer";

    public WebSourceNormalizerOutput Run(WebSourceNormalizerInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Policy.IsNoOp || input.Sources.Count == 0)
        {
            return new WebSourceNormalizerOutput(input.SummaryMarkdown, input.Sources, 0, 0);
        }

        var kept = new List<AiWebSource>();
        var byUrl = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var newIndex = new int?[input.Sources.Count]; // old 0-based -> new 1-based, null = dropped
        var dropped = 0;
        var merged = 0;

        for (var i = 0; i < input.Sources.Count; i++)
        {
            var source = input.Sources[i];
            if (IsExcluded(source.Url, input.Policy))
            {
                dropped++;
                continue;
            }

            var url = input.Policy.StripTrackingParameters ? StripTracking(source.Url) : source.Url;
            if (input.Policy.StripTrackingParameters && byUrl.TryGetValue(url, out var existing))
            {
                newIndex[i] = existing;
                merged++;
                continue;
            }

            kept.Add(url == source.Url ? source : source with { Url = url });
            byUrl[url] = kept.Count;
            newIndex[i] = kept.Count;
        }

        var summary = Renumber(input.SummaryMarkdown, newIndex);
        return new WebSourceNormalizerOutput(summary, kept, dropped, merged);
    }

    /// <summary>Whether the deny-list or the tier policy removes a source of this URL.</summary>
    public static bool IsExcluded(string url, WebSourcePolicy policy)
    {
        var host = HostOf(url);
        if (host is null)
        {
            return false; // not a URL: WebGuard rejects it with its own reason
        }

        if (policy.DeniedDomains.Any(d => MatchesDomain(host, d)))
        {
            return true;
        }

        return policy.MaxSourceTier > 0 && TierOf(host, policy) > policy.MaxSourceTier;
    }

    /// <summary>The tier of a host: the best tier among the listed domains it belongs to.</summary>
    public static int TierOf(string host, WebSourcePolicy policy)
    {
        var best = WebSourcePolicy.UnlistedTier;
        var found = false;
        foreach (var (domain, tier) in policy.DomainTiers)
        {
            if (MatchesDomain(host, domain) && (!found || tier < best))
            {
                best = tier;
                found = true;
            }
        }

        return best;
    }

    private static bool MatchesDomain(string host, string domain)
    {
        var d = domain.Trim().TrimStart('.').ToLowerInvariant();
        return d.Length > 0 && (host == d || host.EndsWith("." + d, StringComparison.Ordinal));
    }

    private static string? HostOf(string url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : null;

    internal static string StripTracking(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Query))
        {
            return url;
        }

        var kept = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair =>
            {
                var name = Uri.UnescapeDataString(pair.Split('=', 2)[0]);
                return !TrackingNames.Contains(name)
                    && !TrackingPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            })
            .ToList();

        var builder = new UriBuilder(uri) { Query = string.Join('&', kept) };
        return builder.Uri.AbsoluteUri;
    }

    private static string Renumber(string markdown, int?[] newIndex) =>
        MarkerPattern().Replace(markdown, match =>
        {
            var old = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (old < 1 || old > newIndex.Length)
            {
                return match.Value; // out of range: WebGuard reports it
            }

            return newIndex[old - 1] is { } mapped
                ? $"{match.Groups[1].Value}[{mapped}]"
                : string.Empty;
        });

    [GeneratedRegex(@"(\s*)\[(\d+)\]")]
    private static partial Regex MarkerPattern();
}
