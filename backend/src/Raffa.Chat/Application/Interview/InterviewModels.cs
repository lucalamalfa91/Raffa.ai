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

/// <remarks>Equality compares <see cref="Options"/> element by element, not by list reference, so
/// a question decoded from <c>interview_json</c> equals the one that was serialized.</remarks>
public sealed record InterviewQuestion(
    string Key,
    string Prompt,
    InterviewPresentation Presentation,
    bool AllowFreeText,
    IReadOnlyList<InterviewOption> Options)
{
    public bool Equals(InterviewQuestion? other) =>
        other is not null &&
        string.Equals(Key, other.Key, StringComparison.Ordinal) &&
        string.Equals(Prompt, other.Prompt, StringComparison.Ordinal) &&
        Presentation == other.Presentation &&
        AllowFreeText == other.AllowFreeText &&
        Options.SequenceEqual(other.Options);

    public override int GetHashCode() => HashCode.Combine(Key, Prompt, Presentation, AllowFreeText, Options.Count);
}

/// <remarks>Equality compares <see cref="Questions"/> element by element (see
/// <see cref="InterviewQuestion"/>).</remarks>
public sealed record InterviewTurn(string Prompt, IReadOnlyList<InterviewQuestion> Questions)
{
    public bool Equals(InterviewTurn? other) =>
        other is not null &&
        string.Equals(Prompt, other.Prompt, StringComparison.Ordinal) &&
        Questions.SequenceEqual(other.Questions);

    public override int GetHashCode() => HashCode.Combine(Prompt, Questions.Count);
}

/// <summary>The client's reply to an interview: which message, which question, and either an
/// option key or free text (the typed question itself).</summary>
public sealed record InterviewAnswer(EntityId MessageId, string QuestionKey, string? OptionKey, bool FreeText);
