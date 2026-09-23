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

    /// <summary>A gap the capability investigator discovered (ADR-031): the nearest existing
    /// screen it named (<see cref="CapabilityGap.NearestCapabilityKey"/>), or no screen at all
    /// when the nearest thing is Ask itself.</summary>
    NearestCapability,
}

/// <summary>Where a <see cref="CapabilityGap"/> came from (ADR-031).</summary>
public enum GapOrigin
{
    /// <summary>One of the fixed <see cref="CapabilityGapCatalog"/> entries, matched by regex.</summary>
    Catalog,

    /// <summary>Found by <see cref="CapabilityInvestigator"/>: a model judged that the turn asks
    /// for an operation nothing in Raffa performs, and described it as a generic backlog item.</summary>
    Investigator,
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
/// <see cref="CapabilityGapCatalog.Match"/> after <paramref name="Exclude"/> is checked.
/// <see langword="null"/> for a gap the investigator discovered (ADR-031): nothing matches it by
/// regex, it exists for the one turn that found it.</param>
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
    Regex? Pattern,
    Regex? Exclude = null)
{
    /// <summary>The prefix of every discovered gap's <see cref="Key"/> — what tells a stored
    /// feature request, an audit row or an issue apart from a catalog key.</summary>
    public const string DiscoveredKeyPrefix = "discovered:";

    /// <summary><see cref="GapOrigin.Catalog"/> for the fixed entries; <see cref="GapOrigin.Investigator"/>
    /// for a gap built by <see cref="Discovered"/>.</summary>
    public GapOrigin Origin { get; init; } = GapOrigin.Catalog;

    /// <summary>Only for a discovered gap: what the investigator found, carried onto the reply's
    /// <c>payload.gap.discovery</c> and from there into the GitHub issue (ADR-031).</summary>
    public Reply.GapDiscovery? Discovery { get; init; }

    /// <summary>The existing screen the investigator named as the nearest alternative, when it is
    /// one the reply can link to; <see langword="null"/> for a catalog gap.</summary>
    public string? NearestCapabilityKey => Discovery?.NearestCapability;

    public string Title(string language) => language == Language.QuestionLanguage.Italian ? TitleIt : TitleEn;

    public string Operation(string language) => language == Language.QuestionLanguage.Italian ? OperationIt : OperationEn;

    public string AlternativeText(string language) => language == Language.QuestionLanguage.Italian ? AlternativeIt : AlternativeEn;

    public string Description(string language) => language == Language.QuestionLanguage.Italian ? DescriptionIt : DescriptionEn;

    /// <summary>True when <paramref name="question"/> asks for this operation and no exclusion
    /// vetoes it. Pure, no I/O (Appendix C rule 6). Always false for a discovered gap.</summary>
    public bool Matches(string question) =>
        Pattern is not null && Pattern.IsMatch(question) && !IsVetoed(question);

    /// <summary>True when <see cref="Exclude"/> vetoes <paramref name="question"/> — also checked
    /// when the investigator (not the regex) names a catalog entry, so "when must we send the
    /// notice?" never becomes a send-request whoever recognised it.</summary>
    public bool IsVetoed(string question) => Exclude is not null && Exclude.IsMatch(question);

    /// <summary>
    /// A gap the capability investigator discovered (ADR-031). Every text is already sanitized by
    /// <see cref="DiscoveredGapText"/> — generic, no supplier, amount, date, e-mail or link — and
    /// the alternative clause is server-authored from <paramref name="nearestCapabilityKey"/>
    /// (<see cref="CapabilityGapCopy.NearestAlternative"/>), never model text.
    /// </summary>
    public static CapabilityGap Discovered(
        string slug,
        string titleEn,
        string titleIt,
        string operationEn,
        string operationIt,
        string descriptionEn,
        string descriptionIt,
        string? nearestCapabilityKey,
        string confidence,
        string investigatorVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        return new CapabilityGap(
            DiscoveredKeyPrefix + slug,
            titleEn,
            titleIt,
            operationEn,
            operationIt,
            CapabilityGapCopy.NearestAlternative(nearestCapabilityKey, Language.QuestionLanguage.English),
            CapabilityGapCopy.NearestAlternative(nearestCapabilityKey, Language.QuestionLanguage.Italian),
            descriptionEn,
            descriptionIt,
            GapAlternative.NearestCapability,
            Pattern: null)
        {
            Origin = GapOrigin.Investigator,
            Discovery = new Reply.GapDiscovery(titleEn, descriptionEn, nearestCapabilityKey, confidence, investigatorVersion),
        };
    }
}
