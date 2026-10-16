using Raffa.JevBenchmark.Tests.Harness;
using Raffa.JevBenchmark.Tests.Metrics;

namespace Raffa.JevBenchmark.Tests.Reporting;

/// <summary>Turns the raw observations of one track into the per-arm summaries and the paired comparison.</summary>
public static class TrackAnalyzer
{
    public const int MinCasesForPower = 30;
    public const double HighFallbackRate = 0.20;
    public const double CostSimilarBand = 0.05;

    public static TrackResult Analyze(CaseSet caseSet, IReadOnlyList<CaseObservation> observations, BenchSettings settings)
    {
        var baseline = observations.Where(o => o.Arm == BenchArms.Baseline).ToList();
        var jev = observations.Where(o => o.Arm == BenchArms.WithJev).ToList();

        var dataset = new DatasetInfo(
            caseSet.Track,
            caseSet.Name,
            caseSet.Version,
            caseSet.Provenance,
            Path.GetFileName(caseSet.SourceFile),
            caseSet.ContentHash,
            caseSet.Cases.Count,
            caseSet.Labels,
            caseSet.Cases.GroupBy(c => c.ExpectedLabel).ToDictionary(g => g.Key, g => g.Count()));

        var summaryA = Summarize(BenchArms.Baseline, false, baseline, caseSet, settings);
        var summaryB = Summarize(BenchArms.WithJev, true, jev, caseSet, settings);
        var comparison = Compare(caseSet, baseline, jev, summaryA, summaryB, settings);
        return new TrackResult(caseSet.Track, dataset, summaryA, summaryB, comparison);
    }

    public static ArmSummary Summarize(string arm, bool jevEnabled, IReadOnlyList<CaseObservation> obs, CaseSet caseSet, BenchSettings settings)
    {
        var matrix = new ConfusionMatrix(caseSet.Labels);
        foreach (var o in obs)
        {
            matrix.Add(o.ExpectedLabel, o.Error ? null : o.Predicted);
        }

        var calls = obs.Count;
        var errors = obs.Count(o => o.Error);
        var timeouts = obs.Count(o => o.TimedOut);
        var fallbacks = obs.Count(o => o.FellBack);

        return new ArmSummary(
            arm,
            jevEnabled,
            calls,
            errors,
            timeouts,
            Rate(errors, calls),
            Rate(timeouts, calls),
            fallbacks,
            Rate(fallbacks, calls),
            obs.Count(o => o.FallbackReason == "jev-error"),
            obs.Count(o => o.FallbackReason == "low-confidence"),
            obs.Count(o => o.ServedBy == "jev"),
            matrix.StrictAccuracy,
            matrix.AnsweredAccuracy,
            matrix.MacroF1,
            matrix.PerClass(),
            matrix.Labels,
            matrix.ToRows(),
            LatencyStats.From(obs.Where(o => !o.Error).Select(o => o.LatencyMs).ToList()),
            Usage(obs, jevEnabled, settings),
            Stability(obs));
    }

    private static double Rate(int count, int total) => total == 0 ? double.NaN : count / (double)total;

    private static UsageSummary Usage(IReadOnlyList<CaseObservation> obs, bool jevEnabled, BenchSettings settings)
    {
        var all = obs.SelectMany(o => o.Calls).ToList();
        var foundry = all.Where(c => c.Backend == "foundry").ToList();
        var jev = all.Where(c => c.Backend == "jev").ToList();

        var foundryIn = foundry.Sum(c => c.InputTokens);
        var foundryOut = foundry.Sum(c => c.OutputTokens);
        var jevIn = jev.Sum(c => c.InputTokens);
        var jevOut = jev.Sum(c => c.OutputTokens);

        double? foundryCost = settings.FoundryUsdPerMillionInputTokens is { } priceIn && settings.FoundryUsdPerMillionOutputTokens is { } priceOut
            ? foundryIn / 1_000_000.0 * priceIn + foundryOut / 1_000_000.0 * priceOut
            : null;

        var jevCosts = jev.Where(c => c.CostUsd.HasValue).Select(c => c.CostUsd!.Value).ToList();
        double? jevCost = jev.Count == 0 ? 0.0 : jevCosts.Count == 0 ? null : jevCosts.Sum();
        if (jev.Count > 0 && jevCosts.Count > 0 && jevCosts.Count < jev.Count(c => c.Success))
        {
            jevCost = null; // some successful calls did not report a cost: the sum would understate it.
        }

        var complete = foundryCost.HasValue && (!jevEnabled || jevCost.HasValue);
        double? total = complete ? foundryCost!.Value + (jevCost ?? 0.0) : null;
        var calls = obs.Count;

        return new UsageSummary(
            foundry.Count,
            foundryIn,
            foundryOut,
            jev.Count,
            jev.Count(c => !c.Success),
            jevIn,
            jevOut,
            jevCost,
            foundryCost,
            total,
            total.HasValue && calls > 0 ? total.Value / calls * 1000 : null,
            complete,
            calls == 0 ? double.NaN : (foundryIn + foundryOut + jevIn + jevOut) / (double)calls);
    }

