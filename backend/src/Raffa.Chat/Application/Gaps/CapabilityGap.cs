using System.Text.RegularExpressions;

namespace Raffa.Chat.Application.Gaps;

/// <summary>Which existing capability Raffa offers in place of the operation it cannot perform
/// (ADR-030 D1). <see cref="DraftEmail"/> is the one alternative that produces new content — the
/// drafted negotiation email (<c>Application.Drafting</c>); the other three are deep links into
/// screens that already hold the facts the user was after.</summary>
public enum GapAlternative
{
    /// <summary>Write the negotiation/renewal email from the contract's own facts.</summary>
    DraftEmail,

    /// <summary>Renewals already tracks every notice deadline (a reminder's real content).</summary>
    Renewals,

    /// <summary>Portfolio already lists the same data as a filterable table (an export's content).</summary>
    Portfolio,

    /// <summary>Contract 360 holds the facts a purchase order needs.</summary>
    ContractDetail,
}

/// <summary>
/// One operation Ask Raffa is asked to perform but cannot — send an email, set a reminder, export
/// a file, raise a purchase order (ADR-030 D1: "when Raffa cannot perform an operation it says so,
/// offers the nearest alternative and lets the user report the gap"). Every user-facing string
/// exists in both languages the persona answers in (Italian and English, persona law 5); the
/// composition root picks one with <c>Application.Language.QuestionLanguage</c>.
/// </summary>
/// <param name="Key">Stable catalog key — the value a feature request records and a GitHub issue
/// names (never a display string).</param>
/// <param name="TitleEn">Short noun phrase naming the missing feature, English.</param>
/// <param name="TitleIt">Same, Italian.</param>
/// <param name="OperationEn">The verb phrase the preface quotes: "I can't <b>send an email to the
/// supplier</b> from Raffa.ai yet".</param>
/// <param name="OperationIt">Same, Italian ("Al momento non posso <b>inviare un'email al
/// fornitore</b> da Raffa.ai").</param>
/// <param name="AlternativeEn">The clause after "but": "<b>I can help you write the renewal
/// email</b>".</param>
/// <param name="AlternativeIt">Same, Italian.</param>
/// <param name="DescriptionEn">The one-line feature description the feedback card prefills its
/// first question with.</param>
/// <param name="DescriptionIt">Same, Italian.</param>
/// <param name="Alternative">Which alternative the composition root delivers.</param>
/// <param name="Pattern">The it/en lexicon that recognises the request. Matched by
/// <see cref="CapabilityGapCatalog.Match"/> after <paramref name="Exclude"/> is checked.</param>
/// <param name="Exclude">An optional pattern that vetoes the match — a notice-deadline question
/// ("when must we send the notice?") shares the verb "send" with a send-request but is a
/// structured-fact question, not a gap.</param>
public sealed record CapabilityGap(
    string Key,
    string TitleEn,
    string TitleIt,
    string OperationEn,
    string OperationIt,
    string AlternativeEn,
    string AlternativeIt,
    string DescriptionEn,
    string DescriptionIt,
    GapAlternative Alternative,
    Regex Pattern,
    Regex? Exclude = null)
{
    public string Title(string language) => language == Language.QuestionLanguage.Italian ? TitleIt : TitleEn;

    public string Operation(string language) => language == Language.QuestionLanguage.Italian ? OperationIt : OperationEn;

    public string AlternativeText(string language) => language == Language.QuestionLanguage.Italian ? AlternativeIt : AlternativeEn;

    public string Description(string language) => language == Language.QuestionLanguage.Italian ? DescriptionIt : DescriptionEn;

    /// <summary>True when <paramref name="question"/> asks for this operation and no exclusion
    /// vetoes it. Pure, no I/O (Appendix C rule 6).</summary>
    public bool Matches(string question) =>
        Pattern.IsMatch(question) && (Exclude is null || !Exclude.IsMatch(question));
}
