using System.Reflection;
using Raffa.JevBenchmark.Tests.Harness;
using Raffa.JevBenchmark.Tests.Live;

namespace Raffa.JevBenchmark.Tests.Tests;

public sealed class GateAndSettingsTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] pairs)
    {
        var map = pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        return name => map.GetValueOrDefault(name);
    }

    [Fact]
    public void Gate_is_closed_by_default()
    {
        var decision = BenchGate.Evaluate(Env());

        Assert.False(decision.ShouldRun);
        Assert.Contains("RAFFA_JEV_BENCH=1", decision.SkipReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("11")]
    public void Only_the_exact_value_1_opens_the_gate(string value)
    {
        var decision = BenchGate.Evaluate(Env(
            (BenchGate.EnableVariable, value),
            (BenchGate.FoundryEndpointVariable, "https://foundry.example"),
            (BenchGate.JevApiKeyVariable, "k")));

        Assert.False(decision.ShouldRun);
    }

    [Fact]
    public void Enabled_but_incomplete_environment_skips_and_names_what_is_missing()
    {
        var noKey = BenchGate.Evaluate(Env((BenchGate.EnableVariable, "1"), (BenchGate.FoundryEndpointVariable, "https://foundry.example")));
        var noEndpoint = BenchGate.Evaluate(Env((BenchGate.EnableVariable, "1"), (BenchGate.JevApiKeyVariable, "k")));
        var blankKey = BenchGate.Evaluate(Env((BenchGate.EnableVariable, "1"), (BenchGate.FoundryEndpointVariable, "https://foundry.example"), (BenchGate.JevApiKeyVariable, "  ")));

        Assert.False(noKey.ShouldRun);
        Assert.Contains(BenchGate.JevApiKeyVariable, noKey.SkipReason, StringComparison.Ordinal);
        Assert.False(noEndpoint.ShouldRun);
        Assert.Contains(BenchGate.FoundryEndpointVariable, noEndpoint.SkipReason, StringComparison.Ordinal);
        Assert.False(blankKey.ShouldRun);
    }

    [Fact]
    public void Gate_opens_only_with_the_flag_the_foundry_endpoint_and_the_jev_key()
    {
        var decision = BenchGate.Evaluate(Env(
            (BenchGate.EnableVariable, "1"),
            (BenchGate.FoundryEndpointVariable, "https://foundry.example"),
            (BenchGate.JevApiKeyVariable, "k")));

        Assert.True(decision.ShouldRun);
        Assert.Null(decision.SkipReason);
    }

    [Fact]
    public void The_skip_message_never_contains_the_key_value()
    {
        var decision = BenchGate.Evaluate(Env((BenchGate.EnableVariable, "1"), (BenchGate.JevApiKeyVariable, "SUPER-SECRET")));

        Assert.DoesNotContain("SUPER-SECRET", decision.SkipReason, StringComparison.Ordinal);
    }

    [Fact]
    public void The_live_test_carries_the_gate_and_the_category_trait()
    {
        var method = typeof(JevBenchmarkLiveTests).GetMethod(nameof(JevBenchmarkLiveTests.Run_before_after_comparison_and_write_the_report))!;
        var fact = method.GetCustomAttribute<JevBenchFactAttribute>();

        Assert.NotNull(fact);
        // Same decision the attribute took at discovery: skipped exactly when the process env does not open the gate.
        Assert.Equal(BenchGate.EvaluateProcessEnvironment().SkipReason, fact.Skip);

        Assert.Contains(
            typeof(JevBenchmarkLiveTests).GetCustomAttributesData(),
            data => data.AttributeType == typeof(TraitAttribute) &&
                    data.ConstructorArguments.Select(a => a.Value as string).SequenceEqual(["Category", "JevBenchmark"]));
    }

    [Fact]
    public void Settings_have_cheap_defaults()
    {
        var settings = BenchSettings.FromEnvironment(Env());

        Assert.Equal(3, settings.Repetitions);
        Assert.Equal(2, settings.WarmupCalls);
        Assert.Equal(0, settings.MaxCasesPerTrack);
        Assert.Equal(BenchTracks.All, settings.Tracks);
        Assert.Equal("jev-benchmark-out", settings.OutputDirectory);
        Assert.Null(settings.FoundryUsdPerMillionInputTokens);
    }

    [Fact]
    public void Settings_are_read_from_the_environment()
    {
        var settings = BenchSettings.FromEnvironment(Env(
            ("RAFFA_JEV_BENCH_REPS", "7"),
            ("RAFFA_JEV_BENCH_WARMUP", "0"),
            ("RAFFA_JEV_BENCH_OUT", " /tmp/out "),
            ("RAFFA_JEV_BENCH_TRACKS", "Classify"),
            ("RAFFA_JEV_BENCH_MAX_CASES", "12"),
            ("RAFFA_JEV_BENCH_FOUNDRY_USD_PER_1M_IN", "0.15"),
            ("RAFFA_JEV_BENCH_FOUNDRY_USD_PER_1M_OUT", "0.6")));

        Assert.Equal(7, settings.Repetitions);
        Assert.Equal(0, settings.WarmupCalls);
        Assert.Equal("/tmp/out", settings.OutputDirectory);
        Assert.Equal([BenchTracks.Classify], settings.Tracks);
        Assert.Equal(12, settings.MaxCasesPerTrack);
        Assert.Equal(0.15, settings.FoundryUsdPerMillionInputTokens);
        Assert.Equal(0.6, settings.FoundryUsdPerMillionOutputTokens);
    }

    [Theory]
    [InlineData("RAFFA_JEV_BENCH_REPS", "0")]
    [InlineData("RAFFA_JEV_BENCH_REPS", "abc")]
    [InlineData("RAFFA_JEV_BENCH_WARMUP", "-1")]
    [InlineData("RAFFA_JEV_BENCH_TRACKS", "classify,other")]
    [InlineData("RAFFA_JEV_BENCH_FOUNDRY_USD_PER_1M_IN", "-3")]
    public void Invalid_settings_fail_loudly(string name, string value)
    {
        Assert.Throws<ArgumentException>(() => BenchSettings.FromEnvironment(Env((name, value))));
    }
}
