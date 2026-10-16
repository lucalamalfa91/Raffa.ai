using System.Net;
using System.Text.Json;
using Raffa.AiFlows.CapabilityGaps.Investigation;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Foundry;
using Raffa.JevBenchmark.Tests.Harness;
using Raffa.JevBenchmark.Tests.Reporting;
using static Raffa.JevBenchmark.Tests.Tests.Samples;

namespace Raffa.JevBenchmark.Tests.Tests;

public sealed class RunnerAndReportTests
{
    private sealed class ScriptedArm(string name, bool jev, List<string> log) : IBenchArm
    {
        public string Name { get; } = name;

        public string Track => BenchTracks.Classify;

        public bool JevEnabled { get; } = jev;

        public Task<CaseObservation> RunAsync(LabelledCase labelledCase, int rep, CancellationToken cancellationToken)
        {
            log.Add($"{Name}:{labelledCase.Id}:{rep}");
            return Task.FromResult(Obs(Name, labelledCase.Id, rep, labelledCase.ExpectedLabel, labelledCase.ExpectedLabel));
        }
    }

    private static CaseSet TwoCases() => new(
        BenchTracks.Classify, "t", "1", "unit test", "x.json", Enum.GetNames<AiDocumentType>(),
        [ClassifyCase("c0"), ClassifyCase("c1")]);

    [Fact]
    public async Task Runner_discards_warm_up_repeats_every_case_and_alternates_who_goes_first()
    {
        var log = new List<string>();
        var runner = new BenchmarkRunner(new BenchSettings { Repetitions = 2, WarmupCalls = 1 });

        var observations = await runner.RunTrackAsync(
            TwoCases(), new ArmPair(new ScriptedArm(BenchArms.Baseline, false, log), new ScriptedArm(BenchArms.WithJev, true, log)), null, default);

        // Warm-up (rep -1) happened but is not in the result.
        Assert.Equal([$"{BenchArms.Baseline}:c0:-1", $"{BenchArms.WithJev}:c0:-1"], log.Take(2));
        Assert.DoesNotContain(observations, o => o.Rep < 0);
        Assert.Equal(2 * 2 * 2, observations.Count);
        Assert.Equal(4, observations.Count(o => o.Arm == BenchArms.Baseline));

        // Order of the measured calls: (rep+index) even => baseline first.
        var measured = log.Skip(2).ToList();
        Assert.Equal(
            [
                $"{BenchArms.Baseline}:c0:0", $"{BenchArms.WithJev}:c0:0",
                $"{BenchArms.WithJev}:c1:0", $"{BenchArms.Baseline}:c1:0",
                $"{BenchArms.WithJev}:c0:1", $"{BenchArms.Baseline}:c0:1",
                $"{BenchArms.Baseline}:c1:1", $"{BenchArms.WithJev}:c1:1",
            ],
            measured);
    }

    [Fact]
    public async Task Runner_pauses_between_calls_when_asked_and_not_otherwise()
    {
        var pauses = 0;
        Task Delay(TimeSpan _, CancellationToken __)
        {
            pauses++;
            return Task.CompletedTask;
        }

        var arms = new ArmPair(new ScriptedArm("A", false, []), new ScriptedArm("B", true, []));
        await new BenchmarkRunner(new BenchSettings { Repetitions = 1, WarmupCalls = 0, DelayBetweenCallsMs = 0 }, Delay).RunTrackAsync(TwoCases(), arms, null, default);
        Assert.Equal(0, pauses);

        await new BenchmarkRunner(new BenchSettings { Repetitions = 1, WarmupCalls = 0, DelayBetweenCallsMs = 250 }, Delay).RunTrackAsync(TwoCases(), arms, null, default);
        Assert.Equal(4, pauses);
    }

