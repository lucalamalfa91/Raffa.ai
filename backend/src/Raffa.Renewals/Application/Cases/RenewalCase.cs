using System.Text.Json;
using System.Text.Json.Serialization;
using Raffa.SharedKernel.Text;

namespace Raffa.Renewals.Application.Cases;

/// <summary>
/// The renewal case aggregate state — a 1:1 typed mirror of the prototype's case object
/// (<c>renewal-case-v4.js</c> <c>RC.create</c>). Plain JSON (camelCase via <see cref="RenewalCaseJson"/>),
/// meant to be stored as one jsonb document. People are display names; the API maps users ↔ names.
/// Mutable on purpose: <see cref="RenewalCaseEngine.Act"/> deep-clones it and returns a new instance.
/// </summary>
public sealed class RenewalCase
{
    public int Cid { get; set; }

    public string Owner { get; set; } = string.Empty;

    public string Approver { get; set; } = string.Empty;

    public DateOnly Updated { get; set; }

    /// <summary>Last feed sequence each viewer has seen (for "since your last visit").</summary>
    public Dictionary<string, int> Seen { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Explicit delegates: name → rights. Order matters (the first delegate is "who approves").</summary>
    public OrderedDictionary<string, CaseDelegate> Delegates { get; set; } = new(StringComparer.Ordinal);

    public List<string> Members { get; set; } = [];

    public List<CaseFeedItem> Feed { get; set; } = [];

    public int Seq { get; set; }

    /// <summary>interview | plan | email | waiting_rem | waiting | waiting_due | review | agree | doc_wait | doc_check | sign_wait | sign_declared | sign_confirm | pipeline | done | notice_out.</summary>
    public string Phase { get; set; } = CasePhases.Interview;

    /// <summary>The levers and strategy Raffa and the person worked out in chat before the case was opened (plain text,
    /// from the stored conversation): shown as the first card, used to shape the questions and the emails. Null = none.</summary>
    public string? Brief { get; set; }

    /// <summary>The interview questions written for this contract and chat (null = the standard wording).</summary>
    public CaseInterviewTailoring? Interview { get; set; }

    /// <summary>Index of the open interview question (0–3).</summary>
    public int Q { get; set; }

    /// <summary>While the person changes an earlier answer: the question to go back to once it is answered
    /// (3 = the clauses, then the plan). Null on the normal path.</summary>
    public int? ResumeQ { get; set; }

    public CaseGoals Goals { get; set; } = new();

    public int PlanVer { get; set; }

    public bool PlanOk { get; set; }

    public List<CaseEmail> Emails { get; set; } = [];

    public List<CaseReminder> Rems { get; set; } = [];

    public List<CaseReply> Reps { get; set; } = [];

    public int Round { get; set; }

    /// <summary>Negotiated terms by key (price, term, uplift, payment, liab, renew, c_…). Insertion order is display order.</summary>
    public OrderedDictionary<string, CaseTerm> Terms { get; set; } = new(StringComparer.Ordinal);

    public CaseLimits? Limits { get; set; }

    /// <summary>Decision log (who did what, when).</summary>
    public List<CaseDecisionEntry> Decs { get; set; } = [];

    /// <summary>Open questions to the owner: price/term trade-off (<c>open</c>) and terms to confirm (<c>agree</c>).</summary>
    public List<CaseQuestion> Qs { get; set; } = [];

    public List<CaseDraftDoc> Docs { get; set; } = [];

    public CaseSigned? Signed { get; set; }

    public CasePipe? Pipe { get; set; }

    public CaseOutcome? Outcome { get; set; }

    public CaseNotice? Notice { get; set; }

    public CaseAlt? Alt { get; set; }

    /// <summary>The vendor thread shown in the alternatives branch (null = the main thread).</summary>
    public string? Thread { get; set; }

    public CaseUi Ui { get; set; } = new();

    public List<CaseOwnerChange> History { get; set; } = [];

    public CaseAgreed? Agreed { get; set; }

    /// <summary>Unit prices the supplier offered in its replies, by term in months ("12" / "24"), as the
    /// model read them; when present they replace the rule-of-thumb counter price.</summary>
    public Dictionary<string, double>? OfferedPrices { get; set; }

    public bool PipeNew { get; set; }

    /// <summary>Transient: text the client should put on the clipboard after <c>copy</c> (never means sent).</summary>
    public string? Clip { get; set; }

    /// <summary>Transient: the client should send <c>tick</c> shortly (extraction pipeline progress).</summary>
    public bool Tick { get; set; }

    public RenewalCase DeepClone() => RenewalCaseJson.Deserialize(RenewalCaseJson.Serialize(this));
}

public static class CasePhases
{
    public const string Interview = "interview";
    public const string Plan = "plan";
    public const string Email = "email";
    public const string WaitingRem = "waiting_rem";
    public const string Waiting = "waiting";
    public const string WaitingDue = "waiting_due";
    public const string Review = "review";
    public const string Agree = "agree";
    public const string DocWait = "doc_wait";
    public const string DocCheck = "doc_check";
    public const string SignWait = "sign_wait";
    public const string SignDeclared = "sign_declared";
    public const string SignConfirm = "sign_confirm";
    public const string Pipeline = "pipeline";
    public const string Done = "done";
    public const string NoticeOut = "notice_out";
}

public static class EmailStates
{
    public const string Draft = "draft";
    public const string ToApprove = "to_approve";
    public const string Approved = "approved";
    public const string Copied = "copied";
    public const string Sent = "sent";
    public const string Superseded = "superseded";
}

public sealed class CaseDelegate
{
    public bool Close { get; set; }
}

/// <summary>One entry of the case feed (a chat card). Which fields are set depends on <see cref="K"/>.</summary>
public sealed class CaseFeedItem
{
    public string Id { get; set; } = string.Empty;

