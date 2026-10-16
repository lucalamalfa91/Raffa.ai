using Raffa.JevBenchmark.Tests.Harness;
using Raffa.JevBenchmark.Tests.Reporting;
using Xunit.Abstractions;

namespace Raffa.JevBenchmark.Tests.Live;

/// <summary>
/// THE LIVE BENCHMARK. Skipped (with a reason) unless <c>RAFFA_JEV_BENCH=1</c> and both
/// <c>AiGateway__Endpoint</c> (Foundry) and <c>AiGateway__Jev__ApiKey</c> are in the environment, so a
/// normal <c>dotnet test</c> and CI never reach this code path and never call out. Run it on purpose:
/// <c>dotnet test --filter Category=JevBenchmark</c> (see docs/jev-benchmark.md).
///
/// <para>It measures, it does not gate: no accuracy or latency threshold is asserted, because a number
/// that moves with a vendor's model is not a build verdict. It fails only if the harness itself cannot
/// produce its result files.</para>
/// </summary>
[Trait("Category", "JevBenchmark")]
public sealed class JevBenchmarkLiveTests(ITestOutputHelper output)
{
    [JevBenchFact]
    public async Task Run_before_after_comparison_and_write_the_report()
    {
        var environment = LiveBenchmarkFactory.ReadProcessEnvironment();
        var settings = BenchSettings.FromEnvironment(name => environment.GetValueOrDefault(name));
        var deps = LiveBenchmarkFactory.Create(environment, settings);

        void Log(string line)
        {
            output.WriteLine(line);
            Console.WriteLine(line);
        }

        var result = await BenchmarkOrchestrator.RunAsync(deps, Log, CancellationToken.None);
        var (jsonPath, markdownPath) = ReportWriter.Write(result, Path.GetFullPath(settings.OutputDirectory));

        var summary = ReportWriter.ToConsoleSummary(result);
        Log(summary);
        Log($"JSON : {jsonPath}");
        Log($"Report: {markdownPath}");

        Assert.True(File.Exists(jsonPath));
        Assert.True(File.Exists(markdownPath));
        Assert.NotEmpty(result.Tracks);
    }
}
