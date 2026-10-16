using System.Globalization;

namespace Raffa.JevBenchmark.Tests.Harness;

/// <summary>Tracks the benchmark can run. Each track has its own labelled set and its own pair of arms.</summary>
public static class BenchTracks
{
    /// <summary>The `classify` role (document type), Foundry vs Jev-with-Foundry-fallback.</summary>
    public const string Classify = "classify";

    /// <summary>The capability investigator's "is this turn asking for something Raffa cannot do" decision.</summary>
    public const string Gaps = "gaps";

    public static readonly IReadOnlyList<string> All = [Classify, Gaps];
}

/// <summary>
/// Everything the operator can tune, read from <c>RAFFA_JEV_BENCH_*</c> variables. Defaults are chosen
/// to keep a first run cheap; the parsed values are echoed in the result so a run is reproducible.
/// </summary>
public sealed record BenchSettings
{
    public int Repetitions { get; init; } = 3;

    public int WarmupCalls { get; init; } = 2;

    public string OutputDirectory { get; init; } = "jev-benchmark-out";

    public IReadOnlyList<string> Tracks { get; init; } = BenchTracks.All;

    /// <summary>0 = every case; otherwise the first N cases of each track (deterministic).</summary>
    public int MaxCasesPerTrack { get; init; }

    public int DelayBetweenCallsMs { get; init; }

    public int BootstrapIterations { get; init; } = 10_000;

    public int Seed { get; init; } = 20_261_016;

    public double Alpha { get; init; } = 0.05;

    public int GapTimeoutSeconds { get; init; } = 30;

    /// <summary>Optional list price of the Foundry models, USD per million input tokens; null = unknown.</summary>
    public double? FoundryUsdPerMillionInputTokens { get; init; }

    public double? FoundryUsdPerMillionOutputTokens { get; init; }

    public static BenchSettings FromEnvironment(Func<string, string?> get)
    {
        ArgumentNullException.ThrowIfNull(get);

        var tracks = BenchTracks.All;
        var rawTracks = get("RAFFA_JEV_BENCH_TRACKS");
        if (!string.IsNullOrWhiteSpace(rawTracks))
        {
            tracks = rawTracks.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var unknown = tracks.Where(t => !BenchTracks.All.Contains(t)).ToList();
            if (tracks.Count == 0 || unknown.Count > 0)
            {
                throw new ArgumentException(
                    $"RAFFA_JEV_BENCH_TRACKS must list {string.Join(" and/or ", BenchTracks.All)}; got '{rawTracks}'.");
            }
        }

        var settings = new BenchSettings
        {
            Repetitions = ReadInt(get, "RAFFA_JEV_BENCH_REPS", 3, min: 1, max: 100),
            WarmupCalls = ReadInt(get, "RAFFA_JEV_BENCH_WARMUP", 2, min: 0, max: 50),
            MaxCasesPerTrack = ReadInt(get, "RAFFA_JEV_BENCH_MAX_CASES", 0, min: 0, max: 100_000),
            DelayBetweenCallsMs = ReadInt(get, "RAFFA_JEV_BENCH_DELAY_MS", 0, min: 0, max: 600_000),
            BootstrapIterations = ReadInt(get, "RAFFA_JEV_BENCH_BOOTSTRAP", 10_000, min: 100, max: 1_000_000),
            Seed = ReadInt(get, "RAFFA_JEV_BENCH_SEED", 20_261_016, min: 0, max: int.MaxValue),
            GapTimeoutSeconds = ReadInt(get, "RAFFA_JEV_BENCH_GAP_TIMEOUT_S", 30, min: 1, max: 600),
            OutputDirectory = string.IsNullOrWhiteSpace(get("RAFFA_JEV_BENCH_OUT")) ? "jev-benchmark-out" : get("RAFFA_JEV_BENCH_OUT")!.Trim(),
            Tracks = tracks,
            FoundryUsdPerMillionInputTokens = ReadDouble(get, "RAFFA_JEV_BENCH_FOUNDRY_USD_PER_1M_IN"),
            FoundryUsdPerMillionOutputTokens = ReadDouble(get, "RAFFA_JEV_BENCH_FOUNDRY_USD_PER_1M_OUT"),
        };

        return settings;
    }

    private static int ReadInt(Func<string, string?> get, string name, int fallback, int min, int max)
    {
        var raw = get(name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
        {
            throw new ArgumentException($"{name} must be an integer between {min} and {max}; got '{raw}'.");
        }

        return value;
    }

    private static double? ReadDouble(Func<string, string?> get, string name)
    {
        var raw = get(name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            throw new ArgumentException($"{name} must be a non-negative number (invariant culture); got '{raw}'.");
        }

        return value;
    }
}