    /// <summary>ov | outlook | q | user | say | plan | policy | email | rem | reply | ana | dec | agree | doc | signwait | sig | pipe | out | ncheck | nrec | nout | info | areq | acand | aoff | acmp | asel | asel2 | recap.</summary>
    public string K { get; set; } = string.Empty;

    /// <summary>Stage (0–5) when the item was posted.</summary>
    public int St { get; set; }

    /// <summary>"08 Sep · 14:21".</summary>
    public string At { get; set; } = string.Empty;

    public string By { get; set; } = "Raffa.ai";

    public int? I { get; set; }

    public int? Ver { get; set; }

    public int? Eid { get; set; }

    public string? Thread { get; set; }

    public int? Rid { get; set; }

    public int? Did { get; set; }

    public int? V { get; set; }

    public string? Vendor { get; set; }

    public CaseText? Text { get; set; }

    public CaseText? Tag { get; set; }

    public CaseText? Kicker { get; set; }

    public CaseText? Title { get; set; }

    public List<CaseText>? Bullets { get; set; }

    public CaseText? Note { get; set; }
}

/// <summary>
/// The interview wording the model wrote for one case, from the contract's own content and the levers worked out in
/// chat: the question, the hint under it and (for the products question) the option labels. Keys and meanings never change.
/// </summary>
public sealed class CaseInterviewTailoring
{
    public List<CaseQuestionTailoring> Questions { get; set; } = [];
}

public sealed class CaseQuestionTailoring
{
    /// <summary>0 products, 1 term, 2 economic goal, 3 clauses.</summary>
    public int Index { get; set; }

    public string? Text { get; set; }

    public string? Hint { get; set; }

    /// <summary>Option key → label (only for the products question).</summary>
    public Dictionary<string, string>? Options { get; set; }
}

public sealed class CaseGoals
{
    /// <summary>keep | reduce | add | custom (null = not answered).</summary>
    public string? Prod { get; set; }

    /// <summary>"12" | "24" | "36" | custom.</summary>
    public string? Term { get; set; }

    /// <summary>none | p5 | p10 | budget | custom (null = not answered; "none" is a valid answer).</summary>
    public string? Target { get; set; }

    /// <summary>Selected clause keys (p0, r1 …); null until question 4 opens.</summary>
    public List<string>? Clauses { get; set; }

    public bool Policy { get; set; }

    public string? ProdText { get; set; }

    public string? TermText { get; set; }

