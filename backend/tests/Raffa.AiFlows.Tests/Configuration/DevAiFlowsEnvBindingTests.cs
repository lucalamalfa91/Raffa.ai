using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Raffa.AiFlows.Ask.Interview;
using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.CapabilityGaps.Drafting;
using Raffa.AiFlows.CapabilityGaps.Investigation;
using Raffa.AiFlows.DocumentExtraction.Orchestration;
using Raffa.AiFlows.MarketKnowledge.Ingestion;
using Raffa.AiFlows.Negotiation.Options;
using Raffa.AiFlows.QuoteExtraction;
using Raffa.AiFlows.WebResearch.Configuration;
using Raffa.AiFlows.WebResearch.Pipeline;
using Raffa.Market.Ingestion;
using Raffa.Market.Retrieval;

namespace Raffa.AiFlows.Tests.Configuration;

/// <summary>
/// Guards the dev environment's AI-flow switches against key typos. The dev Terraform root publishes
/// <c>variable "ai_flows_env"</c> (infra/environments/dev/variables.tf) as environment variables on both
/// Container Apps, and <c>.github/workflows/seed-market-intelligence.yml</c> sets the Market ingestion
/// flags for the one-shot <c>ingest-market</c> job. ASP.NET Core binds a key it does not recognise
/// silently, so a misspelt flag would simply stay off. These tests read the real files, turn each
/// <c>__</c> key into the configuration path the environment-variable provider would produce, bind it
/// through the options class the host binds (<c>GetSection(SectionName).Bind(options)</c>) and assert the
/// value landed. No model call, no network, no Terraform binary.
/// </summary>
public sealed class DevAiFlowsEnvBindingTests
{
    // Longest section first: "Market:Ingestion" must not swallow a "Market:Ingestion:Curation" key.
    private static readonly (string Section, Type Options)[] Targets =
    [
        (ExtractionOrchestrationOptions.SectionName, typeof(ExtractionOrchestrationOptions)),
        (QuoteExtractionOptions.SectionName, typeof(QuoteExtractionOptions)),
        (AskTurnOptions.SectionName, typeof(AskTurnOptions)),
        (GapInvestigationOptions.SectionName, typeof(GapInvestigationOptions)),
        (CouncilOptions.SectionName, typeof(CouncilOptions)),
        (DraftingOptions.SectionName, typeof(DraftingOptions)),
        (InterviewOptions.SectionName, typeof(InterviewOptions)),
        (WebResearchOptions.SectionName, typeof(WebResearchOptions)),
        (MarketIngestionOptions.SectionName, typeof(MarketIngestionOptions)),
        (MarketCurationOptions.SectionName, typeof(MarketCurationOptions)),
        (MarketRetrievalOptions.SectionName, typeof(MarketRetrievalOptions)),
    ];

    private static readonly Regex MapLine = new(
        "^\\s+\"?(?<key>[A-Za-z0-9_.\\-]+)\"?\\s*=\\s*\"(?<value>[^\"]*)\"\\s*$", RegexOptions.Compiled);

    private static readonly Regex WorkflowLine = new(
        "^\\s+(?<key>Market__Ingestion__\\w+):\\s*\\$\\{\\{\\s*inputs\\.target_environment == 'dev' && '(?<value>[^']*)' \\|\\| '[^']*'\\s*\\}\\}\\s*$",
        RegexOptions.Compiled);

    [Fact]
    public void The_dev_map_is_not_empty()
    {
        Assert.NotEmpty(ReadDevMap());
    }

    [Fact]
    public void Every_dev_key_names_a_real_option_and_binds_to_the_value_the_map_carries()
    {
        AssertBinds(ReadDevMap());
    }

    [Fact]
    public void Every_enabled_boolean_is_off_in_the_code_so_the_entry_is_not_redundant()
    {
        foreach (var (key, value) in ReadDevMap().Concat(ReadWorkflowMap()))
        {
            var r = Resolve(key);
            if (r.Property.PropertyType != typeof(bool) || !bool.Parse(value))
            {
                continue;
            }

            Assert.False(
                (bool)r.Property.GetValue(r.Defaults)!,
                $"{key}=true is already the code default; drop it from the dev map.");
        }
    }

