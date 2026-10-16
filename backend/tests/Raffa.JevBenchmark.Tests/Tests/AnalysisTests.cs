using Raffa.JevBenchmark.Tests.Harness;
using Raffa.JevBenchmark.Tests.Reporting;
using static Raffa.JevBenchmark.Tests.Tests.Samples;

namespace Raffa.JevBenchmark.Tests.Tests;

public sealed class AnalysisTests
{
    private static readonly BenchSettings Settings = new() { Repetitions = 2, BootstrapIterations = 300, Seed = 7 };

    private static CaseSet GapSet(int cases) => new(
        BenchTracks.Gaps, "test", "1", "unit test", "x.json", [CaseSets.GapLabel, CaseSets.NoGapLabel],
        Enumerable.Range(0, cases).Select(i => new LabelledCase(BenchTracks.Gaps, $"c{i:00}", "t", CaseSets.GapLabel, "en", "x")).ToList());

    /// <summary>A predicts "gap" on the first <paramref name="aRight"/> cases, B on the first <paramref name="bRight"/>; two reps each.</summary>
    private static List<CaseObservation> Observations(int cases, int aRight, int bRight, double latencyA = 100, double latencyB = 50, int bFallbacks = 0)
    {
        var list = new List<CaseObservation>();
        for (var i = 0; i < cases; i++)
        {
            for (var rep = 0; rep < 2; rep++)
            {
                var id = $"c{i:00}";
                list.Add(Obs(BenchArms.Baseline, id, rep, "gap", i < aRight ? "gap" : "no-gap", latencyA));
                list.Add(Obs(
                    BenchArms.WithJev, id, rep, "gap", i < bRight ? "gap" : "no-gap", latencyB,
                    fallback: i * 2 + rep < bFallbacks ? "jev-error" : null));
            }
        }

        return list;
    }

    [Fact]
    public void A_clear_improvement_is_reported_with_the_cases_jev_fixed()
    {
        var result = TrackAnalyzer.Analyze(GapSet(40), Observations(40, aRight: 20, bRight: 40, bFallbacks: 4), Settings);

        Assert.Equal(0.5, result.Baseline.StrictAccuracy, 9);
        Assert.Equal(1.0, result.WithJev.StrictAccuracy, 9);
        var c = result.Comparison;
        Assert.Equal(40, c.CasesCompared);
        Assert.Equal(20, c.FixedByJev.Count);
        Assert.Empty(c.RegressedByJev);
        Assert.Equal(20, c.McNemar.OnlyJevCorrect);
        Assert.True(c.McNemar.PValue < 0.001);
        Assert.Equal(0.5, c.AccuracyDelta.Estimate, 9);
        Assert.True(c.AccuracyDelta.ExcludesZero);
        Assert.Equal("improvement", c.Verdict.Quality);
        Assert.Equal("faster", c.Verdict.Latency);
        Assert.Equal(-50, c.P50DeltaMs, 9);
        Assert.Equal(-50, c.PairedLatencyDeltaMs.Estimate, 9);
        Assert.Equal(4 / 80.0, result.WithJev.FallbackRate, 9);
        Assert.Equal(4, result.WithJev.FallbackJevError);
        Assert.Equal(0, result.Baseline.Fallbacks);
        Assert.Equal(160, result.Baseline.Calls + result.WithJev.Calls);
    }

    [Fact]
    public void A_regression_is_called_a_regression()
    {
        var result = TrackAnalyzer.Analyze(GapSet(40), Observations(40, aRight: 40, bRight: 20), Settings);

        Assert.Equal("regression", result.Comparison.Verdict.Quality);
        Assert.Equal(20, result.Comparison.RegressedByJev.Count);
        Assert.Empty(result.Comparison.FixedByJev);
    }

    [Fact]
    public void Identical_arms_show_no_significant_difference_and_full_agreement()
    {
        var result = TrackAnalyzer.Analyze(GapSet(40), Observations(40, aRight: 25, bRight: 25, latencyA: 100, latencyB: 100), Settings);
        var c = result.Comparison;

        Assert.Equal("no-significant-difference", c.Verdict.Quality);
        Assert.Equal("no-significant-difference", c.Verdict.Latency);
        Assert.Equal(1.0, c.McNemar.PValue, 9);
        Assert.Equal(1.0, c.Agreement, 9);
        Assert.Equal(1.0, c.Kappa, 9);
        Assert.Equal(160 / 2, c.PairedCalls);
    }