    public string? TargetText { get; set; }

    public string? ClausesText { get; set; }
}

public sealed class CaseEmail
{
    public int Id { get; set; }

    /// <summary>first | followup | counter | docreq | fix | notice | rfq.</summary>
    public string Kind { get; set; } = string.Empty;

    public int Ver { get; set; } = 1;

    /// <summary>Recipient — never generated; empty until the user adds it.</summary>
    public string To { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    /// <summary>Body text (English, as in the prototype).</summary>
    public string Body { get; set; } = string.Empty;

    public List<string> Attach { get; set; } = [];

    /// <summary>draft | to_approve | approved | copied | sent | superseded.</summary>
    public string State { get; set; } = EmailStates.Draft;

    public string? ApprovedBy { get; set; }

    public int? ApprovedVer { get; set; }

    public CaseSentDeclaration? Sent { get; set; }

    public string? Thread { get; set; }

    public string? Vendor { get; set; }
}

public sealed class CaseSentDeclaration
{
    public string By { get; set; } = string.Empty;

    /// <summary>As typed by the user ("08 Sep 2026 · 14:40").</summary>
    public string At { get; set; } = string.Empty;

    public string To { get; set; } = string.Empty;

    public bool Edited { get; set; }

    /// <summary>The text actually sent, when it differs from the approved one.</summary>
    public string? Text { get; set; }
}

public sealed class CaseReminder
{
    public int Rid { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>proposed | set | due | cancelled.</summary>
    public string State { get; set; } = "proposed";
}

public sealed class CaseReply
{
    public int Rid { get; set; }

    public int Round { get; set; }

    public string Text { get; set; } = string.Empty;

    public string By { get; set; } = string.Empty;

    public DateOnly At { get; set; }

    public List<CaseReplyRow> Rows { get; set; } = [];
}

public sealed class CaseReplyRow
{
    public CaseText Label { get; set; } = CaseText.Empty;

    public CaseText Said { get; set; } = CaseText.Empty;

    public CaseText Plan { get; set; } = CaseText.Empty;

    /// <summary>proposed | unclear | rejected | conflict.</summary>
    public string State { get; set; } = "proposed";

    public CaseText Warn { get; set; } = CaseText.Empty;
}

public sealed class CaseTerm
{
    public CaseText Label { get; set; } = CaseText.Empty;

    /// <summary>open | proposed | agreed | rejected | unclear.</summary>
    public string State { get; set; } = "open";

    /// <summary>Part of the confirmed plan.</summary>
    public bool Plan { get; set; }

    public string? Val { get; set; }

    /// <summary>Evidence: "reply 1".</summary>
    public string? Ev { get; set; }
}

public sealed class CaseLimits
{
    public int Term { get; set; }

    public double? PriceMax { get; set; }

    public int? Exit { get; set; }
}

public sealed class CaseDecisionEntry
{
    public string By { get; set; } = string.Empty;

    public DateOnly At { get; set; }

    public CaseText Text { get; set; } = CaseText.Empty;
}

public sealed class CaseQuestion
{
    public int Did { get; set; }

    /// <summary>open (trade-off) | agree (terms to confirm) | done.</summary>
    public string State { get; set; } = "open";

    public string? Opt { get; set; }

    public string? By { get; set; }

    public CaseText? Label { get; set; }
}

public sealed class CaseDraftDoc
{
    public int V { get; set; }

    public string File { get; set; } = string.Empty;

    public string By { get; set; } = string.Empty;

    public DateOnly At { get; set; }

    public List<CaseDraftRow> Rows { get; set; } = [];

    public bool Manual { get; set; }
}

public sealed class CaseDraftRow
{
    public CaseText Label { get; set; } = CaseText.Empty;

    public CaseText Agreed { get; set; } = CaseText.Empty;

    public CaseText Draft { get; set; } = CaseText.Empty;

    /// <summary>ok | diff | unread | manual.</summary>
    public string Res { get; set; } = "ok";

    public string Src { get; set; } = string.Empty;
}

public sealed class CaseSigned
{
    public string File { get; set; } = string.Empty;

    public string By { get; set; } = string.Empty;

