using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Raffa.AiGateway.Foundry;

namespace Raffa.AiGateway.Agents;

/// <summary>Which <see cref="IAiGateway"/> entry point a step calls. Both are the existing roles; the
/// runner adds no role of its own and never reaches a provider except through the gateway.</summary>
public enum AgentRole
{
    /// <summary><see cref="IAiGateway.AnalyzeAsync"/>: strict JSON over caller-supplied evidence.</summary>
    Analyze = 0,

    /// <summary><see cref="IAiGateway.AnswerAsync"/>: the grounded answer role.</summary>
    Answer = 1,
}

/// <summary>What the runner does when a step cannot produce an output (gateway failure, unusable
/// payload, verifier still unhappy after its one retry, or the deadline). It is the step's own
/// declaration of how much the flow depends on it.</summary>
public enum AgentFailurePolicy
{
    /// <summary>The step is required: the result is <see cref="StepStatus.Failed"/> and the flow decides
    /// (the council's strategist: no plays means no council for the turn).</summary>
    Fail = 0,

    /// <summary>The step is optional: the flow goes on without it, and the result is an explicit
    /// <see cref="StepStatus.Skipped"/> carrying why (the council's analysts: a failed analyst
    /// degrades the council, never the turn).</summary>
    Skip = 1,

    /// <summary>The step has a deterministic stand-in (<see cref="IAgentStep{TIn,TOut}.Fallback"/>): the
    /// result is <see cref="StepStatus.FellBack"/> with that value (the drafting writer's template).</summary>
    Fallback = 2,
}

/// <summary>
/// The immutable, Singleton-safe description of one agent (plan 3.2): its name, version, prompt,
/// the strict JSON Schema its output is held to, its deadline, what happens when it
/// cannot deliver, and whether a verifier may send it back once with the violation named. It holds no
/// state and no service, so one instance is shared by every request.
///
/// <para>
/// The prompt is a versioned file (<c>Prompts/&lt;flow&gt;/&lt;agent&gt;/vN.md</c>), read through
/// <see cref="AgentPromptResource"/>; <see cref="RegisteredPromptHash"/> is the hash committed in
/// code next to the version. <see cref="Validate"/> is the drift test every definition must pass: the
/// schema is accepted by <see cref="StrictJsonSchemaValidator"/> (an Azure strict-mode schema is
/// refused with an opaque 400 on every call otherwise), and the prompt in use still hashes to the
/// registered value, so a prompt edited without a version bump fails a test, not a golden run.
/// </para>
/// </summary>
public sealed partial class AgentDefinition
{
    /// <summary>Appended to the system prompt on a verifier retry; <c>{0}</c> is the violation. A step
    /// whose flow wants other wording supplies it through <see cref="IStepVerifier{TIn,TOut}.RetryInstruction"/>.</summary>
    public const string DefaultRetryInstruction =
        "Your previous output was rejected: {0} Return the same strict JSON again, fixing exactly this and nothing else.";

    public AgentDefinition(
        string name,
        string version,
        string prompt,
        string jsonSchema,
        TimeSpan deadline,
        AgentFailurePolicy failurePolicy = AgentFailurePolicy.Fail,
        AgentRole role = AgentRole.Analyze,
        string? registeredPromptHash = null,
        string? promptResource = null,
        string? languageDirective = null,
        bool retryOnVerifierViolation = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(jsonSchema);

        if (!NamePattern().IsMatch(name))
        {
            // The name is the strict-schema name Azure sees and a token in audit lines and span tags.
            throw new ArgumentException(
                $"Agent name '{name}' may only contain letters, digits, '-' and '_'.", nameof(name));
        }

        if (deadline <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(deadline), deadline, "An agent deadline must be positive.");
        }

