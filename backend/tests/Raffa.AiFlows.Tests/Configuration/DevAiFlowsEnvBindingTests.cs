using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Raffa.AiFlows.Ask.Interview;
using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.CapabilityGaps.Drafting;
using Raffa.AiFlows.CapabilityGaps.Investigation;
using Raffa.AiFlows.DocumentExtraction.Orchestration;
using Raffa.AiFlows.Negotiation.Options;
using Raffa.AiFlows.QuoteExtraction;
using Raffa.AiFlows.WebResearch.Configuration;
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
        (MarketRetrievalOptions.SectionName, typeof(MarketRetrievalOptions)),
    ];

    private static readonly Regex MapLine = new(
        "^\\s+(?<key>[A-Za-z0-9_]+)\\s*=\\s*\"(?<value>[^\"]*)\"\\s*$", RegexOptions.Compiled);

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
            var (_, property, defaults) = Resolve(key);
            if (property.PropertyType != typeof(bool) || !bool.Parse(value))
            {
                continue;
            }

            Assert.False(
                (bool)property.GetValue(defaults)!,
                $"{key}=true is already the code default; drop it from the dev map.");
        }
    }

    [Fact]
    public void Enum_and_numeric_entries_are_not_silently_the_code_default_unless_they_pin_it_on_purpose()
    {
        // The two pinned modes: Shadow is the intended "on" state of the verifiers (Gate stays off the
        // map), Triggered is the investigator's default mode written down so a code change cannot flip it.
        var pinned = new HashSet<string>(StringComparer.Ordinal)
        {
            "Documents__Extraction__SpanVerification",
            "Documents__Extraction__CrossStageValidation",
            "Chat__GapInvestigation__Mode",
        };

        foreach (var (key, value) in ReadDevMap())
        {
            var (_, property, defaults) = Resolve(key);
            if (property.PropertyType == typeof(bool) || pinned.Contains(key))
            {
                continue;
            }

            Assert.NotEqual(Parse(property.PropertyType, value), property.GetValue(defaults));
        }
    }

    [Fact]
    public void The_modes_pinned_on_purpose_are_the_non_enforcing_ones()
    {
        var map = ReadDevMap();

        Assert.Equal("Shadow", map["Documents__Extraction__SpanVerification"]);
        Assert.Equal("Shadow", map["Documents__Extraction__CrossStageValidation"]);
        Assert.Equal("Triggered", map["Chat__GapInvestigation__Mode"]);
    }

    [Fact]
    public void Switches_with_their_own_variable_or_a_missing_prerequisite_are_not_in_the_dev_map()
    {
        var keys = ReadDevMap().Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Own Terraform variables / Key Vault secrets.
        Assert.DoesNotContain("Chat__WebResearch__Enabled", keys);
        Assert.DoesNotContain("AiGateway__Jev__Enabled", keys);
        Assert.DoesNotContain("AiGateway__Jev__ApiKey", keys);
        Assert.DoesNotContain("AiGateway__QueryHash__Key", keys);

        // Need a golden set (F7-D08), a source policy, a provider quota or the real AI agents.
        Assert.DoesNotContain("Market__Retrieval__Hybrid", keys);
        Assert.DoesNotContain("Market__Retrieval__Diversify", keys);
        Assert.DoesNotContain("Chat__WebResearch__MaxSourceTier", keys);
        Assert.DoesNotContain("Chat__WebResearch__VendorMasterAsTier1", keys);
        Assert.DoesNotContain("Documents__Extraction__TokensPerMinuteLimit", keys);
        Assert.DoesNotContain("Market__Ingestion__Curation__Enabled", keys);

        // Market ingestion is not read by the Container Apps at all (see the workflow test below).
        Assert.DoesNotContain(keys, k => k.StartsWith("Market__Ingestion__", StringComparison.OrdinalIgnoreCase));

        // Gate is a decision after Shadow has been measured, not part of "enabled".
        Assert.DoesNotContain(ReadDevMap().Values, v => string.Equals(v, "Gate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_market_ingestion_flags_of_the_dev_seed_job_bind_to_the_ingestion_options()
    {
        var workflow = ReadWorkflowMap();

        Assert.Equal(
            ["Market__Ingestion__BlueGreen", "Market__Ingestion__CheckpointPerBatch", "Market__Ingestion__EmbedBatchSize"],
            workflow.Keys.Order(StringComparer.Ordinal).ToArray());
        AssertBinds(workflow);
    }

    // --------------------------------------------------------------------------------------------

    private static void AssertBinds(IReadOnlyDictionary<string, string> env)
    {
        // The environment-variable provider maps "__" to ":" and nothing else.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(env.ToDictionary(e => e.Key.Replace("__", ":", StringComparison.Ordinal), e => (string?)e.Value))
            .Build();

        var bound = new Dictionary<Type, object>();
        foreach (var (key, expected) in env)
        {
            var (target, property, _) = Resolve(key);
            if (!bound.TryGetValue(target.Options, out var options))
            {
                options = Activator.CreateInstance(target.Options)!;
                configuration.GetSection(target.Section).Bind(options);
                bound[target.Options] = options;
            }

            Assert.Equal(Parse(property.PropertyType, expected), property.GetValue(options));
        }
    }

    private static ((string Section, Type Options) Target, PropertyInfo Property, object Defaults) Resolve(string envKey)
    {
        var path = envKey.Replace("__", ":", StringComparison.Ordinal);
        var target = Targets
            .OrderByDescending(t => t.Section.Length)
            .FirstOrDefault(t => path.StartsWith(t.Section + ":", StringComparison.OrdinalIgnoreCase));
        Assert.True(target.Options is not null, $"{envKey}: no options section matches (typo in the section name?).");

        var name = path[(target.Section.Length + 1)..];
        var property = target.Options.GetProperty(
            name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        Assert.True(
            property is not null && property.CanWrite && name.Equals(property.Name, StringComparison.OrdinalIgnoreCase),
            $"{envKey}: {target.Options.Name} has no settable property '{name}' (typo in the key?).");

        return (target, property!, Activator.CreateInstance(target.Options)!);
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