    public DateOnly At { get; set; }

    public string? ConfirmedBy { get; set; }

    /// <summary>Signed-vs-last-draft comparison read from the signed file (points checked, differences found); null = not read.</summary>
    public int? CheckedPoints { get; set; }

    public int? Differences { get; set; }
}

public sealed class CasePipe
{
    /// <summary>0 upload · 1 processing · 2 extraction · 3 review · 4 validation.</summary>
    public int Stage { get; set; }

    /// <summary>False when no automatic extraction ran on the signed file: the fields are the values that
    /// will become the contract's new terms, for the user to check against the signed document.</summary>
    public bool Extracted { get; set; } = true;

    public List<CaseWeakField> Weak { get; set; } = [];
}

public sealed class CaseWeakField
{
    public string N { get; set; } = string.Empty;

    public CaseText Name { get; set; } = CaseText.Empty;

    public string Val { get; set; } = string.Empty;

    /// <summary>Extraction confidence in %; null when the value was not extracted from the document.</summary>
    public int? Conf { get; set; }

    /// <summary>Where the value comes from (page, section, or the reply it was agreed in); "" when unknown.</summary>
    public CaseText Src { get; set; } = CaseText.Empty;

    /// <summary>open | accepted | corrected.</summary>
    public string State { get; set; } = "open";
}

public sealed class CaseOutcome
{
    /// <summary>renewed | terminated.</summary>
    public string Kind { get; set; } = string.Empty;

    public DateOnly? Start { get; set; }

    public DateOnly End { get; set; }

    public DateOnly? Cancel { get; set; }

    public int? CancelDays { get; set; }

    public double? Spend { get; set; }

    public double? Delta { get; set; }

    public CaseText? Price { get; set; }

    public string? File { get; set; }

    public DateOnly? When { get; set; }

    public string? ReplacedBy { get; set; }

    /// <summary>The contract the case renewed, as it stood when the case closed. Once the outcome is
    /// written the contract's facts carry the new term, so the case keeps the previous dates itself.
    /// Null on cases closed before these were recorded.</summary>
    public DateOnly? PrevStart { get; set; }

    public DateOnly? PrevEnd { get; set; }

    /// <summary>The end of the contract this case renewed: recorded at closure, else the day before the new
    /// term starts, else <paramref name="currentEnd"/>.</summary>
    public DateOnly PreviousEnd(DateOnly currentEnd) => PrevEnd ?? Start?.AddDays(-1) ?? currentEnd;
}

public sealed class CaseNotice
{
    /// <summary>check | email | sent | out.</summary>
    public string State { get; set; } = "check";

    public DateOnly? SentAt { get; set; }

    public bool Evidence { get; set; }

    public string? EvFile { get; set; }

    public string? By { get; set; }
}

public sealed class CaseAlt
{
    /// <summary>req | cand | rfq | compare | selected.</summary>
    public string State { get; set; } = "req";

    public List<CaseAltRequirement> Reqs { get; set; } = [];

    public List<string> Vendors { get; set; } = [];

