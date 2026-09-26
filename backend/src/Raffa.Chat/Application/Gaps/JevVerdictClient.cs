using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Foundry;
using Raffa.AiGateway.Jev;
using Raffa.SharedKernel;

namespace Raffa.Chat.Application.Gaps;

/// <summary>One Jev decision for <see cref="CapabilityInvestigator"/>: the verdict (a real
/// classification choice) plus, when relevant, which known-gap or capability key it named.
/// <see cref="Confidence"/> is Jev's own calibrated probability for the verdict question --
/// bucketed into the existing high/medium/low vocabulary by the caller
/// (<see cref="GapInvestigationOptions"/>'s own doc comment on why that bucketing is a starting
/// point, not a measured calibration).</summary>
public sealed record JevVerdictAnswer(
    string Verdict,
    double Confidence,
    string? KnownGapKey,
    string? NearestCapabilityKey,
    AiCallMetadata Metadata);

/// <summary>
/// Asks Jev the one real classification decision <see cref="CapabilityInvestigator"/> needs --
/// verdict (question/supported/known-gap/gap), and, alongside it in the same request, which
/// known-gap or capability key the turn names -- as up to three Jev "choice" questions over the
/// same <c>state</c> the Foundry `analyst` path already builds
/// (<c>CapabilityInvestigator.InvestigatorInput</c>, serialized once and reused as-is). This is
/// the "never delegate a classification decision to the LLM" half of ADR-031's investigator; the
/// <c>gap</c> verdict's free-text feature description still needs the Foundry `analyst` role --
/// Jev's choice/noul/score primitives cannot write it (see <c>AiGatewayJevOptions</c>'s own doc
/// comment) -- <see cref="CapabilityInvestigator"/> calls that separately, only when Jev says
/// <c>gap</c>, and keeps Jev's own verdict/confidence for the decision either way.
/// </summary>
public sealed class JevVerdictClient(JevHttpJsonClient httpClient, AiGatewayJevOptions options, IClock clock)
{
    private const string VerdictKey = "verdict";
    private const string KnownGapKey = "knownGapKey";
    private const string NearestCapabilityKey = "nearestCapabilityKey";

    /// <summary>Bump when the question text/criteria shape below changes -- recorded in every
    /// <see cref="AiCallMetadata.PromptVersion"/> this client produces.</summary>
    public const string Version = "jev-gaps-v1";

    /// <param name="stateJson">The same serialized <c>InvestigatorInput</c>
    /// (question/language/capabilities/askAbilities/knownGaps) the Foundry path builds -- Jev's
    /// <c>state</c> is exactly this, not a second copy.</param>
    /// <param name="verdictCriteria">Verdict key -> one-line description (the four fixed
    /// verdicts).</param>
    /// <param name="knownGapCriteria">Known-gap key -> operation description. Omitted from the
    /// request entirely when empty (a tenant/catalog with no known gaps at all).</param>
    /// <param name="nearestCapabilityCriteria">Capability key (plus <c>"ask"</c>) -> title.
    /// Omitted from the request entirely when empty.</param>
    public async Task<Result<JevVerdictAnswer>> DecideAsync(
        string stateJson,
        IReadOnlyDictionary<string, string> verdictCriteria,
        IReadOnlyDictionary<string, string> knownGapCriteria,
        IReadOnlyDictionary<string, string> nearestCapabilityCriteria,
        CancellationToken cancellationToken)
    {
        var questions = new Dictionary<string, JevQuestion>
        {
            [VerdictKey] = new(
                Type: "choice",
                Instructions:
                    "Decide what the user is asking Raffa to do, against the capabilities, " +
                    "askAbilities and knownGaps in the state.",
                Criteria: verdictCriteria),
        };

        if (knownGapCriteria.Count > 0)
        {
            questions[KnownGapKey] = new(
                Type: "choice",
                Instructions: "If the verdict is known-gap, which operation is it? Ignored otherwise.",
                Criteria: knownGapCriteria);
        }

        if (nearestCapabilityCriteria.Count > 0)
        {
            questions[NearestCapabilityKey] = new(
                Type: "choice",
                Instructions:
                    "If the verdict is gap, which existing capability comes closest to what the " +
                    "user wanted (or 'ask' if the closest thing is an answer in the chat)? Ignored " +
                    "otherwise.",
                Criteria: nearestCapabilityCriteria);
        }

        var wireRequest = new JevSystemOneRequest(options.Model, stateJson, questions);

        var sent = await httpClient
            .PostAsync<JevSystemOneRequest, JevSystemOneResponse>(options.Endpoint, wireRequest, cancellationToken)
            .ConfigureAwait(false);

        if (sent.IsFailure)
        {
            return Result<JevVerdictAnswer>.Failure(sent.Error);
        }

        var answers = sent.Value.Answers;
        if (answers is null || !answers.TryGetValue(VerdictKey, out var verdictAnswer) ||
            !JevAnswerReader.TryReadChoice(verdictAnswer, out var verdict, out var confidence))
        {
            return Result<JevVerdictAnswer>.Failure(
                $"Jev capability-investigator response carried no usable '{VerdictKey}' answer. " +
                "This client's response contract is unverified against a live account -- see " +
                "AiGatewayJevOptions's own doc comment.");
        }

        string? knownGapAnswer = null;
        if (answers.TryGetValue(KnownGapKey, out var knownGapElement) &&
            JevAnswerReader.TryReadChoice(knownGapElement, out var knownGapChoice, out _))
        {
            knownGapAnswer = knownGapChoice;
        }

        string? nearestCapabilityAnswer = null;
        if (answers.TryGetValue(NearestCapabilityKey, out var nearestElement) &&
            JevAnswerReader.TryReadChoice(nearestElement, out var nearestChoice, out _))
        {
            nearestCapabilityAnswer = nearestChoice;
        }

        var model = new AiModelSelection(options.Model, options.Model);
        var metadata = FoundryCallMetadataFactory.Build(model, Version, clock, stateJson);

        return Result<JevVerdictAnswer>.Success(new JevVerdictAnswer(
            verdict!, Math.Clamp(confidence, 0, 1), knownGapAnswer, nearestCapabilityAnswer, metadata));
    }
}
