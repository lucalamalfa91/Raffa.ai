namespace Raffa.JevBenchmark.Tests.Metrics;

/// <summary>Latency summary in milliseconds. Percentiles use linear interpolation (the R-7 / NumPy default).</summary>
public sealed record LatencyStats(int Count, double Mean, double Min, double P50, double P95, double P99, double Max)
{
    public static LatencyStats Empty { get; } = new(0, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);

    public static LatencyStats From(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
        {
            return Empty;
        }

        var sorted = values.Order().ToArray();
        return new LatencyStats(
            sorted.Length,
            sorted.Average(),
            sorted[0],
            Stats.Percentile(sorted, 50),
            Stats.Percentile(sorted, 95),
            Stats.Percentile(sorted, 99),
            sorted[^1]);
    }
}

/// <summary>A confidence interval for a statistic (percentile bootstrap).</summary>
public sealed record Interval(double Estimate, double Lower, double Upper)
{
    public bool ExcludesZero => !double.IsNaN(Lower) && !double.IsNaN(Upper) && (Lower > 0 || Upper < 0);
}

public static class Stats
{
    /// <summary>Percentile <paramref name="p"/> (0..100) of an ascending-sorted array; NaN when empty.</summary>
    public static double Percentile(IReadOnlyList<double> sortedAscending, double p)
    {
        if (sortedAscending.Count == 0)
        {
            return double.NaN;
        }

        if (sortedAscending.Count == 1)
        {
            return sortedAscending[0];
        }

        var rank = Math.Clamp(p, 0, 100) / 100.0 * (sortedAscending.Count - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        return sortedAscending[lower] + (sortedAscending[upper] - sortedAscending[lower]) * (rank - lower);
    }

    public static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return Percentile(sorted, 50);
    }

    /// <summary>
    /// Exact two-sided McNemar test on the discordant pairs: <paramref name="onlyBCorrect"/> cases the
    /// second arm got right and the first wrong, <paramref name="onlyACorrect"/> the reverse. Under H0
    /// each discordant pair is a fair coin, so the p-value is twice the lower binomial tail (capped at 1).
    /// </summary>
    public static double McNemarExactP(int onlyACorrect, int onlyBCorrect)
    {
        var n = onlyACorrect + onlyBCorrect;
        if (n == 0)
        {
            return 1.0;
        }

        var k = Math.Min(onlyACorrect, onlyBCorrect);
        var logHalfN = -n * Math.Log(2);
        var tail = 0.0;
        for (var i = 0; i <= k; i++)
        {
            tail += Math.Exp(LogChoose(n, i) + logHalfN);
        }

        return Math.Min(1.0, 2 * tail);
    }

    private static double LogChoose(int n, int k) => LogFactorial(n) - LogFactorial(k) - LogFactorial(n - k);

    private static double LogFactorial(int n)
    {
        var sum = 0.0;
        for (var i = 2; i <= n; i++)
        {
            sum += Math.Log(i);
        }

        return sum;
    }

    /// <summary>
    /// Percentile-bootstrap CI (default 95 percent) of <paramref name="statistic"/> over resampled
    /// <paramref name="values"/> (a per-case paired difference). Seeded, so the same data always gives the
    /// same interval.
    /// </summary>
    public static Interval BootstrapCi(
        IReadOnlyList<double> values, Func<double[], double> statistic, int iterations, int seed, double alpha = 0.05)
    {
        if (values.Count == 0)
        {
            return new Interval(double.NaN, double.NaN, double.NaN);
        }

        var source = values.ToArray();
        var estimate = statistic(source);
        var random = new Random(seed);
        var sample = new double[source.Length];
        var estimates = new double[iterations];
        for (var i = 0; i < iterations; i++)
        {
            for (var j = 0; j < sample.Length; j++)
            {
                sample[j] = source[random.Next(source.Length)];
            }

            estimates[i] = statistic(sample);
        }

        Array.Sort(estimates);
        return new Interval(estimate, Percentile(estimates, alpha / 2 * 100), Percentile(estimates, (1 - alpha / 2) * 100));
    }

    public static Interval BootstrapMeanCi(IReadOnlyList<double> values, int iterations, int seed, double alpha = 0.05) =>
        BootstrapCi(values, v => v.Average(), iterations, seed, alpha);

    /// <summary>Cohen's kappa between two aligned label sequences; NaN when undefined (empty, or chance agreement is 1).</summary>
    public static double CohensKappa(IReadOnlyList<string> first, IReadOnlyList<string> second)
    {
        if (first.Count != second.Count)
        {
            throw new ArgumentException("Label sequences must have the same length.");
        }

        var n = first.Count;
        if (n == 0)
        {
            return double.NaN;
        }

        var observed = first.Zip(second).Count(pair => pair.First == pair.Second) / (double)n;
        var expected = first.Concat(second).Distinct(StringComparer.Ordinal)
            .Sum(label => first.Count(x => x == label) / (double)n * (second.Count(x => x == label) / (double)n));

        return Math.Abs(1 - expected) < 1e-12 ? double.NaN : (observed - expected) / (1 - expected);
    }
}