    /// <summary>Offers received, by vendor (insertion order = display order).</summary>
    public OrderedDictionary<string, CaseAltOffer> Offers { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Vendors an RFQ email was already prepared for (the prototype's <c>offers[vendor+'_rfq']</c>).</summary>
    public List<string> RfqFor { get; set; } = [];

    public bool Cmp { get; set; }

    public string? Chosen { get; set; }

    public bool ExitDone { get; set; }

    public bool NewDone { get; set; }

    public string? NewFile { get; set; }
}

public sealed class CaseAltRequirement
{
    public CaseText Text { get; set; } = CaseText.Empty;

    public bool On { get; set; } = true;
}

public sealed class CaseAltOffer
{
    /// <summary>Unit price for per-unit products, annual total otherwise.</summary>
    public double Price { get; set; }

    public double Total { get; set; }

    public int Term { get; set; }

    public string Cover { get; set; } = string.Empty;

    public string Mig { get; set; } = string.Empty;

    public string Gap { get; set; } = string.Empty;

    public string By { get; set; } = string.Empty;
}

public sealed class CaseOwnerChange
{
    public string Who { get; set; } = string.Empty;

    public DateOnly At { get; set; }
}

/// <summary>
/// What the owner confirmed as agreed ("Confirm as agreed"). Outside the demo script it is a snapshot of
/// the terms the supplier's replies proposed and the owner confirmed (<see cref="Points"/>): a point
/// nobody agreed stays null and never reaches an email or a document check.
/// </summary>
public sealed class CaseAgreed
{
    /// <summary>Agreed unit price of the priced (first recurring) line.</summary>
    public double Price { get; set; }

    /// <summary>Agreed term in months.</summary>
    public int Term { get; set; }

    /// <summary>Agreed cap on yearly price increases in %; 0 = no increase; null = not agreed.</summary>
    public int? Cap { get; set; }

    /// <summary>Agreed limitation of liability in months of fees; null = not agreed.</summary>
    public int? Liab { get; set; }

    /// <summary>Agreed quantity of the priced line; null = unchanged.</summary>
    public double? Qty { get; set; }

    /// <summary>Agreed payment terms as the supplier wrote them; null = not agreed.</summary>
    public string? Payment { get; set; }

    /// <summary>Every confirmed point, as read from the supplier's replies (label, value, evidence).</summary>
    public List<CaseAgreedPoint> Points { get; set; } = [];

    public string? Auth { get; set; }
}

public sealed class CaseAgreedPoint
{
    public string Key { get; set; } = string.Empty;

    public CaseText Label { get; set; } = CaseText.Empty;

    public string Val { get; set; } = string.Empty;

    /// <summary>Evidence: "reply 2".</summary>
    public string Ev { get; set; } = string.Empty;
}

/// <summary>Per-case UI state the prototype keeps in the case (inputs, open panels, warnings).</summary>
public sealed class CaseUi
{
    /// <summary>Last denial or validation message (cleared on every action).</summary>
    public CaseText? Warn { get; set; }

    public CaseSince? Since { get; set; }

    public DateOnly? RemDate { get; set; }

    public int? RemN { get; set; }

    public bool RemCustom { get; set; }

    public string? RemNum { get; set; }

    /// <summary>The question index whose "Other — write your own answer" box is open.</summary>
    public int? Other { get; set; }

    public string? OtherText { get; set; }

    public int? Edit { get; set; }

    public string? EditBody { get; set; }

    public int? SentOpen { get; set; }

    public string? SentDate { get; set; }

    public bool SentEdited { get; set; }

    public string? SentText { get; set; }

    public bool ReplyOpen { get; set; }

    public string? ReplyText { get; set; }

    /// <summary>Who authorized the policy exception (name / role).</summary>
    public string? Auth { get; set; }

    public bool NoEv { get; set; }

    public string? AltFeat { get; set; }

    public string? AltName { get; set; }

    /// <summary>yes | wait | no.</summary>
    public string? RecapDone { get; set; }

    public CaseConflict? Conflict { get; set; }

    /// <summary>Corrected values of flagged fields: cf0, cf1 …</summary>
    public Dictionary<string, string> Cf { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Any other free key set through the <c>ui</c> action.</summary>
    public Dictionary<string, string> Extra { get; set; } = new(StringComparer.Ordinal);
}

public sealed class CaseSince
{
    public int N { get; set; }

    public List<CaseSinceItem> Items { get; set; } = [];
}

public sealed class CaseSinceItem
{
    public string By { get; set; } = string.Empty;

    public string K { get; set; } = string.Empty;
}

public sealed class CaseConflict
{
    public int Eid { get; set; }

    public string By { get; set; } = string.Empty;

    public string At { get; set; } = string.Empty;

    public int Ver { get; set; }

    public string PrevState { get; set; } = string.Empty;

    public string? Yours { get; set; }
}

/// <summary>The jsonb serialization of <see cref="RenewalCase"/> (camelCase, nulls omitted).</summary>
public static class RenewalCaseJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(RenewalCase c) => JsonSerializer.Serialize(c, Options);

    public static RenewalCase Deserialize(string json) =>
        JsonSerializer.Deserialize<RenewalCase>(json, Options) ?? throw new JsonException("Empty renewal case document.");
}
