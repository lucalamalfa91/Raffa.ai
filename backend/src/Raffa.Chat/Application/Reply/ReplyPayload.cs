using System.Text.Json;
using System.Text.Json.Serialization;

namespace Raffa.Chat.Application.Reply;

/// <summary>
/// The structured half of a reply (ADR-030 D2; the wire's <c>payload</c> object and
/// <c>ConversationMessage.PayloadJson</c>): every member is optional, and a turn that carries
/// none of them has a <see langword="null"/> payload altogether. Which members a kind carries:
/// <see cref="ReplyKind.Draft"/> — <see cref="Gap"/> + <see cref="Draft"/> + <see cref="FeedbackOffer"/>;
/// a capability-gap <see cref="ReplyKind.Redirect"/> — <see cref="Gap"/> + <see cref="FeedbackOffer"/>;
/// the feedback confirmation (<see cref="ReplyKind.Answer"/>) — <see cref="FeedbackResult"/>.
/// </summary>
public sealed record ReplyPayload(
    GapInfo? Gap = null,
    EmailDraft? Draft = null,
    FeedbackOffer? FeedbackOffer = null,
    FeedbackResult? FeedbackResult = null);

/// <summary>Which gap fired and in which language the deterministic copy was written.</summary>
/// <param name="Key"><c>Gaps.CapabilityGap.Key</c> — a catalog key, or
/// <c>discovered:&lt;slug&gt;</c> for a gap the capability investigator found (ADR-031).</param>
/// <param name="Title">The gap's title in <paramref name="Language"/> — what the feedback card and
/// the confirmation name.</param>
/// <param name="Language"><c>Language.QuestionLanguage</c>'s "it" or "en".</param>
/// <param name="Discovery">Only for a discovered gap: what the investigator found. Rows stored
/// before ADR-031 have no such member and read back as <see langword="null"/>.</param>
public sealed record GapInfo(string Key, string Title, string Language, GapDiscovery? Discovery = null);

/// <summary>
/// What the capability investigator (<c>Gaps.CapabilityInvestigator</c>, ADR-031) found when a
/// turn asked for an operation nothing in Raffa performs — the developer-facing half of a
/// discovered gap, in English. Every text is already generic (<c>Gaps.DiscoveredGapText</c>: no
/// supplier, amount, date, e-mail or link), because this record travels into a public GitHub issue.
/// </summary>
/// <param name="TitleEn">The proposed feature's title — the issue's title.</param>
/// <param name="DescriptionEn">One sentence: what Raffa should do.</param>
/// <param name="NearestCapability">The <c>Capabilities.CapabilityCatalog</c> key of the closest
/// existing screen, or <see langword="null"/> when nothing comes close.</param>
/// <param name="Confidence">The investigator's own "high" / "medium".</param>
/// <param name="InvestigatorVersion">The prompt version that found it (<c>Gaps.CapabilityInvestigatorAgent.Version</c>).</param>
public sealed record GapDiscovery(
    string TitleEn,
    string DescriptionEn,
    string? NearestCapability,
    string Confidence,
    string InvestigatorVersion);

/// <summary>The drafted email — plain text, verbatim, never rendered through the markdown subset
/// (the client shows it in a pre-wrap block with a "Copy email" button). No inline <c>[n]</c>
/// marker, no link, no guid (<c>Drafting.DraftGuard</c>).</summary>
public sealed record EmailDraft(string Subject, string Body);

/// <summary>Everything the in-chat feedback card renders, in the question's language — the
/// client owns no copy of its own for it (ADR-030 D4/D5).</summary>
public sealed record FeedbackOffer(
    string Prompt,
    string YesLabel,
    string NoLabel,
    string NextLabel,
    string BackLabel,
    string SubmitLabel,
    string SendingLabel,
    string ThanksLabel,
    string ErrorLabel,
    string PublicNotice,
    IReadOnlyList<FeedbackQuestion> Questions);

/// <summary>One of the three interview questions.</summary>
/// <param name="Key"><c>what</c> / <c>frequency</c> / <c>importance</c> — the keys
/// <c>Feedback.FeedbackAnswers</c> is submitted with.</param>
/// <param name="Kind"><c>text</c> (a free-text field, prefilled) or <c>choice</c> (one of
/// <paramref name="Choices"/>).</param>
/// <param name="Prefill">The editable default for a <c>text</c> question; <see langword="null"/>
/// for a <c>choice</c>.</param>
/// <param name="Choices">The quick-choice chips for a <c>choice</c> question; <see langword="null"/>
/// for <c>text</c>.</param>
public sealed record FeedbackQuestion(
    string Key, string Kind, string Label, string? Prefill, IReadOnlyList<FeedbackChoice>? Choices);

public sealed record FeedbackChoice(string Key, string Label);

/// <summary>What a submitted feedback produced — carried by the confirmation turn so a resumed
/// conversation knows which offer was already answered.</summary>
/// <param name="ForMessageId">The id of the turn whose <see cref="FeedbackOffer"/> was answered.</param>
/// <param name="Status"><c>recorded</c> (stored, no issue) or <c>issue_opened</c>.</param>
public sealed record FeedbackResult(string ForMessageId, string Status, int? IssueNumber, string? IssueUrl);

/// <summary>The one JSON convention for <see cref="ReplyPayload"/> — camelCase, nulls written
/// explicitly (so the hand-written OpenAPI's <c>required</c> lists stay honest), no indentation —
/// shared by the persisted column, the wire, and the feedback service that reads a stored turn
/// back.</summary>
public static class ReplyPayloadJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    public static string Serialize(ReplyPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return JsonSerializer.Serialize(payload, Options);
    }

    /// <summary><see langword="null"/> for a null/blank column and for a column that does not
    /// parse — a malformed stored row degrades to "no payload", never to a failed read.</summary>
    public static ReplyPayload? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ReplyPayload>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static JsonElement ToJsonElement(ReplyPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        using var document = JsonDocument.Parse(Serialize(payload));
        return document.RootElement.Clone();
    }
}
