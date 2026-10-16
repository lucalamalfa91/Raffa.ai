using Raffa.AiFlows.CapabilityGaps.Investigation;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.JevBenchmark.Tests.Reporting;

namespace Raffa.JevBenchmark.Tests.Harness;

/// <summary>Everything a run needs from the outside world, so a unit test can hand in fakes for all of it.</summary>
public sealed record BenchDependencies(
    BenchSettings Settings,
    IAiGateway Foundry,
    AiGatewayJevOptions JevOptions,
    GapInvestigationOptions GapOptions,
    AiGatewayResilienceOptions Resilience,
    HttpMessageHandler JevTransport,
    BenchConfigSnapshot Config,
    IBenchClock Clock,
    Func<string, CaseSet> LoadCaseSet,
    Func<TimeSpan, CancellationToken, Task>? Delay = null);

public static class BenchmarkOrchestrator
{
    public static async Task<BenchmarkResult> RunAsync(BenchDependencies deps, Action<string>? log, CancellationToken cancellationToken)
    {
        var settings = deps.Settings;
        var started = DateTimeOffset.UtcNow;
        var runner = new BenchmarkRunner(settings, deps.Delay);
        var tracks = new List<TrackResult>();
        var observations = new List<CaseObservation>();

        foreach (var track in settings.Tracks)
        {
            var caseSet = deps.LoadCaseSet(track).Take(settings.MaxCasesPerTrack);
            var arms = track == BenchTracks.Classify
                ? ArmWiring.CreateClassifyPair(deps.Foundry, deps.JevOptions, deps.JevTransport, deps.Resilience, deps.Clock, deps.Delay)
                : ArmWiring.CreateGapPair(deps.Foundry, deps.JevOptions, deps.GapOptions, deps.JevTransport, deps.Resilience, deps.Clock, deps.Delay);

            log?.Invoke($"[{track}] {caseSet.Cases.Count} case(s) from '{caseSet.Name}' v{caseSet.Version}.");
            var trackObservations = await runner.RunTrackAsync(caseSet, arms, log, cancellationToken).ConfigureAwait(false);
            observations.AddRange(trackObservations);
            tracks.Add(TrackAnalyzer.Analyze(caseSet, trackObservations, settings));
        }

        var finished = DateTimeOffset.UtcNow;
        return new BenchmarkResult(
            1,
            finished.ToString("u", System.Globalization.CultureInfo.InvariantCulture),
            (finished - started).TotalSeconds,
            deps.Config,
            tracks,
            observations);
    }
}
