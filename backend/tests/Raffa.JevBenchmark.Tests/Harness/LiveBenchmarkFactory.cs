using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Raffa.AiFlows.CapabilityGaps.Investigation;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Foundry;
using Raffa.JevBenchmark.Tests.Reporting;

namespace Raffa.JevBenchmark.Tests.Harness;

/// <summary>
/// Builds the real dependencies from the environment, exactly the way the deployed host binds them:
/// the same <c>AiGateway__*</c> variables, the same <c>AddAiGatewayModule</c> registrations, the real
/// <see cref="FoundryAiGateway"/>. Constructing them performs no network I/O (credentials are acquired
/// lazily on the first Foundry call), so this is safe to unit test with fake variables.
/// </summary>
public static class LiveBenchmarkFactory
{
    public static BenchDependencies Create(IReadOnlyDictionary<string, string> environment, BenchSettings settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(ToConfigurationKeys(environment))
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddAiGatewayModule();
        // Deliberately not disposed: the Foundry gateway keeps the provider's singletons (HttpClient,
        // credential) for the life of the process.
        var provider = services.BuildServiceProvider();

        var foundryOptions = provider.GetRequiredService<AiGatewayFoundryOptions>();
        var models = provider.GetRequiredService<AiGatewayModelOptions>();
        var jevOptions = provider.GetRequiredService<AiGatewayJevOptions>();
        var resilience = provider.GetRequiredService<AiGatewayResilienceOptions>();
        // Resolved from the root provider (a singleton chain); the logging decorator the API adds
        // on top is deliberately absent: it needs the audit store and is not part of what is compared.
        var foundry = provider.GetRequiredService<FoundryAiGateway>();

        var gapOptions = new GapInvestigationOptions();
        configuration.GetSection(GapInvestigationOptions.SectionName).Bind(gapOptions);
        gapOptions.Enabled = true;
        gapOptions.ContextTurns = 0;
        gapOptions.TimeoutSeconds = settings.GapTimeoutSeconds;

        var jevHost = Uri.TryCreate(foundryOptions.Endpoint, UriKind.Absolute, out var uri) ? uri.Host : "(unset)";
        string? Env(string name) => environment.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
        var runUrl = Env("GITHUB_SERVER_URL") is { } server && Env("GITHUB_REPOSITORY") is { } repo && Env("GITHUB_RUN_ID") is { } run
            ? $"{server}/{repo}/actions/runs/{run}"
            : null;

        var config = new BenchConfigSnapshot(
            settings,
            jevHost,
            models.Classify.ModelId,
            models.Classify.ModelVersion,
            (models.Analyst ?? models.Answer).ModelId,
            jevOptions.Model,
            jevOptions.Endpoint,
            !string.IsNullOrWhiteSpace(jevOptions.ApiKey),
            jevOptions.ClassifyMinConfidence,
            jevOptions.CheckOptionOrder,
            jevOptions.ClassifyMaxInputChars,
            gapOptions.JevHighConfidenceThreshold,
            gapOptions.JevMediumConfidenceThreshold,
            gapOptions.MinConfidence,
            resilience.MaxRetries,
            resilience.RequestTimeoutSeconds,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            Env("RAFFA_JEV_BENCH_GIT_SHA") ?? Env("GITHUB_SHA"),
            runUrl,
            "AiGateway:Jev:Enabled=false. The classify role is served by Foundry alone; the investigator decides the verdict with the Foundry `analyst` call alone.",
            "AiGateway:Jev:Enabled=true. The classify role is served by Jev, falling back to Foundry on a Jev error or below ClassifyMinConfidence; " +
            "the investigator takes its verdict from Jev and only calls Foundry to write the description of a gap.");

        return new BenchDependencies(
            settings,
            foundry,
            jevOptions,
            gapOptions,
            resilience,
            new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5), AutomaticDecompression = System.Net.DecompressionMethods.All },
            config,
            new StopwatchBenchClock(),
            track => track == BenchTracks.Classify ? CaseSets.LoadClassify() : CaseSets.LoadGaps());
    }

    /// <summary>The whole process environment as a dictionary (later duplicates win, names are case-sensitive on Linux).</summary>
    public static IReadOnlyDictionary<string, string> ReadProcessEnvironment()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>Only the variables that configure the host (<c>AiGateway__*</c>, <c>Chat__GapInvestigation__*</c>), as <c>:</c>-separated configuration keys.</summary>
    internal static IEnumerable<KeyValuePair<string, string?>> ToConfigurationKeys(IReadOnlyDictionary<string, string> environment) =>
        environment
            .Where(pair => pair.Key.StartsWith("AiGateway__", StringComparison.Ordinal) || pair.Key.StartsWith("Chat__GapInvestigation__", StringComparison.Ordinal))
            .Select(pair => new KeyValuePair<string, string?>(pair.Key.Replace("__", ":", StringComparison.Ordinal), pair.Value));
}
