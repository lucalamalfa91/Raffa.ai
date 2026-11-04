using Raffa.SharedKernel.Text;

namespace Raffa.Renewals.Application.Cases;

/// <summary>
/// What <see cref="RenewalCaseEngine.View"/> returns: everything the prototype's <c>RC.view</c> returns
/// (header, summary, every feed card, timeline, checklist …) plus <see cref="Page"/>, the case screen as
/// the prototype composes it (completed steps, trailing chat bubbles, the one current card with at most
/// three visible buttons and the rest under "More"). Texts are <see cref="CaseText"/>.
/// Tones: <c>urgent</c> (accent), <c>alert</c> (accent-700), <c>muted</c> (neutral-600), <c>strong</c> (text); null = inherit.
/// </summary>
public sealed record CaseView
{
    public required CaseHeader Hdr { get; init; }

    public required CaseSummary Summary { get; init; }

    public required IReadOnlyList<CaseCard> Cards { get; init; }

    public required CasePage Page { get; init; }

    public required CaseTable Outlook { get; init; }

    public required IReadOnlyList<CaseTimelineEntry> Timeline { get; init; }

    public required IReadOnlyList<CaseUpcoming> Upcoming { get; init; }

    public int Stage { get; init; }

    /// <summary>The six phases (Start … Acquisition).</summary>
    public required IReadOnlyList<CaseStepItem> Steps7 { get; init; }

    /// <summary>The alternatives' eight steps while that branch is open, otherwise <see cref="Steps7"/>.</summary>
    public required IReadOnlyList<CaseStepItem> StepsX { get; init; }

    public CaseAltView? Alt { get; init; }

    public required IReadOnlyList<CaseTermCompare> TermsCmp { get; init; }

    public CaseOffersTable? OffersCmp { get; init; }

    public required IReadOnlyList<CaseChecklistItem> Check { get; init; }

    public required CaseText Example { get; init; }

    public CaseSinceView? Since { get; init; }

    public required IReadOnlyList<CaseRefItem> EmailsList { get; init; }

    public required IReadOnlyList<CaseRefItem> DocsList { get; init; }

    /// <summary>Case-level actions (Paste reply, Add contract, Add policy).</summary>
    public required IReadOnlyList<CaseLink> Actions { get; init; }

    public required IReadOnlyList<CaseThread> Threads { get; init; }

    /// <summary>Demo shortcuts (jump to the check date, simulate a colleague's edit).</summary>
    public required IReadOnlyList<CaseLink> Demo { get; init; }

    public required CaseReplyBox Reply { get; init; }

    /// <summary>English status code-name (the prototype's <c>shortStatus</c>, not translated there either).</summary>
    public required string ShortStatus { get; init; }

    public required CasePermissions Permissions { get; init; }
}

public sealed record CasePermissions(bool CanWork, bool CanApprove, bool CanClose, string Role);

public sealed record CaseHeader
{
    public required CaseText Title { get; init; }

    public required string Supplier { get; init; }

    public required string Contract { get; init; }

    public required CaseText Status { get; init; }

    public required CaseText Next { get; init; }

    public required string Approver { get; init; }

    public required string NoticeBy { get; init; }

    public int NoticeDays { get; init; }

    public required CaseText End { get; init; }

    public required string Owner { get; init; }

    public required IReadOnlyList<CaseText> Tracks { get; init; }

    public CaseText? Warn { get; init; }

    public required IReadOnlyList<CasePerson> People { get; init; }

    public required CaseText YouRole { get; init; }
}

public sealed record CasePerson(string Name, CaseText Role, bool IsOwner, bool IsDel, bool CanClose, bool Viewing);

public sealed record CaseSummary(
    IReadOnlyList<CaseRow> Goals,
    IReadOnlyList<CaseTermView> Terms,
    IReadOnlyList<CaseDecisionView> Decisions,
    CaseText Refs);

public sealed record CaseTermView(CaseText Label, string Val, CaseText State, string Status, string? Tone, int W);

public sealed record CaseDecisionView(string By, string At, CaseText Text);

public sealed record CaseRow(CaseText K, CaseText V, int? W = null, string? Tone = null);

public sealed record CaseCell(CaseText V, int? W = null, string? Tone = null);

