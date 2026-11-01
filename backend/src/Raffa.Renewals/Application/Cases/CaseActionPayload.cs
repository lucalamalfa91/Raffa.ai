using System.Globalization;
using System.Text.Json;
using Raffa.SharedKernel.Text;

namespace Raffa.Renewals.Application.Cases;

/// <summary>
/// The payload of <see cref="RenewalCaseEngine.Act"/> — the union of every field the prototype's
/// <c>RC.act(case, type, p, ctx)</c> reads from <c>p</c>. Each action reads only its own fields
/// (see <see cref="CaseActions"/>).
/// </summary>
public sealed record CaseActionPayload
{
    /// <summary><c>ans</c>: question index 0–2.</summary>
    public int? Q { get; init; }

    /// <summary><c>ans</c>: option key (keep, 12, p10 …).</summary>
    public string? Val { get; init; }

    /// <summary><c>ui</c>: the key (otherText, to:0, remNum, auth, cf0 …); <c>toggle</c>: the clause key.</summary>
    public string? K { get; init; }

    /// <summary><c>ui</c>: the value (string, bool or number; a <see cref="JsonElement"/> when deserialized).</summary>
    public object? V { get; init; }

    public int? Eid { get; init; }

    public int? Rid { get; init; }

    public int? Did { get; init; }

    /// <summary><c>decide</c>: a | b | c.</summary>
    public string? Opt { get; init; }

    /// <summary><c>say</c> / <c>note</c> / <c>reply</c>: the person's words; <c>answer</c>: Raffa's sourced answer.</summary>
    public CaseText? Text { get; init; }

    /// <summary><c>answer</c>: the "back to the pending step" line.</summary>
    public CaseText? Resume { get; init; }

    /// <summary><c>delegate</c> / <c>delegateClose</c> / <c>transfer</c>: the person.</summary>
    public string? Who { get; init; }

    /// <summary><c>delegate</c>: add (true) or remove (false).</summary>
    public bool? On { get; init; }

    /// <summary><c>acceptF</c> / <c>correctF</c> / <c>altReqToggle</c>: index.</summary>
    public int? I { get; init; }

    /// <summary><c>remPick</c>: days from today.</summary>
    public int? N { get; init; }

    /// <summary><c>markAnnex</c>: draft version (1-based).</summary>
    public int? Version { get; init; }

    public string? Vendor { get; init; }

    /// <summary><c>thread</c>: the vendor thread to show (null = main).</summary>
    public string? T { get; init; }

    /// <summary><c>addDoc</c>: the uploaded file name (defaults to the prototype's sample name).</summary>
    public string? File { get; init; }

    /// <summary>Navigation target of a <c>nav</c> button (c360 | renewals) — never sent to Act.</summary>
    public string? To { get; init; }

    public static CaseActionPayload None { get; } = new();

    internal string ValueString()
    {
        return V switch
        {
            null => string.Empty,
            string s => s,
            bool b => b ? "true" : "false",
            JsonElement { ValueKind: JsonValueKind.String } j => j.GetString() ?? string.Empty,
            JsonElement { ValueKind: JsonValueKind.True } => "true",
            JsonElement { ValueKind: JsonValueKind.False } => "false",
            JsonElement { ValueKind: JsonValueKind.Number } j => CaseText.JsNumber(j.GetDouble()),
            JsonElement { ValueKind: JsonValueKind.Null } => string.Empty,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => V.ToString() ?? string.Empty,
        };
    }

    /// <summary>JavaScript truthiness of <see cref="V"/>.</summary>
    internal bool ValueBool()
    {
        return V switch
        {
            null => false,
            bool b => b,
            string s => s.Length > 0,
            int i => i != 0,
            double d => d != 0 && !double.IsNaN(d),
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.String } j => (j.GetString() ?? string.Empty).Length > 0,
            JsonElement { ValueKind: JsonValueKind.Number } j => j.GetDouble() != 0,
            _ => false,
        };
    }

    internal int? ValueInt()
    {
        var s = ValueString();
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    }
}

/// <summary>Every action type <see cref="RenewalCaseEngine.Act"/> understands (the prototype's <c>RC.act</c> switch).</summary>
public static class CaseActions
{
    public static readonly IReadOnlyList<string> All =
    [
        "visit", "sinceOk", "ui", "ans", "toggle", "policy", "q3done", "editPlan", "confirmPlan", "requestAppr", "approve", "edit",
        "cancelEdit", "saveEdit", "copy", "sentOpen", "sentCancel", "sentOk", "toEx", "remOk", "remChange", "ff", "followup",
        "openReply", "closeReply", "sampleReply", "reply", "decide", "agreeOk", "addDoc", "fixEmail", "markAnnex", "signDeclared",
        "sigOk", "tick", "acceptF", "correctF", "validate", "startNotice", "nStart", "nReceipt", "nOut", "startAlt", "altReqs",
        "altReqToggle", "altFeatAdd", "remPick", "recapYes", "recapWait", "recapNo", "altAdd", "altEx", "altRfq", "thread",
        "altOffer", "altSel", "altNew", "delegate", "delegateClose", "transfer", "say", "note", "answer", "collab",
        "conflictRecover", "conflictDismiss",
    ];

    /// <summary>Actions that do not touch <c>updated</c> (UI-only).</summary>
    internal static readonly HashSet<string> Passive = new(StringComparer.Ordinal)
    {
        "ui", "visit", "thread", "sinceOk", "sentOpen", "sentCancel", "edit", "cancelEdit", "openReply", "closeReply", "sampleReply",
    };
}
