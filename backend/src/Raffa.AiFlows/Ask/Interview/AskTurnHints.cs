using Raffa.Chat.Application.Interview;
using Raffa.Chat.Domain;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Ask.Interview;

/// <summary>
/// What an interview resolution forces on the next turn. <see cref="SuppressInterview"/> is what
/// stops an interview from ever answering an interview; <see cref="ForcedIntent"/> bypasses the
/// planner's lexicons; <see cref="ForcedContractId"/> narrows the turn to one contract exactly as
/// a scoped conversation would (an id this tenant cannot see refuses, never silently widens).
/// <see cref="AuthorizedWebResearch"/> is set only by a consumed consent option (ADR-030);
/// <see cref="DeclinedWebResearch"/> only by the consent's "no" option, so the turn is audited as
/// a decline while it runs the normal, contracts-only pipeline. <see cref="WebMode"/> is the
/// composer's web-search toggle (ADR-032): set by the endpoint from the request, never by an
/// interview resolution, it sends the turn to the web and to Raffa's own store with the
/// procurement-only filters lifted — the toggle itself is the consent.
/// </summary>
public sealed record AskTurnHints(
    AskIntent? ForcedIntent,
    EntityId? ForcedContractId,
    string? ForcedSupplierName,
    bool SuppressInterview,
    WebResearchRequest? AuthorizedWebResearch = null,
    bool DeclinedWebResearch = false,
    bool WebMode = false)
{
    public static AskTurnHints None { get; } = new(null, null, null, false);

    public static AskTurnHints From(InterviewResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        EntityId? contractId = resolution.ContractId is { } text && Guid.TryParse(text, out var guid)
            ? new EntityId(guid)
            : null;

        return new AskTurnHints(
            resolution.Intent,
            contractId,
            resolution.SupplierName,
            SuppressInterview: true,
            resolution.WebResearch);
    }

    /// <summary>The hints for a free-text interview answer: nothing forced, only no second interview.</summary>
    public static AskTurnHints FreeText { get; } = new(null, null, null, true);
}

/// <summary>One contract the interview can offer as an option — display data only, never a guid
/// in the label (R-ASK-08); the id travels in the resolution.</summary>
public sealed record InterviewContractChoice(
    string ContractId,
    string SupplierName,
    string ContractType,
    DateOnly? RenewalDate,
    DateOnly? CancellationDeadline,
    DateOnly? EndDate);

/// <summary>Host-supplied facts the planner needs to phrase a "which contract" question.</summary>
public sealed record InterviewInputs(
    IReadOnlyList<InterviewContractChoice> SupplierContracts,
    IReadOnlyList<InterviewContractChoice> NoticeCandidates)
{
    public static InterviewInputs Empty { get; } = new([], []);
}
