using Raffa.AiFlows.CapabilityGaps.Investigation;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Foundry;
using Raffa.AiGateway.Jev;
using Raffa.SharedKernel;

namespace Raffa.JevBenchmark.Tests.Harness;

public static class BenchArms
{
    public const string Baseline = "A-baseline";
    public const string WithJev = "B-jev";
}

/// <summary>One side of the comparison for one track. Runs a case and reports what happened; never throws for a model failure.</summary>
public interface IBenchArm
{
    string Name { get; }

    string Track { get; }

    bool JevEnabled { get; }

    Task<CaseObservation> RunAsync(LabelledCase labelledCase, int rep, CancellationToken cancellationToken);
}

/// <summary>What an arm's model call produced, before it is turned into a <see cref="CaseObservation"/>.</summary>
public sealed record ArmOutcome(
    string? Predicted,
    double? Confidence = null,
    bool? ServedByJev = null,
    string? Outcome = null,
    string? Error = null,
    bool TimedOut = false);

/// <summary>Shared timing / error / fallback bookkeeping.</summary>
public abstract class BenchArmBase(string name, string track, bool jevEnabled, CallLedger ledger, IBenchClock clock) : IBenchArm
{
    public string Name { get; } = name;

    public string Track { get; } = track;

    public bool JevEnabled { get; } = jevEnabled;

    protected abstract Task<ArmOutcome> ExecuteAsync(LabelledCase labelledCase, CancellationToken cancellationToken);

    public async Task<CaseObservation> RunAsync(LabelledCase labelledCase, int rep, CancellationToken cancellationToken)
    {
        ledger.Drain();
        ArmOutcome outcome;
        var start = clock.NowMs;
        try
        {
            outcome = await ExecuteAsync(labelledCase, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            outcome = new ArmOutcome(null, Error: $"{ex.GetType().Name}: {ex.Message}", TimedOut: ex is OperationCanceledException or TimeoutException);
        }

        var latency = clock.NowMs - start;
        var calls = ledger.Drain();
        var error = outcome.Predicted is null || outcome.Error is not null;

        string? fallback = null;
        if (JevEnabled)
        {
            var jevAnswered = calls.Any(c => c.Backend == "jev" && c.Usable);
            if (!jevAnswered)
            {
                fallback = "jev-error";
            }
            else if (outcome.ServedByJev == false)
            {
                fallback = "low-confidence";
            }
        }

        var servedBy = outcome.ServedByJev switch
        {
            true => "jev",
            false => "foundry",
            null => null,
        };

        return new CaseObservation(
            Track,
            Name,
            labelledCase.Id,
            rep,
            labelledCase.ExpectedLabel,
            error ? null : outcome.Predicted,
            outcome.Confidence,
            latency,
            error,
            outcome.TimedOut,
            fallback,
            servedBy,
            outcome.Outcome,
            outcome.Error,
            calls);
    }

    protected static bool LooksLikeTimeout(string? message) =>
        message is not null &&
        (message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("TaskCanceled", StringComparison.OrdinalIgnoreCase));
}

/// <summary>The `classify` role through whatever gateway the arm was given (Foundry alone, or JevAiGateway in front of it).</summary>
public sealed class ClassifyArm(
    string name, IAiGateway gateway, AiGatewayJevOptions jevOptions, bool jevEnabled, CallLedger ledger, IBenchClock clock)
    : BenchArmBase(name, BenchTracks.Classify, jevEnabled, ledger, clock)
{
    protected override async Task<ArmOutcome> ExecuteAsync(LabelledCase labelledCase, CancellationToken cancellationToken)
    {
        var result = await gateway.ClassifyAsync(new AiClassificationRequest(labelledCase.Text), cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return new ArmOutcome(null, Error: result.Error, TimedOut: LooksLikeTimeout(result.Error));
        }

        var metadata = result.Value.Metadata;
        var servedByJev = string.Equals(metadata.ModelId, jevOptions.Model, StringComparison.Ordinal) ||
                          metadata.PromptVersion.StartsWith("jev-", StringComparison.Ordinal);
        return new ArmOutcome(
            result.Value.DocumentType.ToString(),
            result.Value.Confidence,
            servedByJev,
            $"{metadata.ModelId}@{metadata.PromptVersion}");
    }
}

/// <summary>
/// The capability investigator, reduced to the binary decision the Jev verdict path changes: does this
/// turn ask for something Raffa cannot do ("gap": a discovered gap or a known one) or not ("no-gap":
/// an ordinary question, an already-supported operation, or a verdict that did not clear the
/// confidence bar so no follow-up would be sent).
/// </summary>
public sealed class GapVerdictArm(
    string name, CapabilityInvestigator investigator, bool jevEnabled, CallLedger ledger, IBenchClock clock)
    : BenchArmBase(name, BenchTracks.Gaps, jevEnabled, ledger, clock)
{
    protected override async Task<ArmOutcome> ExecuteAsync(LabelledCase labelledCase, CancellationToken cancellationToken)
    {
        var investigation = await investigator.InvestigateAsync(labelledCase.Text, [], cancellationToken).ConfigureAwait(false);

        switch (investigation.Verdict)
        {
            case GapVerdict.Gap:
            case GapVerdict.KnownGap:
                return new ArmOutcome(CaseSets.GapLabel, null, null, investigation.Outcome);
            case GapVerdict.Supported:
                return new ArmOutcome(CaseSets.NoGapLabel, null, null, investigation.Outcome);
            default:
                // None: "failed" is an error (or a timeout); low-confidence / unusable / off mean
                // "no follow-up", i.e. the turn is treated as not a gap.
                return investigation.Outcome == "failed"
                    ? new ArmOutcome(null, Outcome: investigation.Outcome, Error: investigation.Failure ?? "investigation failed", TimedOut: investigation.TimedOut)
                    : new ArmOutcome(CaseSets.NoGapLabel, null, null, investigation.Outcome);
        }
    }
}

/// <summary>An A/B pair for one track, built so that arm A is structurally unable to reach Jev.</summary>
public sealed record ArmPair(IBenchArm Baseline, IBenchArm WithJev);

/// <summary>
/// Builds the two arms around one shared Foundry gateway. A and B get separate ledgers and separate
/// Jev clients: A's transport throws if touched, B's is the real (or, in unit tests, fake) transport
/// wrapped by the recording handler.
/// </summary>
public static class ArmWiring
{
    public static ArmPair CreateClassifyPair(
        IAiGateway foundry,
        AiGatewayJevOptions jevOptions,
        HttpMessageHandler jevTransport,
        AiGatewayResilienceOptions resilience,
        IBenchClock clock,
        Func<TimeSpan, CancellationToken, Task>? retryDelay = null)
    {
        var ledgerA = new CallLedger();
        var baseline = new ClassifyArm(
            BenchArms.Baseline, new MeteringAiGateway(foundry, ledgerA, clock), DisabledCopy(jevOptions), false, ledgerA, clock);

        var ledgerB = new CallLedger();
        var jevOn = EnabledCopy(jevOptions);
        var jevClient = BuildJevHttpClient(jevOn, jevTransport, ledgerB, resilience, clock, retryDelay);
        var gateway = new JevAiGateway(
            new MeteringAiGateway(foundry, ledgerB, clock),
            new JevClassifyClient(jevClient, jevOn, SystemClock.Instance),
            jevOn);
        var withJev = new ClassifyArm(BenchArms.WithJev, gateway, jevOn, true, ledgerB, clock);

        return new ArmPair(baseline, withJev);
    }