public sealed record CaseTable(string Grid, IReadOnlyList<CaseText> Cols, IReadOnlyList<IReadOnlyList<CaseCell>> Rows);

public sealed record CaseStat(CaseText K, CaseText V, string? Tone = null);

public sealed record CaseCardStep(CaseText Label, bool On, bool Now);

public sealed record CaseStepItem(CaseText Label, int N, bool Done, bool Now);

public sealed record CaseButton(CaseText Label, string Act, CaseActionPayload P, bool Primary = false, bool Off = false, CaseText? Why = null);

public sealed record CaseChip(CaseText Label, string Act, CaseActionPayload P, bool On, bool Off = false);

/// <summary>An input bound to the <c>ui</c> action (<c>Act("ui", { k: Ui, v })</c>). Type: text | check | area.</summary>
public sealed record CaseInput(CaseText Label, object Value, string Ui, string Type);

/// <param name="Src">Where the value comes from (page, section, the reply it was agreed in); empty when unknown.</param>
public sealed record CaseField(CaseText Name, string Val, string Conf, string State, bool Open, CaseText DoneLabel, string InKey, string InVal, int I, CaseText? Src = null);

public sealed record CaseCardEmail
{
    public int Eid { get; init; }

    public required string To { get; init; }

    public bool ToMissing { get; init; }

    /// <summary><c>ui</c> key editing the recipient ("to:3").</summary>
    public required string ToKey { get; init; }

    /// <summary>draft | to_approve | approved | copied | sent | superseded.</summary>
    public required string StateKey { get; init; }

    public required CaseText State { get; init; }

    public required string Subject { get; init; }

    public required string Body { get; init; }

    public required IReadOnlyList<string> Attach { get; init; }

    public bool Editing { get; init; }

    public required string EditBody { get; init; }

    public bool ShowBody { get; init; }

    public required CaseText SentNote { get; init; }

    public required CaseText AttachNote { get; init; }
}

/// <summary>One feed card as <c>RC.view</c> builds it (<c>mkc</c>).</summary>
public sealed class CaseCard
{
    public string Id { get; set; } = string.Empty;

    public string K { get; set; } = string.Empty;

    public int St { get; set; }

    public string By { get; set; } = CaseRules.Raffa;

    public string At { get; set; } = string.Empty;

    /// <summary>A chat bubble (Raffa's or a person's words) rather than a step card.</summary>
    public bool Plain { get; set; }

    public CaseText Text { get; set; } = CaseText.Empty;

    public CaseText Kicker { get; set; } = CaseText.Empty;

    public CaseText Title { get; set; } = CaseText.Empty;

    public List<CaseText> Lines { get; set; } = [];

    public List<CaseRow> Rows { get; set; } = [];

    public CaseTable? Table { get; set; }

    public List<CaseChip> Chips { get; set; } = [];

    public List<CaseButton> Buttons { get; set; } = [];

    public List<CaseInput> Inputs { get; set; } = [];

    public CaseCardEmail? Email { get; set; }

    public List<CaseField> Fields { get; set; } = [];

    public List<CaseStat> Stats { get; set; } = [];

    public List<CaseText> Bullets { get; set; } = [];

    public List<CaseCardStep> Steps { get; set; } = [];

    public CaseText Note { get; set; } = CaseText.Empty;

    public string Pre { get; set; } = string.Empty;

    public CaseText Tag { get; set; } = CaseText.Empty;

    /// <summary>"" or "wait".</summary>
    public string Tone { get; set; } = string.Empty;

    /// <summary>The card the user acts on now.</summary>
    public bool Current { get; set; }

    /// <summary>Answered or superseded: shown muted.</summary>
    public bool Dim { get; set; }

    public CaseText Banner { get; set; } = CaseText.Empty;

    /// <summary>Outlook card stays pinned while the interview/plan is open.</summary>
    public bool Pin { get; set; }
}

/// <summary>The case screen as composed by the prototype page (<c>cs</c> + <c>cd</c>).</summary>
public sealed record CasePage
{
    /// <summary>"Renewal · Salesforce · Sales Cloud Enterprise".</summary>
    public required CaseText Title { get; init; }

    /// <summary>"Phase 3/6 · Negotiation" (or the header status).</summary>
    public required CaseText Phase { get; init; }

    public required CaseText Next { get; init; }