        Name = name;
        Version = version;
        Prompt = prompt;
        JsonSchema = jsonSchema;
        Deadline = deadline;
        FailurePolicy = failurePolicy;
        Role = role;
        RegisteredPromptHash = registeredPromptHash;
        PromptResource = promptResource;
        LanguageDirective = string.IsNullOrWhiteSpace(languageDirective) ? null : languageDirective;
        RetryOnVerifierViolation = retryOnVerifierViolation;
        PromptHash = HashPrompt(prompt);
    }

    /// <summary>A definition whose schema is derived from <typeparamref name="TOut"/> (<see cref="AgentSchema"/>).</summary>
    public static AgentDefinition ForOutput<TOut>(
        string name,
        string version,
        string prompt,
        TimeSpan deadline,
        AgentFailurePolicy failurePolicy = AgentFailurePolicy.Fail,
        string? registeredPromptHash = null,
        string? promptResource = null,
        string? languageDirective = null,
        bool retryOnVerifierViolation = true) =>
        new(name, version, prompt, AgentSchema.For<TOut>(), deadline, failurePolicy, AgentRole.Analyze,
            registeredPromptHash, promptResource, languageDirective, retryOnVerifierViolation);

    /// <summary>Stable agent identity: the audit <c>agent=</c> and <c>step=</c>, the span name, the
    /// gateway's strict-schema name. Letters, digits, '-' and '_' only.</summary>
    public string Name { get; }

    /// <summary>The prompt version tag (<c>council-v3</c>) echoed onto the call's metadata.</summary>
    public string Version { get; }

    /// <summary>The agent's own persona and rules, exactly as read from its prompt file.</summary>
    public string Prompt { get; }

    /// <summary>SHA-256 (hex, lower case) of <see cref="Prompt"/> with line endings and trailing
    /// whitespace normalized, so a checkout on another platform hashes the same.</summary>
    public string PromptHash { get; }

    /// <summary>The hash committed in code next to <see cref="Version"/>; <see langword="null"/> while
    /// the prompt has no registered hash yet (then <see cref="Validate"/> has nothing to compare).</summary>
    public string? RegisteredPromptHash { get; }

    /// <summary>Where the prompt lives (<c>Prompts/council/contract-analyst/v3.md</c>); informational,
    /// for messages and the registry.</summary>
    public string? PromptResource { get; }

    /// <summary>The strict JSON Schema the response must match. Unused (empty) for <see cref="AgentRole.Answer"/>.</summary>
    public string JsonSchema { get; }

    /// <summary>How long the step may take in all, a verifier retry included. A step that overruns is
    /// cancelled and degrades by its <see cref="FailurePolicy"/> with an explicit "deadline" reason,
    /// never a silent truncation.</summary>
    public TimeSpan Deadline { get; }

    public AgentFailurePolicy FailurePolicy { get; }

    public AgentRole Role { get; }

    /// <summary>Appended to the prompt for agents that write text for the user, so the answer comes in
    /// the language of the question (plan LANG-04). Versioned with the prompt; <see langword="null"/>
    /// for agents whose output is not prose.</summary>
    public string? LanguageDirective { get; }

    /// <summary>Whether a verifier violation buys one more attempt with the violation named (at most
    /// one, the generalization of <c>RegenerateOnce</c>); <see langword="false"/> rejects at once.</summary>
    public bool RetryOnVerifierViolation { get; }

    /// <summary>The system prompt actually sent: <see cref="Prompt"/>, then the language directive.</summary>
    public string SystemPrompt =>
        LanguageDirective is null ? Prompt : Prompt + Environment.NewLine + Environment.NewLine + LanguageDirective;

    /// <summary>
    /// The problems that would make this definition fail in production or drift silently, empty when
    /// it is sound: the schema is not accepted by <see cref="StrictJsonSchemaValidator"/> (analyst
    /// role), the prompt no longer hashes to <see cref="RegisteredPromptHash"/>, a hash that is not a
    /// SHA-256 hex string. Every definition has a test that asserts this is empty.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (Role == AgentRole.Analyze)
        {
            var schema = StrictJsonSchemaValidator.Validate(JsonSchema);
            if (schema.IsFailure)
            {
                problems.Add($"{Name}: {schema.Error}");
            }
        }

        if (RegisteredPromptHash is not null)
        {
            if (!HashPattern().IsMatch(RegisteredPromptHash))
            {
                problems.Add($"{Name}: the registered prompt hash is not a SHA-256 hex string.");
            }
            else if (!string.Equals(RegisteredPromptHash, PromptHash, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(
                    $"{Name}: prompt drift -- {PromptResource ?? "the prompt"} hashes to {PromptHash} but " +
                    $"{RegisteredPromptHash} is registered for version {Version}. Bump the version and register the new hash.");
            }
        }

        return problems;
    }

    /// <summary>The hash <see cref="RegisteredPromptHash"/> must hold for <paramref name="prompt"/>.</summary>
    public static string HashPrompt(string prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AgentPromptResource.Normalize(prompt))))
            .ToLowerInvariant();
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex NamePattern();

    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex HashPattern();
}