    [Fact]
    public void Numeric_and_enum_entries_differ_from_the_code_default_unless_pinned_on_purpose()
    {
        // Triggered is the investigator's default mode written down so a code change cannot flip it.
        var pinned = new HashSet<string>(StringComparer.Ordinal) { "Chat__GapInvestigation__Mode" };

        foreach (var (key, value) in ReadDevMap().Concat(ReadWorkflowMap()))
        {
            var r = Resolve(key);
            if (r.Property.PropertyType == typeof(bool) || r.DictionaryKey is not null || pinned.Contains(key))
            {
                continue;
            }

            Assert.NotEqual(Parse(r.Property.PropertyType, value), r.Property.GetValue(r.Defaults));
        }
    }

    [Fact]
    public void The_verifiers_are_in_gate_mode_and_the_investigator_is_pinned_to_triggered()
    {
        var map = ReadDevMap();

        Assert.Equal("Gate", map["Documents__Extraction__SpanVerification"]);
        Assert.Equal("Gate", map["Documents__Extraction__CrossStageValidation"]);
        Assert.Equal("Triggered", map["Chat__GapInvestigation__Mode"]);
    }

    [Fact]
    public void The_per_process_token_limit_is_the_dev_chat_quota_split_across_the_worker_replicas()
    {
        // 300K TPM (gpt-5.4-nano, infra/environments/dev/main.tf) / 5 replicas (worker_max_replicas).
        Assert.Equal(300_000 / 5, int.Parse(ReadDevMap()["Documents__Extraction__TokensPerMinuteLimit"], CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Switches_with_their_own_variable_or_secret_or_left_off_by_decision_are_not_in_the_dev_map()
    {
        var keys = ReadDevMap().Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Own Terraform variables / Key Vault secrets (query-hash-key, jev-api-key).
        Assert.DoesNotContain("Chat__WebResearch__Enabled", keys);
        Assert.DoesNotContain("AiGateway__QueryHash__Key", keys);

        // Left off: no real key (Jev), decision D5 = no visible label, no invented deny-list.
        Assert.DoesNotContain(keys, k => k.StartsWith("AiGateway__Jev__", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(keys, k => k.StartsWith("Market__Provenance__", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(keys, k => k.StartsWith("Chat__WebResearch__DeniedDomains", StringComparison.OrdinalIgnoreCase));

        // Market ingestion is not read by the Container Apps at all (see the workflow test below).
        Assert.DoesNotContain(keys, k => k.StartsWith("Market__Ingestion__", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_market_retrieval_and_web_policy_flags_are_on()
    {
        var map = ReadDevMap();

        Assert.Equal("true", map["Market__Retrieval__Hybrid"]);
        Assert.Equal("true", map["Market__Retrieval__Diversify"]);
        Assert.Equal("true", map["Chat__WebResearch__VendorMasterAsTier1"]);
        Assert.Equal("3", map["Chat__WebResearch__MaxSourceTier"]);
    }

    [Fact]
    public void The_domain_tiers_come_from_the_vendor_master_and_skip_tenant_hosted_domains()
    {
        const string prefix = "Chat__WebResearch__DomainTiers__";
        var mapped = ReadDevMap().Where(e => e.Key.StartsWith(prefix, StringComparison.Ordinal))
            .ToDictionary(e => e.Key[prefix.Length..], e => e.Value, StringComparer.OrdinalIgnoreCase);
        Assert.All(mapped.Values, v => Assert.Equal("1", v));

        using var json = JsonDocument.Parse(File.ReadAllText(RepoFile("backend/tests/Raffa.AiEval/golden/pending/vendor-master.v1.json")));
        var draft = json.RootElement.GetProperty("vendors").EnumerateArray()
            .SelectMany(v => v.GetProperty("officialDomains").EnumerateArray().Select(d => d.GetString()!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tenantHosted = new[] { "atlassian.net", "amazonaws.com", "google.com" };

        Assert.Equal(
            draft.Except(tenantHosted, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal),
            mapped.Keys.Order(StringComparer.Ordinal));

        // The code's own master (VendorMasterAsTier1) adds ibm, snowflake, databricks, ... at tier 1; where
        // both name a domain they must agree, because the configuration wins over the master.
        foreach (var domain in WebVendorMaster.All.SelectMany(v => v.OfficialDomains))
        {
            Assert.True(!mapped.TryGetValue(domain, out var tier) || tier == "1", $"{domain} must be tier 1");
        }
    }

    [Fact]
    public void The_market_ingestion_flags_of_the_dev_seed_job_bind_to_the_ingestion_options()
    {
        var workflow = ReadWorkflowMap();

        Assert.Equal(
            [
                "Market__Ingestion__BlueGreen",
                "Market__Ingestion__CheckpointPerBatch",
                "Market__Ingestion__Curation__Enabled",
                "Market__Ingestion__EmbedBatchSize",
                "Market__Ingestion__EmbedTokensPerMinute",
            ],
            workflow.Keys.Order(StringComparer.Ordinal).ToArray());
        AssertBinds(workflow);
    }

    // --------------------------------------------------------------------------------------------

    private sealed record Resolved(
        (string Section, Type Options) Target, PropertyInfo Property, object Defaults, string? DictionaryKey);

    private static void AssertBinds(IReadOnlyDictionary<string, string> env)
    {
        // The environment-variable provider maps "__" to ":" and nothing else (dots stay in the key).
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(env.ToDictionary(e => e.Key.Replace("__", ":", StringComparison.Ordinal), e => (string?)e.Value))
            .Build();

        var bound = new Dictionary<Type, object>();
        foreach (var (key, expected) in env)
        {
            var r = Resolve(key);
            if (!bound.TryGetValue(r.Target.Options, out var options))
            {
                options = Activator.CreateInstance(r.Target.Options)!;
                configuration.GetSection(r.Target.Section).Bind(options);
                bound[r.Target.Options] = options;
            }

            var actual = r.Property.GetValue(options);
            if (r.DictionaryKey is null)
            {
                Assert.Equal(Parse(r.Property.PropertyType, expected), actual);
            }
            else
            {
                var tiers = Assert.IsType<Dictionary<string, int>>(actual);
                Assert.Equal(int.Parse(expected, CultureInfo.InvariantCulture), tiers[r.DictionaryKey]);
            }
        }
    }

    private static Resolved Resolve(string envKey)
    {
        var path = envKey.Replace("__", ":", StringComparison.Ordinal);
        var target = Targets
            .OrderByDescending(t => t.Section.Length)
            .FirstOrDefault(t => path.StartsWith(t.Section + ":", StringComparison.OrdinalIgnoreCase));
        Assert.True(target.Options is not null, $"{envKey}: no options section matches (typo in the section name?).");

        var name = path[(target.Section.Length + 1)..];
        var separator = name.IndexOf(':', StringComparison.Ordinal);
        var propertyName = separator < 0 ? name : name[..separator];
        var dictionaryKey = separator < 0 ? null : name[(separator + 1)..];

        var property = target.Options.GetProperty(
            propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        Assert.True(
            property is not null && property.CanWrite && propertyName.Equals(property.Name, StringComparison.OrdinalIgnoreCase),
            $"{envKey}: {target.Options.Name} has no settable property '{propertyName}' (typo in the key?).");
        Assert.True(
            dictionaryKey is null || property!.PropertyType == typeof(Dictionary<string, int>),
            $"{envKey}: '{propertyName}' is not a dictionary, so it cannot take a sub-key.");

        return new Resolved(target, property!, Activator.CreateInstance(target.Options)!, dictionaryKey);
    }

    private static object? Parse(Type type, string text)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying.IsEnum)
        {
            return Enum.Parse(underlying, text, ignoreCase: true);
        }

        if (underlying == typeof(TimeSpan))
        {
            return TimeSpan.Parse(text, CultureInfo.InvariantCulture);
        }

        return Convert.ChangeType(text, underlying, CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, string> ReadDevMap()
    {
        var lines = File.ReadAllLines(RepoFile("infra/environments/dev/variables.tf"));
        var start = Array.FindIndex(lines, l => l.Contains("variable \"ai_flows_env\"", StringComparison.Ordinal));
        Assert.True(start >= 0, "variable \"ai_flows_env\" not found in infra/environments/dev/variables.tf");
        var open = Array.FindIndex(lines, start, l => l.TrimEnd() == "  default = {");
        Assert.True(open >= 0, "the default map of ai_flows_env was not found");

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = open + 1; i < lines.Length && lines[i].TrimEnd() != "  }"; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var match = MapLine.Match(line);
            Assert.True(match.Success, $"unreadable line in ai_flows_env (one `Key = \"value\"` per line): {line}");
            Assert.True(map.TryAdd(match.Groups["key"].Value, match.Groups["value"].Value), $"duplicate key: {line}");
        }

        return map;
    }

    private static Dictionary<string, string> ReadWorkflowMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(RepoFile(".github/workflows/seed-market-intelligence.yml")))
        {
            var match = WorkflowLine.Match(line);
            if (match.Success)
            {
                map[match.Groups["key"].Value] = match.Groups["value"].Value;
            }
        }

        return map;
    }

    private static string RepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"{relative} not found above {AppContext.BaseDirectory}.");
    }
}