    private static StabilitySummary Stability(IReadOnlyList<CaseObservation> obs)
    {
        var perCase = obs.GroupBy(o => o.CaseId).Where(g => g.Count() >= 2).ToList();
        if (perCase.Count == 0)
        {
            return new StabilitySummary(0, double.NaN, 0, []);
        }

        var consistencies = new List<double>();
        var flipped = new List<string>();
        foreach (var group in perCase)
        {
            var outcomes = group.Select(o => o.Error ? ConfusionMatrix.ErrorColumn : o.Predicted ?? ConfusionMatrix.ErrorColumn).ToList();
            var modal = outcomes.GroupBy(x => x).Max(g => g.Count());
            consistencies.Add(modal / (double)outcomes.Count);
            if (outcomes.Distinct(StringComparer.Ordinal).Count() > 1)
            {
                flipped.Add(group.Key);
            }
        }

        return new StabilitySummary(perCase.Count, consistencies.Average(), flipped.Count, flipped);
    }

    private static ComparisonSummary Compare(
        CaseSet caseSet,
        IReadOnlyList<CaseObservation> baseline,
        IReadOnlyList<CaseObservation> jev,
        ArmSummary summaryA,
        ArmSummary summaryB,
        BenchSettings settings)
    {
        var byCaseA = baseline.GroupBy(o => o.CaseId).ToDictionary(g => g.Key, g => g.ToList());
        var byCaseB = jev.GroupBy(o => o.CaseId).ToDictionary(g => g.Key, g => g.ToList());
        var caseIds = caseSet.Cases.Select(c => c.Id).Where(id => byCaseA.ContainsKey(id) && byCaseB.ContainsKey(id)).ToList();

        // Case level: a case is "correct" for an arm when more than half of its repetitions are.
        var rateDiffs = new List<double>();
        var latencyDiffs = new List<double>();
        int both = 0, onlyA = 0, onlyB = 0, neither = 0;
        var fixedByJev = new List<string>();
        var regressed = new List<string>();
        double sumRateA = 0, sumRateB = 0;

        foreach (var id in caseIds)
        {
            var a = byCaseA[id];
            var b = byCaseB[id];
            var rateA = a.Count(o => o.Correct) / (double)a.Count;
            var rateB = b.Count(o => o.Correct) / (double)b.Count;
            sumRateA += rateA;
            sumRateB += rateB;
            rateDiffs.Add(rateB - rateA);

            var majorityA = rateA > 0.5;
            var majorityB = rateB > 0.5;
            switch (majorityA, majorityB)
            {
                case (true, true): both++; break;
                case (true, false): onlyA++; regressed.Add(id); break;
                case (false, true): onlyB++; fixedByJev.Add(id); break;
                default: neither++; break;
            }

            var latA = a.Where(o => !o.Error).Select(o => o.LatencyMs).ToList();
            var latB = b.Where(o => !o.Error).Select(o => o.LatencyMs).ToList();
            if (latA.Count > 0 && latB.Count > 0)
            {
                latencyDiffs.Add(Stats.Median(latB) - Stats.Median(latA));
            }
        }

        var n = caseIds.Count;
        var accuracyDelta = Stats.BootstrapMeanCi(rateDiffs, settings.BootstrapIterations, settings.Seed, settings.Alpha);
        var latencyDelta = Stats.BootstrapMeanCi(latencyDiffs, settings.BootstrapIterations, settings.Seed + 1, settings.Alpha);
        var mcNemar = new McNemarSummary(both, onlyA, onlyB, neither, Stats.McNemarExactP(onlyA, onlyB));

        // Agreement: repetition-aligned pairs where both arms answered.
        var pairsA = new List<string>();
        var pairsB = new List<string>();
        foreach (var id in caseIds)
        {
            var a = byCaseA[id].Where(o => !o.Error).ToDictionary(o => o.Rep, o => o.Predicted!);
            foreach (var o in byCaseB[id].Where(o => !o.Error))
            {
                if (a.TryGetValue(o.Rep, out var predictedA))
                {
                    pairsA.Add(predictedA);
                    pairsB.Add(o.Predicted!);
                }
            }
        }

        var agreement = pairsA.Count == 0 ? double.NaN : pairsA.Zip(pairsB).Count(p => p.First == p.Second) / (double)pairsA.Count;
        var kappa = pairsA.Count == 0 ? double.NaN : Stats.CohensKappa(pairsA, pairsB);

        double? costDelta = summaryA.Usage.CostPer1000Calls is { } costA && summaryB.Usage.CostPer1000Calls is { } costB ? costB - costA : null;
        var f1Delta = double.IsNaN(summaryA.MacroF1) || double.IsNaN(summaryB.MacroF1) ? (double?)null : summaryB.MacroF1 - summaryA.MacroF1;

        var comparison = new ComparisonSummary(
            n,
            pairsA.Count,
            agreement,
            kappa,
            n == 0 ? double.NaN : sumRateA / n,
            n == 0 ? double.NaN : sumRateB / n,
            accuracyDelta,
            mcNemar,
            fixedByJev,
            regressed,
            summaryB.Latency.P50 - summaryA.Latency.P50,
            summaryB.Latency.P95 - summaryA.Latency.P95,
            summaryB.Latency.P99 - summaryA.Latency.P99,
            latencyDelta,
            costDelta,
            f1Delta,
            new Verdict("", "", "", "", []));

        return comparison with { Verdict = VerdictEngine.Decide(comparison, summaryA, summaryB, settings) };
    }
}

