using Raffa.Chat.Domain;
using Raffa.SharedKernel;

namespace Raffa.Chat.Application.Interview;

/// <summary>
/// The interview (ADR-030): when a question is ambiguous, Raffa asks one short question with
/// clickable options before it retrieves anything. Every option carries a server-authored
/// <see cref="InterviewResolution"/> — the rewritten question and the forced intent/contract the
/// normal pipeline runs on once the user picks it. The resolution is persisted with the turn and
/// never sent to the client; the client answers by key (<see cref="InterviewAnswer"/>), never by
/// label, so a tampered label can never change what Raffa does.
/// </summary>
public enum InterviewPresentation
{
    /// <summary>A choice among readings/contracts, rendered as chips.</summary>
    Choice,

    /// <summary>A yes/no authorization the SPA renders as an alert dialog (web research).</summary>
    Consent,
}

/// <summary>What an interview option asks the web-research role to do, once the user consents
/// (ADR-030). The query is authored by the server (<c>WebQuerySanitizer</c>), never by the client
/// or the model.</summary>
public sealed record WebResearchRequest(string Query, string Purpose);

public sealed record InterviewResolution(
    AskIntent? Intent,
    string? ContractId,
    string? SupplierName,
    string RewrittenQuestion,
    WebResearchRequest? WebResearch = null);

public sealed record InterviewOption(string Key, string Label, string? Hint, InterviewResolution ResolvesTo);

public sealed record InterviewQuestion(
    string Key,
    string Prompt,
    InterviewPresentation Presentation,
    bool AllowFreeText,
    IReadOnlyList<InterviewOption> Options);

public sealed record InterviewTurn(string Prompt, IReadOnlyList<InterviewQuestion> Questions);

/// <summary>The client's reply to an interview: which message, which question, and either an
/// option key or free text (the typed question itself).</summary>
public sealed record InterviewAnswer(EntityId MessageId, string QuestionKey, string? OptionKey, bool FreeText);

/// <summary>
/// What an interview resolution forces on the next turn. <see cref="SuppressInterview"/> is what
/// stops an interview from ever answering an interview; <see cref="ForcedIntent"/> bypasses the
/// planner's lexicons; <see cref="ForcedContractId"/> narrows the turn to one contract exactly as
/// a scoped conversation would (an id this tenant cannot see refuses, never silently widens).
/// <see cref="AuthorizedWebResearch"/> is set only by a consumed consent option (ADR-030);
/// <see cref="DeclinedWebResearch"/> only by the consent's "no" option, so the turn is audited as
/// a decline while it runs the normal, contracts-only pipeline.
/// </summary>
public sealed record AskTurnHints(
    AskIntent? ForcedIntent,
    EntityId? ForcedContractId,
    string? ForcedSupplierName,
    bool SuppressInterview,
    WebResearchRequest? AuthorizedWebResearch = null,
    bool DeclinedWebResearch = false)
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
