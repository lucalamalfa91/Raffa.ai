namespace Raffa.JevBenchmark.Tests.Harness;

/// <summary>
/// Drives one track: warm-up (real calls whose results are thrown away, so connection setup, token
/// acquisition and JIT do not pollute the latency figures), then <see cref="BenchSettings.Repetitions"/>
/// passes over every case, running both arms on the same case back to back. The order inside a pair
/// alternates, so neither arm systematically goes first (cache or throttling effects hit both).
/// Strictly sequential: parallel calls would measure queueing, not the models.
/// </summary>
public sealed class BenchmarkRunner(BenchSettings settings, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task<IReadOnlyList<CaseObservation>> RunTrackAsync(
        CaseSet caseSet, ArmPair arms, Action<string>? log, CancellationToken cancellationToken)
    {
        var cases = caseSet.Cases;
        if (cases.Count == 0)
        {
            return [];
        }

        for (var i = 0; i < settings.WarmupCalls; i++)
        {
            var warm = cases[i % cases.Count];
            await arms.Baseline.RunAsync(warm, -1, cancellationToken).ConfigureAwait(false);
            await PauseAsync(cancellationToken).ConfigureAwait(false);
            await arms.WithJev.RunAsync(warm, -1, cancellationToken).ConfigureAwait(false);
            await PauseAsync(cancellationToken).ConfigureAwait(false);
        }

        log?.Invoke($"[{caseSet.Track}] warm-up done ({settings.WarmupCalls} call(s) per arm discarded); measuring {cases.Count} case(s) x {settings.Repetitions} rep(s) x 2 arms.");

        var observations = new List<CaseObservation>(cases.Count * settings.Repetitions * 2);
        for (var rep = 0; rep < settings.Repetitions; rep++)
        {
            for (var index = 0; index < cases.Count; index++)
            {
                var current = cases[index];
                var baselineFirst = (rep + index) % 2 == 0;
                var first = baselineFirst ? arms.Baseline : arms.WithJev;
                var second = baselineFirst ? arms.WithJev : arms.Baseline;

                observations.Add(await first.RunAsync(current, rep, cancellationToken).ConfigureAwait(false));
                await PauseAsync(cancellationToken).ConfigureAwait(false);
                observations.Add(await second.RunAsync(current, rep, cancellationToken).ConfigureAwait(false));
                await PauseAsync(cancellationToken).ConfigureAwait(false);
            }

            log?.Invoke($"[{caseSet.Track}] rep {rep + 1}/{settings.Repetitions} done.");
        }

        return observations;
    }

    private Task PauseAsync(CancellationToken cancellationToken) =>
        settings.DelayBetweenCallsMs > 0
            ? _delay(TimeSpan.FromMilliseconds(settings.DelayBetweenCallsMs), cancellationToken)
            : Task.CompletedTask;
}