/// <summary>
/// Turns the statistics into a plain verdict. The rule is deliberately conservative: quality counts as
/// improved (or regressed) only when the exact McNemar test on the per-case outcomes is below alpha AND
/// the bootstrap CI of the accuracy difference excludes zero; one without the other is "inconclusive".
/// </summary>
public static class VerdictEngine
{
    public static Verdict Decide(ComparisonSummary c, ArmSummary a, ArmSummary b, BenchSettings settings)
    {
        var warnings = new List<string>();
        if (c.CasesCompared < TrackAnalyzer.MinCasesForPower)
        {
            warnings.Add($"Only {c.CasesCompared} paired cases (< {TrackAnalyzer.MinCasesForPower}): the test has little power, a real difference can go undetected.");
        }

        if (c.McNemar.OnlyBaselineCorrect + c.McNemar.OnlyJevCorrect < 10)
        {
            warnings.Add($"Only {c.McNemar.OnlyBaselineCorrect + c.McNemar.OnlyJevCorrect} cases differ between the arms: McNemar is weak below ~10 discordant cases.");
        }

        if (b.FallbackRate > TrackAnalyzer.HighFallbackRate)
        {
            warnings.Add($"Arm B was served by Foundry in {b.FallbackRate:P0} of the calls: its quality largely measures Foundry, not Jev.");
        }

        if (a.ErrorRate > 0 || b.ErrorRate > 0)
        {
            warnings.Add($"Errors: A {a.ErrorRate:P1}, B {b.ErrorRate:P1}. Strict accuracy counts an error as wrong.");
        }

        if (settings.Repetitions < 2)
        {
            warnings.Add("Repetitions = 1: run-to-run stability is not measured.");
        }

        if (!a.Usage.CostComplete || !b.Usage.CostComplete)
        {
            warnings.Add("Cost is incomplete: set RAFFA_JEV_BENCH_FOUNDRY_USD_PER_1M_IN/_OUT to estimate Foundry spend; tokens are reported regardless.");
        }

        string quality;
        if (c.CasesCompared == 0 || double.IsNaN(c.AccuracyDelta.Estimate))
        {
            quality = "insufficient-data";
        }
        else
        {
            var pSignificant = c.McNemar.PValue < settings.Alpha;
            var ciSignificant = c.AccuracyDelta.ExcludesZero;
            quality = (pSignificant, ciSignificant) switch
            {
                (true, true) => c.AccuracyDelta.Estimate > 0 ? "improvement" : "regression",
                (false, false) => "no-significant-difference",
                _ => "inconclusive",
            };
        }

        var latency = c.PairedLatencyDeltaMs.ExcludesZero
            ? (c.PairedLatencyDeltaMs.Estimate < 0 ? "faster" : "slower")
            : double.IsNaN(c.PairedLatencyDeltaMs.Estimate) ? "insufficient-data" : "no-significant-difference";

        string cost;
        if (a.Usage.CostPer1000Calls is { } costA && b.Usage.CostPer1000Calls is { } costB && costA > 0)
        {
            var relative = (costB - costA) / costA;
            cost = Math.Abs(relative) <= TrackAnalyzer.CostSimilarBand ? "similar" : relative < 0 ? "cheaper" : "more-expensive";
        }
        else
        {
            cost = "not-computable";
        }

        var summary =
            $"Quality: {quality} (accuracy B-A = {FormatPoints(c.AccuracyDelta.Estimate)} points, 95% CI {FormatPoints(c.AccuracyDelta.Lower)} .. {FormatPoints(c.AccuracyDelta.Upper)}, " +
            $"McNemar p = {c.McNemar.PValue:0.####}). Latency: {latency} (paired median delta {c.PairedLatencyDeltaMs.Estimate:0.#} ms, " +
            $"95% CI {c.PairedLatencyDeltaMs.Lower:0.#} .. {c.PairedLatencyDeltaMs.Upper:0.#}). Cost: {cost}.";

        return new Verdict(quality, latency, cost, summary, warnings);
    }

    private static string FormatPoints(double fraction) =>
        double.IsNaN(fraction) ? "n/a" : (fraction * 100).ToString("+0.0;-0.0;0.0", System.Globalization.CultureInfo.InvariantCulture);
}
