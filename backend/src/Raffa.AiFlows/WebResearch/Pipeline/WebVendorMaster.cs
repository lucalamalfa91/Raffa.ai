using System.Text.RegularExpressions;

namespace Raffa.AiFlows.WebResearch.Pipeline;

/// <summary>One vendor of the master: a display name, the lower-case names a question uses for it, and
/// the domains it publishes its own pricing, terms and announcements on.</summary>
public sealed record WebVendor(string Name, IReadOnlyList<string> Aliases, IReadOnlyList<string> OfficialDomains);

/// <summary>
/// F3-D12 — the initial vendor master of the web research: the software vendors a procurement team
/// meets most, with their aliases and official domains. Static reference data, no I/O. It feeds the
/// source policy (<see cref="Configuration.WebResearchOptions.VendorMasterAsTier1"/>: a vendor's own site is
/// tier 1 for news about that vendor) and answers "which vendor does this text mean"
/// (<see cref="FindByText"/>), the lookup the supplier-event taxonomy builds on. The list is a
/// starting point of well-known public sites, not an allow-list: a source outside it is never
/// dropped by the master, only ranked by the tiers the options configure.
/// </summary>
public static class WebVendorMaster
{
    public static IReadOnlyList<WebVendor> All { get; } =
    [
        new("Salesforce", ["salesforce", "sfdc"], ["salesforce.com"]),
        new("Microsoft", ["microsoft", "msft", "microsoft 365", "office 365", "azure", "dynamics 365"], ["microsoft.com"]),
        new("Oracle", ["oracle"], ["oracle.com"]),
        new("SAP", ["sap", "sap s/4hana", "successfactors"], ["sap.com"]),
        new("ServiceNow", ["servicenow"], ["servicenow.com"]),
        new("Adobe", ["adobe"], ["adobe.com"]),
        new("Atlassian", ["atlassian", "jira", "confluence"], ["atlassian.com"]),
        new("Workday", ["workday"], ["workday.com"]),
        new("Amazon Web Services", ["aws", "amazon web services"], ["aws.amazon.com"]),
        new("Google Cloud", ["google cloud", "gcp", "google workspace"], ["cloud.google.com", "workspace.google.com"]),
        new("IBM", ["ibm"], ["ibm.com"]),
        new("Snowflake", ["snowflake"], ["snowflake.com"]),
        new("Databricks", ["databricks"], ["databricks.com"]),
        new("Zoom", ["zoom"], ["zoom.us", "zoom.com"]),
        new("HubSpot", ["hubspot"], ["hubspot.com"]),
        new("Slack", ["slack"], ["slack.com"]),
    ];

    /// <summary>The domain-to-tier map the source policy reads: every official domain at tier 1, except
    /// where <paramref name="configured"/> already ranks that domain (the configuration wins).</summary>
    public static IReadOnlyDictionary<string, int> WithOfficialDomains(IReadOnlyDictionary<string, int> configured)
    {
        ArgumentNullException.ThrowIfNull(configured);

        var tiers = new Dictionary<string, int>(configured, StringComparer.OrdinalIgnoreCase);
        foreach (var domain in All.SelectMany(v => v.OfficialDomains))
        {
            tiers.TryAdd(domain, 1);
        }

        return tiers;
    }

    /// <summary>The vendors <paramref name="text"/> names, in master order, each at most once. An alias
    /// matches as a whole word (so "sap" is not found in "sapere"), case-insensitively.</summary>
    public static IReadOnlyList<WebVendor> FindByText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return All
            .Where(vendor => vendor.Aliases.Any(alias =>
                Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(alias)}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            .ToList();
    }
}