    [Fact]
    public void A_small_sample_cannot_be_called_an_improvement_and_says_so()
    {
        // 3 fixed cases out of 10: exact McNemar p = 0.25.
        var result = TrackAnalyzer.Analyze(GapSet(10), Observations(10, aRight: 5, bRight: 8), Settings);

        Assert.Equal(0.25, result.Comparison.McNemar.PValue, 9);
        // Never an "improvement": the exact test alone does not clear alpha ("inconclusive" if the bootstrap CI happens to exclude zero).
        Assert.Contains(result.Comparison.Verdict.Quality, new[] { "no-significant-difference", "inconclusive" });
        Assert.Contains(result.Comparison.Verdict.Warnings, w => w.Contains("little power", StringComparison.Ordinal));
        Assert.Contains(result.Comparison.Verdict.Warnings, w => w.Contains("McNemar is weak", StringComparison.Ordinal));
    }

    [Fact]
    public void A_high_fallback_rate_warns_that_arm_B_is_mostly_Foundry()
    {
        var result = TrackAnalyzer.Analyze(GapSet(40), Observations(40, aRight: 20, bRight: 40, bFallbacks: 40), Settings);

        Assert.Equal(0.5, result.WithJev.FallbackRate, 9);
        Assert.Contains(result.Comparison.Verdict.Warnings, w => w.Contains("served by Foundry", StringComparison.Ordinal));
    }

    [Fact]
    public void Errors_are_counted_wrong_in_strict_accuracy_but_not_in_answered_accuracy()
    {
        var set = GapSet(2);
        var obs = new List<CaseObservation>
        {
            Obs(BenchArms.Baseline, "c00", 0, "gap", "gap"),
            Obs(BenchArms.Baseline, "c01", 0, "gap", null, error: true, timedOut: true),
            Obs(BenchArms.WithJev, "c00", 0, "gap", "gap"),
            Obs(BenchArms.WithJev, "c01", 0, "gap", "gap"),
        };

        var result = TrackAnalyzer.Analyze(set, obs, Settings with { Repetitions = 1 });

        Assert.Equal(0.5, result.Baseline.StrictAccuracy, 9);
        Assert.Equal(1.0, result.Baseline.AnsweredAccuracy, 9);
        Assert.Equal(0.5, result.Baseline.ErrorRate, 9);
        Assert.Equal(0.5, result.Baseline.TimeoutRate, 9);
        Assert.Equal(1, result.Baseline.Latency.Count); // an errored call has no meaningful latency sample
        Assert.Equal([1, 0, 1], result.Baseline.Confusion[0]);
    }

    [Fact]
    public void Stability_measures_how_often_the_same_input_gives_the_same_answer()
    {
        var set = GapSet(2);
        var obs = new List<CaseObservation>
        {
            Obs(BenchArms.Baseline, "c00", 0, "gap", "gap"),
            Obs(BenchArms.Baseline, "c00", 1, "gap", "gap"),
            Obs(BenchArms.Baseline, "c00", 2, "gap", "no-gap"),
            Obs(BenchArms.Baseline, "c01", 0, "gap", "gap"),
            Obs(BenchArms.Baseline, "c01", 1, "gap", "gap"),
            Obs(BenchArms.Baseline, "c01", 2, "gap", "gap"),
        };

        var summary = TrackAnalyzer.Summarize(BenchArms.Baseline, false, obs, set, Settings);

        Assert.Equal(2, summary.Stability.CasesMeasured);
        Assert.Equal((2 / 3.0 + 1.0) / 2, summary.Stability.MeanConsistency, 9);
        Assert.Equal(1, summary.Stability.CasesWithFlips);
        Assert.Equal(["c00"], summary.Stability.FlippedCaseIds);
    }

    [Fact]
    public void A_single_repetition_has_no_stability_figure()
    {
        var summary = TrackAnalyzer.Summarize(
            BenchArms.Baseline, false, [Obs(BenchArms.Baseline, "c00", 0, "gap", "gap")], GapSet(1), Settings);

        Assert.True(double.IsNaN(summary.Stability.MeanConsistency));
    }

