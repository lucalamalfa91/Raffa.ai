using Raffa.JevBenchmark.Tests.Metrics;

namespace Raffa.JevBenchmark.Tests.Tests;

public sealed class MetricsTests
{
    [Fact]
    public void Percentiles_use_linear_interpolation()
    {
        double[] sorted = [1, 2, 3, 4, 5];

        Assert.Equal(3.0, Stats.Percentile(sorted, 50), 9);
        Assert.Equal(4.8, Stats.Percentile(sorted, 95), 9);
        Assert.Equal(4.96, Stats.Percentile(sorted, 99), 9);
        Assert.Equal(1.0, Stats.Percentile(sorted, 0), 9);
        Assert.Equal(5.0, Stats.Percentile(sorted, 100), 9);
        Assert.Equal(7.0, Stats.Percentile([7.0], 99), 9);
        Assert.True(double.IsNaN(Stats.Percentile([], 50)));
    }

    [Fact]
    public void Latency_stats_summarise_an_unsorted_sample()
    {
        var stats = LatencyStats.From([50, 10, 30, 20, 40]);

        Assert.Equal(5, stats.Count);
        Assert.Equal(30, stats.Mean, 9);
        Assert.Equal(10, stats.Min, 9);
        Assert.Equal(30, stats.P50, 9);
        Assert.Equal(48, stats.P95, 9);
        Assert.Equal(50, stats.Max, 9);
        Assert.Equal(0, LatencyStats.From([]).Count);
    }

    [Theory]
    [InlineData(0, 0, 1.0)]
    [InlineData(5, 5, 1.0)]
    [InlineData(0, 10, 0.001953125)]
    [InlineData(10, 0, 0.001953125)]
    [InlineData(3, 10, 0.0922851562)]
    public void Exact_McNemar_matches_the_binomial_tail(int onlyA, int onlyB, double expected)
    {
        Assert.Equal(expected, Stats.McNemarExactP(onlyA, onlyB), 8);
    }

    [Fact]
    public void Bootstrap_is_deterministic_for_a_seed_and_collapses_on_constant_data()
    {
        double[] diffs = [0.0, 1.0, 0.0, 1.0, 1.0, 0.0, 0.0, 1.0];

        var first = Stats.BootstrapMeanCi(diffs, 500, seed: 42);
        var second = Stats.BootstrapMeanCi(diffs, 500, seed: 42);
        var constant = Stats.BootstrapMeanCi([0.25, 0.25, 0.25], 200, seed: 1);

        Assert.Equal(first, second);
        Assert.Equal(0.5, first.Estimate, 9);
        Assert.True(first.Lower <= first.Estimate && first.Estimate <= first.Upper);
        Assert.Equal(new Interval(0.25, 0.25, 0.25), constant);
        Assert.True(constant.Lower > 0 && constant.ExcludesZero);
        Assert.True(double.IsNaN(Stats.BootstrapMeanCi([], 100, 1).Estimate));
    }

    [Fact]
    public void Kappa_is_one_for_identical_labels_and_zero_for_chance_agreement()
    {
        Assert.Equal(1.0, Stats.CohensKappa(["a", "b", "a", "b"], ["a", "b", "a", "b"]), 9);
        // Half agree, label frequencies balanced: observed 0.5, expected 0.5.
        Assert.Equal(0.0, Stats.CohensKappa(["a", "a", "b", "b"], ["a", "b", "a", "b"]), 9);
        // Everything constant: chance agreement is 1, kappa undefined.
        Assert.True(double.IsNaN(Stats.CohensKappa(["a", "a"], ["a", "a"])));
        Assert.Throws<ArgumentException>(() => Stats.CohensKappa(["a"], ["a", "b"]));
    }

    [Fact]
    public void Confusion_matrix_gives_per_class_precision_recall_and_both_accuracies()
    {
        var matrix = new ConfusionMatrix(["a", "b"]);
        matrix.Add("a", "a");
        matrix.Add("a", "b");
        matrix.Add("b", "b");
        matrix.Add("b", "b");
        matrix.Add("b", null); // an error: not a false positive anywhere, but a missed "b"

        Assert.Equal(5, matrix.Total);
        Assert.Equal(4, matrix.Answered);
        Assert.Equal(3 / 5.0, matrix.StrictAccuracy, 9);
        Assert.Equal(3 / 4.0, matrix.AnsweredAccuracy, 9);

        var a = matrix.PerClass().Single(c => c.Label == "a");
        var b = matrix.PerClass().Single(c => c.Label == "b");
        Assert.Equal(1.0, a.Precision, 9);
        Assert.Equal(0.5, a.Recall, 9);
        Assert.Equal(2 / 3.0, b.Precision, 9);
        Assert.Equal(2 / 3.0, b.Recall, 9);
        Assert.Equal(1, b.FalsePositives);
        Assert.Equal(1, b.FalseNegatives);
        Assert.Equal((2 * 1.0 * 0.5 / 1.5 + 2 / 3.0) / 2, matrix.MacroF1, 9);

        Assert.Equal(1, matrix.Count("b", null));
        Assert.Equal([1, 1, 0], matrix.ToRows()[0]);
        Assert.Equal([0, 2, 1], matrix.ToRows()[1]);
    }

    [Fact]
    public void Confusion_matrix_rejects_labels_outside_the_set()
    {
        var matrix = new ConfusionMatrix(["a", "b"]);

        Assert.Throws<ArgumentException>(() => matrix.Add("c", "a"));
        Assert.Throws<ArgumentException>(() => matrix.Add("a", "c"));
    }

    [Fact]
    public void A_class_that_never_occurs_has_undefined_metrics_and_is_left_out_of_macro_f1()
    {
        var matrix = new ConfusionMatrix(["a", "b", "c"]);
        matrix.Add("a", "a");
        matrix.Add("b", "b");

        var c = matrix.PerClass().Single(x => x.Label == "c");
        Assert.True(double.IsNaN(c.Precision) && double.IsNaN(c.Recall));
        Assert.Equal(1.0, matrix.MacroF1, 9);
    }
}
