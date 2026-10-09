using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Jev;
using Raffa.SharedKernel;

namespace Raffa.Chat.Application.Gaps;

/// <summary>One Jev decision for <see cref="CapabilityInvestigator"/>: which operation the turn asks
/// for, how sure Jev is, and -- only when no listed operation fits -- which capability comes
/// closest. <see cref="Confidence"/> is TypeSafe's own <c>confidence</c> on the operation Choice
/// (the lower of the two when <see cref="AiGatewayJevOptions.CheckOptionOrder"/> asked it in both
/// orders); <see cref="OrderConsistent"/> is false when the two orders named different
/// operations.</summary>
public sealed record JevVerdictAnswer(
    string Operation,
    double Confidence,
    bool OrderConsistent,
    string? NearestCapabilityKey,
    AiCallMetadata Metadata);

/// <summary>
/// Asks Jev the one classification decision <see cref="CapabilityInvestigator"/> needs. The user's
/// turn is the whole <c>state</c> (question and language, nothing else: TypeSafe's accuracy falls as
/// the state fills with detail the question does not need); everything Jev must compare it against
/// lives in the Choice options themselves.
///
/// <para>One Choice, not a four-way "verdict": the options are the operations -- <c>question</c> (an
/// ordinary question Ask answers), one <c>capability:&lt;key&gt;</c> per thing Raffa already does,
/// one <c>known-gap:&lt;key&gt;</c> per operation it knows it does not do, and <c>none</c> (nothing
/// above fits) -- so the model only ever answers "which of these is the user asking for" and the
/// verdict (supported / known-gap / gap) is derived in code from the option's prefix. The earlier
/// design asked for the verdict directly while the options overlapped (a known gap and a new gap
/// are both "not supported") and needed catalog membership as a second hop, which is the
/// double-judgement-in-one-question and indirection shape TypeSafe's Jev 1.13 notes warn about
/// (https://docs.typesafe.ai/model-jaggedness/jev-1.13).</para>
///
/// <para>A second Choice -- the nearest capability -- rides in the same request and is ignored
/// unless the operation is <c>none</c> ("speculative fan-out",
/// https://docs.typesafe.ai/patterns/fan-out). With <see cref="AiGatewayJevOptions.CheckOptionOrder"/>
/// the operation Choice is also asked with its options reversed.</para>
///
/// <para>The <c>gap</c> verdict's free-text feature description still needs the Foundry `analyst`
/// role -- Jev's choice/noul/score primitives cannot write it; <see cref="CapabilityInvestigator"/>
/// calls that separately and keeps Jev's decision either way.</para>
/// </summary>
public sealed class JevVerdictClient(JevHttpJsonClient httpClient, AiGatewayJevOptions options, IClock clock)
{
    public const string OperationQuestion = "question";
    public const string OperationNone = "none";
    public const string CapabilityPrefix = "capability:";
    public const string KnownGapPrefix = "known-gap:";

    private const string OperationKey = "operation";
    private const string ReversedOperationKey = "operationReversed";
    private const string NearestCapabilityQuestionKey = "nearestCapability";

    /// <summary>Bump when the question text or option shape below changes -- recorded in every
    /// <see cref="AiCallMetadata.PromptVersion"/> this client produces.</summary>
    public const string Version = "jev-gaps-v2";

    private const string OperationInstructions =
        "Which of these is the user asking Raffa to do? Choose the one option that describes the " +
        "request in `question`; choose `none` only when no other option describes it.";

    /// <param name="question">The user's message, as typed.</param>
    /// <param name="language">The language <see cref="CapabilityInvestigator"/> detected.</param>
    /// <param name="operationCriteria">Operation key -> description, in the order to present them.
    /// Must contain <see cref="OperationQuestion"/> and <see cref="OperationNone"/>.</param>
    /// <param name="nearestCapabilityCriteria">Capability key (plus <c>"ask"</c>) -> title. The
    /// question is omitted from the request when empty.</param>
    public async Task<Result<JevVerdictAnswer>> DecideAsync(
        string question,
        string language,
        IReadOnlyDictionary<string, string> operationCriteria,
        IReadOnlyDictionary<string, string> nearestCapabilityCriteria,
        CancellationToken cancellationToken)
    {
        var questions = new Dictionary<string, JevQuestion>
        {
            [OperationKey] = new("choice", OperationInstructions, operationCriteria),
        };

        if (options.CheckOptionOrder)
        {
            questions[ReversedOperationKey] =
                new("choice", OperationInstructions, JevOptionOrder.Reversed(operationCriteria));
        }

        if (nearestCapabilityCriteria.Count > 0)
        {
            questions[NearestCapabilityQuestionKey] = new(
                "choice",
                "Which existing capability comes closest to what the user asks for in `question` " +
                "(or `ask` if the closest thing is an answer in the chat)?",
                nearestCapabilityCriteria);
        }

        // The user's turn only: no catalog in the state (it is in the options already).
        var state = new Dictionary<string, string> { ["question"] = question, ["language"] = language };
        var wireRequest = new JevSystemOneRequest(options.Model, state, questions);

        var sent = await httpClient
            .PostAsync<JevSystemOneRequest, JevSystemOneResponse>(options.Endpoint, wireRequest, cancellationToken)
            .ConfigureAwait(false);

        if (sent.IsFailure)
        {
            return Result<JevVerdictAnswer>.Failure(sent.Error);
        }

        httpClient.LogDecisions("gaps", sent.Value);

        if (!JevAnswers.TryReadChoice(sent.Value.Answers, OperationKey, out var operation))
        {
            return Result<JevVerdictAnswer>.Failure(
                $"Jev capability-investigator response carried no usable '{OperationKey}' answer. " +
                "A Choice answer is {type, choice, probabilities, confidence} -- " +
                "https://docs.typesafe.ai/api#choice-answer.");
        }

        var confidence = operation!.Confidence;
        var consistent = true;
        if (options.CheckOptionOrder)
        {
            if (JevAnswers.TryReadChoice(sent.Value.Answers, ReversedOperationKey, out var reversed) &&
                string.Equals(reversed!.Choice, operation.Choice, StringComparison.Ordinal))
            {
                confidence = Math.Min(confidence, reversed.Confidence);
            }
            else
            {
                consistent = false;
                confidence = 0;
            }
        }

        string? nearest = null;
        if (JevAnswers.TryReadChoice(sent.Value.Answers, NearestCapabilityQuestionKey, out var nearestChoice))
        {
            nearest = nearestChoice!.Choice;
        }

        return Result<JevVerdictAnswer>.Success(new JevVerdictAnswer(
            operation.Choice,
            confidence,
            consistent,
            nearest,
            JevAnswers.Metadata(options.Model, sent.Value, Version, clock, $"{language}\n{question}")));
    }
}