    [Fact]
    public void Cost_combines_foundry_token_estimate_and_the_cost_openrouter_reports()
    {
        var priced = Settings with { FoundryUsdPerMillionInputTokens = 10, FoundryUsdPerMillionOutputTokens = 0 };
        var foundryCall = new CallRecord("foundry", "analyze", 10, true, true, null, 100_000, 0, null, null);
        var jevCall = new CallRecord("jev", "decision", 5, true, true, 200, 500, 5, 0.002, null);
        var obsA = new[] { Obs(BenchArms.Baseline, "c00", 0, "gap", "gap", calls: [foundryCall]) };
        var obsB = new[] { Obs(BenchArms.WithJev, "c00", 0, "gap", "gap", calls: [jevCall]) };

        var a = TrackAnalyzer.Summarize(BenchArms.Baseline, false, obsA, GapSet(1), priced);
        var b = TrackAnalyzer.Summarize(BenchArms.WithJev, true, obsB, GapSet(1), priced);

        Assert.Equal(1.0, a.Usage.FoundryEstimatedCostUsd!.Value, 9);
        Assert.Equal(1000.0, a.Usage.CostPer1000Calls!.Value, 9);
        Assert.Equal(0.002, b.Usage.JevReportedCostUsd!.Value, 9);
        Assert.Equal(2.0, b.Usage.CostPer1000Calls!.Value, 9);
        Assert.True(b.Usage.CostComplete);
        Assert.Equal(500, b.Usage.JevInputTokens);
    }

    [Fact]
    public void Without_prices_tokens_are_reported_but_cost_is_not_invented()
    {
        var call = new CallRecord("foundry", "classify", 10, true, true, null, 100, 10, null, null);
        var summary = TrackAnalyzer.Summarize(
            BenchArms.Baseline, false, [Obs(BenchArms.Baseline, "c00", 0, "gap", "gap", calls: [call])], GapSet(1), Settings);

        Assert.Equal(110, summary.Usage.MeanTokensPerCall, 9);
        Assert.Null(summary.Usage.CostPer1000Calls);
        Assert.False(summary.Usage.CostComplete);
    }

    [Fact]
    public void Jev_cost_is_unknown_when_a_successful_call_reports_none()
    {
        var priced = Settings with { FoundryUsdPerMillionInputTokens = 1, FoundryUsdPerMillionOutputTokens = 1 };
        var withCost = new CallRecord("jev", "decision", 5, true, true, 200, 500, 5, 0.002, null);
        var withoutCost = new CallRecord("jev", "decision", 5, true, true, 200, 500, 5, null, null);

        var summary = TrackAnalyzer.Summarize(
            BenchArms.WithJev, true, [Obs(BenchArms.WithJev, "c00", 0, "gap", "gap", calls: [withCost, withoutCost])], GapSet(1), priced);

        Assert.Null(summary.Usage.JevReportedCostUsd);
        Assert.False(summary.Usage.CostComplete);
    }

    [Fact]
    public void Cost_verdict_compares_per_1000_calls_with_a_five_percent_band()
    {
        var priced = Settings with { FoundryUsdPerMillionInputTokens = 10, FoundryUsdPerMillionOutputTokens = 0 };
        CaseObservation A(int id) => Obs(BenchArms.Baseline, $"c{id:00}", 0, "gap", "gap", calls: [new CallRecord("foundry", "analyze", 1, true, true, null, 100_000, 0, null, null)]);
        CaseObservation B(int id) => Obs(BenchArms.WithJev, $"c{id:00}", 0, "gap", "gap", calls: [new CallRecord("jev", "decision", 1, true, true, 200, 10, 1, 0.1, null)]);

        var cheaper = TrackAnalyzer.Analyze(GapSet(2), [A(0), A(1), B(0), B(1)], priced with { Repetitions = 1 });

        Assert.Equal("cheaper", cheaper.Comparison.Verdict.Cost);
        Assert.True(cheaper.Comparison.CostPer1000DeltaUsd < 0);
    }

    [Fact]
    public void Missing_data_is_insufficient_not_a_crash()
    {
        var result = TrackAnalyzer.Analyze(GapSet(3), [], Settings);

        Assert.Equal("insufficient-data", result.Comparison.Verdict.Quality);
        Assert.Equal(0, result.Comparison.CasesCompared);
    }
}