    /// <summary>"18 Oct 2026 · 40 d".</summary>
    public required CaseText Notice { get; init; }

    public bool NoticeUrgent { get; init; }

    public required CaseText End { get; init; }

    public CaseText? Warn { get; init; }

    /// <summary>"Who can proceed: …" shown with a warning.</summary>
    public required CaseText WhoCan { get; init; }

    public required IReadOnlyList<CaseDoneStep> Done { get; init; }

    /// <summary>The last (≤4) chat bubbles after the current card.</summary>
    public required IReadOnlyList<CaseBubble> Msgs { get; init; }

    public required CaseCurrentCard Current { get; init; }
}

public sealed record CaseDoneStep(
    CaseText Kicker,
    CaseText Title,
    CaseText Note,
    IReadOnlyList<CaseText> Lines,
    IReadOnlyList<CaseRow> Rows,
    IReadOnlyList<CaseCardStep> Steps,
    IReadOnlyList<CaseChip> Chips);

public sealed record CaseBubble(string By, bool Raffa, CaseText Text);

/// <summary>The current step card: at most three visible <see cref="Buttons"/> (primary first); everything else in <see cref="More"/>.</summary>
public sealed record CaseCurrentCard
{
    public required string K { get; init; }

    public required CaseText Kicker { get; init; }

    public required CaseText Title { get; init; }

    public required CaseText Banner { get; init; }

    public required IReadOnlyList<CaseText> Lines { get; init; }

    public required IReadOnlyList<CaseRow> Rows { get; init; }

    public CaseTable? Table { get; init; }

    public required IReadOnlyList<CaseCardStep> Steps { get; init; }

    public CaseCardEmail? Email { get; init; }

    public required string Pre { get; init; }

    public required IReadOnlyList<CaseField> Fields { get; init; }

    public required IReadOnlyList<CaseChip> Chips { get; init; }

    public required IReadOnlyList<CaseInput> Inputs { get; init; }

    public required IReadOnlyList<CaseButton> Buttons { get; init; }

    public required IReadOnlyList<CaseButton> More { get; init; }

    public required CaseText Note { get; init; }

    public required IReadOnlyList<CaseText> Bullets { get; init; }

    public required IReadOnlyList<CaseStat> Stats { get; init; }
}

public sealed record CaseTimelineEntry(string At, string Time, string By, CaseText Label, CaseText Text, bool Mine);

public sealed record CaseUpcoming(string Date, CaseText Label, CaseText When);

public sealed record CaseChecklistItem(bool Done, CaseText Label, string Who);

public sealed record CaseAltView(int St, IReadOnlyList<CaseStepItem> Steps7, IReadOnlyList<CaseChecklistItem> Check, CaseText Example);

public sealed record CaseTermCompare(CaseText Label, CaseText Hint, CaseText Current, CaseText From, CaseText Proposed, CaseText PropFrom, CaseText State, string? Tone, int W);

public sealed record CaseOffersTable(IReadOnlyList<string> Names, string? Best, double? BestTotal, CaseTable Table);

public sealed record CaseSinceView(int N, IReadOnlyList<CaseText> Lines);

public sealed record CaseRefItem(CaseText Label, CaseText State, string? Tone);

public sealed record CaseLink(CaseText Label, string Act, CaseActionPayload P);

public sealed record CaseThread(string T, bool On, bool Offer, bool HasOffer);

public sealed record CaseReplyBox(bool Open, string Text, string From);

/// <summary>An item for the Today feed (<c>RC.todo</c>).</summary>
public sealed record CaseTodoItem
{
    /// <summary>recipient | request | approve | send | plan | remind | reply | decide | policy | agree | draft | sign | sigok | fields | notice | nout | alt | altreq | altcand | altoffer | altexit | altnew.</summary>
    public required string Kind { get; init; }

    public required string Who { get; init; }

    public required CaseText Title { get; init; }

    public required CaseText Why { get; init; }

    public required string Due { get; init; }

    public int DueDays { get; init; }

    public int Stage { get; init; }

    public required CaseText StageLabel { get; init; }

    public string Ref { get; init; } = string.Empty;

    /// <summary>Days since the case last moved when ≥ 7, otherwise 0.</summary>
    public int Stalled { get; init; }
}