    [Fact]
    public async Task The_whole_run_with_fakes_writes_json_and_markdown_and_never_leaks_the_key()
    {
        var clock = new FakeBenchClock();
        var foundry = new FakeFoundryGateway(
            clock, 100, text => (AiDocumentType.Quote, 0.9), analyzePayload: GapPayload());
        var transport = new FakeJevTransport(clock, 30, HttpStatusCode.OK, FakeJevTransport.ClassifyBody("Invoice", 0.95));
        var settings = new BenchSettings { Repetitions = 2, WarmupCalls = 1, BootstrapIterations = 200, Tracks = [BenchTracks.Classify] };
        var jevOptions = new AiGatewayJevOptions { ApiKey = "SUPER-SECRET-KEY" };
        var config = ConfigSnapshot(settings, jevOptions);

        var deps = new BenchDependencies(
            settings, foundry, jevOptions, new GapInvestigationOptions(), new AiGatewayResilienceOptions { MaxRetries = 0 },
            transport, config, clock,
            _ => new CaseSet(
                BenchTracks.Classify, "tiny", "9", "unit test", "tiny.json", Enum.GetNames<AiDocumentType>(),
                [ClassifyCase("c0"), ClassifyCase("c1", "Quote")]),
            (_, _) => Task.CompletedTask);

        var result = await BenchmarkOrchestrator.RunAsync(deps, null, default);

        var track = Assert.Single(result.Tracks);
        Assert.Equal(2 * 2 * 2, result.Observations.Count);
        Assert.Equal(0.5, track.Baseline.StrictAccuracy, 9); // Foundry says Quote: right on c1 only
        Assert.Equal(0.5, track.WithJev.StrictAccuracy, 9);  // Jev says Invoice: right on c0 only
        Assert.Equal(0.0, track.Comparison.Agreement, 9);

        var directory = Path.Combine(Path.GetTempPath(), "jev-bench-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (jsonPath, markdownPath) = ReportWriter.Write(result, directory);
            var json = await File.ReadAllTextAsync(jsonPath);
            var markdown = await File.ReadAllTextAsync(markdownPath);

            using var parsed = JsonDocument.Parse(json);
            Assert.Equal(1, parsed.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("tiny", parsed.RootElement.GetProperty("tracks")[0].GetProperty("dataset").GetProperty("name").GetString());
            Assert.True(parsed.RootElement.GetProperty("config").GetProperty("jevApiKeyConfigured").GetBoolean());
            Assert.DoesNotContain("SUPER-SECRET-KEY", json, StringComparison.Ordinal);
            Assert.DoesNotContain("SUPER-SECRET-KEY", markdown, StringComparison.Ordinal);

            Assert.Contains("# Benchmark Jev", markdown, StringComparison.Ordinal);
            Assert.Contains("### Verdetto", markdown, StringComparison.Ordinal);
            Assert.Contains("### Confronto affiancato", markdown, StringComparison.Ordinal);
            Assert.Contains("McNemar", markdown, StringComparison.Ordinal);
            Assert.Contains("Matrice di confusione", markdown, StringComparison.Ordinal);
            Assert.Contains("## Configurazione usata", markdown, StringComparison.Ordinal);
            Assert.Contains("typesafe/jev-1.13", markdown, StringComparison.Ordinal);
            Assert.Contains(track.Dataset.ContentHash, markdown, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Empty_results_serialize_without_throwing_on_not_a_number_values()
    {
        var set = new CaseSet(BenchTracks.Gaps, "t", "1", "p", "x.json", [CaseSets.GapLabel, CaseSets.NoGapLabel], []);
        var track = TrackAnalyzer.Analyze(set, [], new BenchSettings { BootstrapIterations = 100 });
        var settings = new BenchSettings();
        var result = new BenchmarkResult(1, "now", 0, ConfigSnapshot(settings, new AiGatewayJevOptions()), [track], []);

        var json = ReportWriter.ToJson(result);
        var markdown = ReportWriter.ToMarkdown(result);

        Assert.Contains("NaN", json, StringComparison.Ordinal);
        Assert.Contains("n/a", markdown, StringComparison.Ordinal);
        Assert.Contains("insufficient-data", markdown, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(ReportWriter.ToConsoleSummary(result)));
    }

    [Fact]
    public void The_real_dependency_graph_builds_from_environment_variables_without_any_network_call()
    {
        var environment = new Dictionary<string, string>
        {
            ["AiGateway__Endpoint"] = "https://foundry.example.invalid/openai",
            ["AiGateway__Models__Classify__ModelId"] = "my-classify-deployment",
            ["AiGateway__Models__Classify__ModelVersion"] = "2026-01-01",
            ["AiGateway__Jev__ApiKey"] = "ANOTHER-SECRET",
            ["AiGateway__Jev__Model"] = "typesafe/jev-pinned",
            ["AiGateway__Jev__ClassifyMinConfidence"] = "0.7",
            ["Chat__GapInvestigation__MinConfidence"] = "high",
            ["UNRELATED_VARIABLE"] = "ignored",
            ["GITHUB_SERVER_URL"] = "https://github.com",
            ["GITHUB_REPOSITORY"] = "owner/repo",
            ["GITHUB_RUN_ID"] = "123",
            ["GITHUB_SHA"] = "abc123",
        };

        var deps = LiveBenchmarkFactory.Create(environment, new BenchSettings { GapTimeoutSeconds = 45 });

        Assert.IsType<FoundryAiGateway>(deps.Foundry);
        Assert.Equal("foundry.example.invalid", deps.Config.FoundryEndpointHost);
        Assert.Equal("my-classify-deployment", deps.Config.ClassifyModelId);
        Assert.Equal("typesafe/jev-pinned", deps.Config.JevModel);
        Assert.Equal(0.7, deps.Config.JevClassifyMinConfidence);
        Assert.True(deps.Config.JevApiKeyConfigured);
        Assert.Equal("high", deps.Config.GapMinConfidence);
        Assert.Equal(45, deps.GapOptions.TimeoutSeconds);
        Assert.Equal("abc123", deps.Config.GitSha);
        Assert.Equal("https://github.com/owner/repo/actions/runs/123", deps.Config.RunUrl);
        Assert.DoesNotContain("ANOTHER-SECRET", JsonSerializer.Serialize(deps.Config), StringComparison.Ordinal);
    }

    [Fact]
    public void Only_gateway_and_investigator_variables_reach_the_configuration()
    {
        var keys = LiveBenchmarkFactory.ToConfigurationKeys(new Dictionary<string, string>
        {
            ["AiGateway__Jev__Enabled"] = "true",
            ["Chat__GapInvestigation__Enabled"] = "false",
            ["PATH"] = "/usr/bin",
            ["RAFFA_JEV_BENCH"] = "1",
        }).Select(p => p.Key).Order().ToList();

        Assert.Equal(["AiGateway:Jev:Enabled", "Chat:GapInvestigation:Enabled"], keys);
    }

    private static BenchConfigSnapshot ConfigSnapshot(BenchSettings settings, AiGatewayJevOptions jev) =>
        new(
            settings, "foundry.example.invalid", "gpt-test", "v1", "gpt-analyst", jev.Model, jev.Endpoint,
            !string.IsNullOrWhiteSpace(jev.ApiKey), jev.ClassifyMinConfidence, jev.CheckOptionOrder, jev.ClassifyMaxInputChars,
            0.85, 0.6, "medium", 0, 5, ".NET test", "test-os", "sha", null, "A description", "B description");
}
