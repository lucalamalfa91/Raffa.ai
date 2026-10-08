using Raffa.AiGateway.Contracts;

namespace Raffa.AiGateway.Agents;

/// <summary>
/// One agent with one job (plan 3.1/3.2): an immutable, serializable input record in, an immutable,
/// serializable output record out, an <see cref="AgentDefinition"/> that says how it is called.
/// <typeparamref name="TIn"/> and <typeparamref name="TOut"/> are plain records with no behaviour, no
/// service and no <c>DbContext</c>: the data is read before the step runs and handed to it, so a step
/// can later become a durable activity unchanged (the plan's condition for DTS). Implementations
/// are stateless and Singleton-safe; the runner is the only thing that touches the gateway.
/// </summary>
public interface IAgentStep<TIn, TOut>
    where TIn : notnull
    where TOut : class
{
    AgentDefinition Definition { get; }

    /// <summary>The verifier run on the output before it is accepted; <see langword="null"/> for none.</summary>
    IStepVerifier<TIn, TOut>? Verifier => null;

    /// <summary>The deterministic stand-in when <see cref="AgentDefinition.FailurePolicy"/> is
    /// <see cref="AgentFailurePolicy.Fallback"/> and the step could not deliver; <see langword="null"/>
    /// when there is none (the step then fails).</summary>
    TOut? Fallback(TIn input) => null;
}

/// <summary>A step assembled from a definition (and, optionally, a verifier and a fallback) where a
/// dedicated class would add nothing.</summary>
public sealed class AgentStep<TIn, TOut>(
    AgentDefinition definition,
    IStepVerifier<TIn, TOut>? verifier = null,
    Func<TIn, TOut?>? fallback = null) : IAgentStep<TIn, TOut>
    where TIn : notnull
    where TOut : class
{
    public AgentDefinition Definition { get; } = definition ?? throw new ArgumentNullException(nameof(definition));

    public IStepVerifier<TIn, TOut>? Verifier { get; } = verifier;

    public TOut? Fallback(TIn input) => fallback?.Invoke(input);
}

/// <summary>
/// The check run on a step's output after the step and before it is accepted (plan 3.1: verifiers
/// are separate from the agent that produces). Deterministic code, synchronous, no model call: the
/// guards the flows already have (<c>NumericGuard</c>, <c>DraftGuard</c>, <c>WebFigureGuard</c>) are
/// adapted by implementing this over the step's input (the pack the output must stay grounded in)
/// and output. Plan A-03 builds the reusable verifiers on top of this seam.
/// </summary>
public interface IStepVerifier<TIn, TOut>
    where TIn : notnull
    where TOut : class
{
    /// <summary>Stable name, for messages and the "verifier intervened" count.</summary>
    string Name { get; }

    StepVerdict Verify(TIn input, TOut output);

    /// <summary>What is appended to the system prompt when the output is sent back once with the
    /// violation named; the default is <see cref="AgentDefinition.DefaultRetryInstruction"/>.</summary>
    string RetryInstruction(StepVerdict verdict) =>
        string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            AgentDefinition.DefaultRetryInstruction,
            string.Join(' ', verdict.Violations));
}

/// <summary>The verifier's answer: accepted, or the named violations (never empty when rejected).</summary>
public sealed record StepVerdict(bool Ok, IReadOnlyList<string> Violations)
{
    public static StepVerdict Pass { get; } = new(true, []);

    public static StepVerdict Reject(params string[] violations) =>
        new(false, violations is { Length: > 0 } ? violations : ["The output was rejected."]);
}

/// <summary>The input of an <see cref="AgentRole.Answer"/> step: the question, the pre-retrieved and
/// pre-authorized evidence, and the context pack JSON, as <see cref="AiAnswerRequest"/> carries them.</summary>
public sealed record AnswerStepInput(
    string Question,
    IReadOnlyList<AiEvidenceSnippet> Evidence,
    string? PackJson = null);