    public static ArmPair CreateGapPair(
        IAiGateway foundry,
        AiGatewayJevOptions jevOptions,
        GapInvestigationOptions investigationOptions,
        HttpMessageHandler jevTransport,
        AiGatewayResilienceOptions resilience,
        IBenchClock clock,
        Func<TimeSpan, CancellationToken, Task>? retryDelay = null)
    {
        var ledgerA = new CallLedger();
        var jevOff = DisabledCopy(jevOptions);
        var baselineInvestigator = new CapabilityInvestigator(
            new MeteringAiGateway(foundry, ledgerA, clock),
            investigationOptions,
            jevOff,
            new JevVerdictClient(
                BuildJevHttpClient(jevOff, new ForbiddenJevTransport(), ledgerA, resilience, clock, retryDelay),
                jevOff,
                SystemClock.Instance));
        var baseline = new GapVerdictArm(BenchArms.Baseline, baselineInvestigator, false, ledgerA, clock);

        var ledgerB = new CallLedger();
        var jevOn = EnabledCopy(jevOptions);
        var jevInvestigator = new CapabilityInvestigator(
            new MeteringAiGateway(foundry, ledgerB, clock),
            investigationOptions,
            jevOn,
            new JevVerdictClient(
                BuildJevHttpClient(jevOn, jevTransport, ledgerB, resilience, clock, retryDelay),
                jevOn,
                SystemClock.Instance));
        var withJev = new GapVerdictArm(BenchArms.WithJev, jevInvestigator, true, ledgerB, clock);

        return new ArmPair(baseline, withJev);
    }

    private static JevHttpJsonClient BuildJevHttpClient(
        AiGatewayJevOptions options,
        HttpMessageHandler transport,
        CallLedger ledger,
        AiGatewayResilienceOptions resilience,
        IBenchClock clock,
        Func<TimeSpan, CancellationToken, Task>? retryDelay) =>
        new(
            new HttpClient(new RecordingJevHandler(transport, ledger, clock))
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(1, resilience.RequestTimeoutSeconds)),
            },
            options,
            new FoundryRetryPolicy(resilience, retryDelay));

    /// <summary>The Jev options with only <c>Enabled</c> forced; every other value is the one configured for the environment.</summary>
    public static AiGatewayJevOptions EnabledCopy(AiGatewayJevOptions source) => Copy(source, enabled: true);

    public static AiGatewayJevOptions DisabledCopy(AiGatewayJevOptions source) => Copy(source, enabled: false);

    private static AiGatewayJevOptions Copy(AiGatewayJevOptions source, bool enabled) => new()
    {
        Enabled = enabled,
        ApiKey = source.ApiKey,
        Endpoint = source.Endpoint,
        Model = source.Model,
        HttpReferer = source.HttpReferer,
        AppTitle = source.AppTitle,
        ClassifyMinConfidence = source.ClassifyMinConfidence,
        CheckOptionOrder = source.CheckOptionOrder,
        ClassifyMaxInputChars = source.ClassifyMaxInputChars,
    };
}
