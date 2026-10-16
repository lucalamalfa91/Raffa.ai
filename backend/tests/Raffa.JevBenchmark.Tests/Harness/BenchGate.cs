namespace Raffa.JevBenchmark.Tests.Harness;

/// <summary>
/// Opt-in gate of the live Jev benchmark. The benchmark calls a billed model (Foundry) and a billed
/// third party (Jev through OpenRouter), so it must never run by accident: it runs only when
/// <c>RAFFA_JEV_BENCH=1</c> (exactly "1", not "true" or "yes": a typo cannot turn CI into a billed run)
/// AND the Foundry endpoint AND the Jev key are present in the environment. Anything else is a skip
/// with a reason, never a failure and never a network call.
/// </summary>
public static class BenchGate
{
    public const string EnableVariable = "RAFFA_JEV_BENCH";

    /// <summary>The standard .NET configuration spelling of <c>AiGateway:Endpoint</c> (Foundry).</summary>
    public const string FoundryEndpointVariable = "AiGateway__Endpoint";

    /// <summary>The standard .NET configuration spelling of <c>AiGateway:Jev:ApiKey</c>.</summary>
    public const string JevApiKeyVariable = "AiGateway__Jev__ApiKey";

    public static GateDecision Evaluate(Func<string, string?> getVariable)
    {
        ArgumentNullException.ThrowIfNull(getVariable);

        if (!string.Equals(getVariable(EnableVariable)?.Trim(), "1", StringComparison.Ordinal))
        {
            return new GateDecision(false, $"Live Jev benchmark is opt-in: set {EnableVariable}=1 to run it (it calls Foundry and Jev and is billed).");
        }

        var missing = new[] { FoundryEndpointVariable, JevApiKeyVariable }
            .Where(name => string.IsNullOrWhiteSpace(getVariable(name)))
            .ToList();

        return missing.Count == 0
            ? new GateDecision(true, null)
            : new GateDecision(false, $"{EnableVariable}=1 but the environment is incomplete; missing: {string.Join(", ", missing)}.");
    }

    public static GateDecision EvaluateProcessEnvironment() => Evaluate(Environment.GetEnvironmentVariable);
}

/// <param name="ShouldRun">True only when every precondition holds.</param>
/// <param name="SkipReason">Human-readable reason; null when <paramref name="ShouldRun"/>.</param>
public sealed record GateDecision(bool ShouldRun, string? SkipReason);

/// <summary>
/// <c>[Fact]</c> that is skipped (xunit "Skip", visible in the run output with its reason) unless
/// <see cref="BenchGate"/> allows the live run. The decision is taken once, at discovery time, from
/// the process environment.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class JevBenchFactAttribute : FactAttribute
{
    public JevBenchFactAttribute()
    {
        var decision = BenchGate.EvaluateProcessEnvironment();
        if (!decision.ShouldRun)
        {
            Skip = decision.SkipReason;
        }
    }
}
