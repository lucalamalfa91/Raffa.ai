namespace Raffa.AiGateway.Agents;

/// <summary>
/// A reusable, deterministic check on one agent output (plan 3.1/3.2 and A-03): it knows only the
/// output, so the same verifier serves every step that produces that type. Synchronous, no model
/// call, no state. A verifier that also needs the step's input (the pack the output must stay
/// grounded in) implements <see cref="IStepVerifier{TIn,TOut}"/> directly, or is built with
/// <see cref="Verifier.From{TIn,TOut}"/>.
/// </summary>
public interface IVerifier<in TOut>
    where TOut : class
{
    /// <summary>Stable name, for messages and the "verifier intervened" metric.</summary>
    string Name { get; }

    /// <summary>Accepts the output or names every violation (never empty when rejected).</summary>
    StepVerdict Verify(TOut output);
}

/// <summary>Composition and adaptation of verifiers onto the runner's <see cref="IStepVerifier{TIn,TOut}"/> seam.</summary>
public static class Verifier
{
    /// <summary>A verifier from a delegate over the step's input and output. The retry instruction is
    /// <see cref="AgentDefinition.DefaultRetryInstruction"/> unless <paramref name="retryInstruction"/> is given.</summary>
    public static IStepVerifier<TIn, TOut> From<TIn, TOut>(
        string name, Func<TIn, TOut, StepVerdict> verify, Func<StepVerdict, string>? retryInstruction = null)
        where TIn : notnull
        where TOut : class =>
        new DelegateVerifier<TIn, TOut>(name, verify, retryInstruction);

    /// <summary>Lifts an output-only <see cref="IVerifier{TOut}"/> to the step seam.</summary>
    public static IStepVerifier<TIn, TOut> ForStep<TIn, TOut>(
        IVerifier<TOut> verifier, Func<StepVerdict, string>? retryInstruction = null)
        where TIn : notnull
        where TOut : class
    {
        ArgumentNullException.ThrowIfNull(verifier);
        return new DelegateVerifier<TIn, TOut>(verifier.Name, (_, output) => verifier.Verify(output), retryInstruction);
    }

    /// <summary>
    /// All of <paramref name="verifiers"/>, in order, as one. <paramref name="stopAtFirst"/> keeps the
    /// first rejecting verifier's violations only (the guards' "first violation found" semantics: the
    /// retry names one thing); otherwise the violations of every rejecting verifier are collected. The
    /// retry instruction is <paramref name="retryInstruction"/> or the default one naming the violations.
    /// </summary>
    public static IStepVerifier<TIn, TOut> AllOf<TIn, TOut>(
        string name, bool stopAtFirst, Func<StepVerdict, string>? retryInstruction, params IStepVerifier<TIn, TOut>[] verifiers)
        where TIn : notnull
        where TOut : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(verifiers);
        return new CompositeVerifier<TIn, TOut>(name, stopAtFirst, retryInstruction, verifiers);
    }

    private sealed class DelegateVerifier<TIn, TOut>(
        string name, Func<TIn, TOut, StepVerdict> verify, Func<StepVerdict, string>? retry) : IStepVerifier<TIn, TOut>
        where TIn : notnull
        where TOut : class
    {
        public string Name { get; } = !string.IsNullOrWhiteSpace(name)
            ? name
            : throw new ArgumentException("A verifier needs a name.", nameof(name));

        public StepVerdict Verify(TIn input, TOut output) => verify(input, output);

        public string RetryInstruction(StepVerdict verdict) =>
            retry is not null
                ? retry(verdict)
                : string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    AgentDefinition.DefaultRetryInstruction,
                    string.Join(' ', verdict.Violations));
    }

    private sealed class CompositeVerifier<TIn, TOut>(
        string name, bool stopAtFirst, Func<StepVerdict, string>? retry, IStepVerifier<TIn, TOut>[] verifiers)
        : IStepVerifier<TIn, TOut>
        where TIn : notnull
        where TOut : class
    {
        public string Name { get; } = name;

        public StepVerdict Verify(TIn input, TOut output)
        {
            var violations = new List<string>();
            foreach (var verifier in verifiers)
            {
                var verdict = verifier.Verify(input, output);
                if (verdict.Ok)
                {
                    continue;
                }

                violations.AddRange(verdict.Violations);
                if (stopAtFirst)
                {
                    break;
                }
            }

            return violations.Count == 0 ? StepVerdict.Pass : new StepVerdict(false, violations);
        }

        public string RetryInstruction(StepVerdict verdict) =>
            retry is not null
                ? retry(verdict)
                : string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    AgentDefinition.DefaultRetryInstruction,
                    string.Join(' ', verdict.Violations));
    }
}
