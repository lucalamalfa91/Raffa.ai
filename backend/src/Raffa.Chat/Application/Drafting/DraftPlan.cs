using Raffa.Chat.Application.Pack;

namespace Raffa.Chat.Application.Drafting;

/// <summary>One ask of the offer: the lever, the quotable sentence, the pack keys it rests on.</summary>
public sealed record DraftAsk(string Lever, string Sentence, IReadOnlyList<string> CitationKeys);

/// <summary>
/// The offer plan the writer works from (ADR-030 D3) — normally the offer planner's own output,
/// otherwise derived deterministically from the pack (<see cref="FromPack"/>) so a failed or
/// disabled planner never leaves the writer, or the template, without asks.
/// </summary>
public sealed record DraftPlan(
    string Position,
    IReadOnlyList<DraftAsk> Asks,
    string Trade,
    string DeadlineAnchor,
    string Closing)
{
    private const int MaxAsksFromPack = 3;

    /// <summary>Asks from the council's plays (<c>calc:council:play[n]</c>, their quotable ask
    /// sentence only — never the "Timing:/Fallback:/Grounded in:" trail) or, without a council,
    /// from the lever items; the position from the target verdict; the trade from the first
    /// playbook entry's own quotable ask.</summary>
    public static DraftPlan FromPack(IReadOnlyList<PackItem> pack)
    {
        ArgumentNullException.ThrowIfNull(pack);

        var plays = pack.Where(i => i.CitationKey.StartsWith("calc:council:play[", StringComparison.Ordinal)).ToList();
        var source = plays.Count > 0
            ? plays
            : pack.Where(i => i.CitationKey.StartsWith("calc:lever[", StringComparison.Ordinal)).ToList();

        var asks = source
            .Take(MaxAsksFromPack)
            .Select(item => new DraftAsk(LeverName(item.Title), AskSentence(item.Snippet), [item.CitationKey]))
            .Where(ask => !string.IsNullOrWhiteSpace(ask.Sentence))
            .ToList();

        // The goal verdict first (what the offer is anchored on), else the timing item.
        var position = (pack.FirstOrDefault(i => i.CitationKey == "calc:savings-target")
            ?? pack.FirstOrDefault(i => i.CitationKey == "calc:when-you-must-move"))?.Snippet ?? string.Empty;

        var trade = pack
            .Where(i => i.CitationKey.StartsWith("raffa:playbook:", StringComparison.Ordinal))
            .Select(i => PlaybookAsk(i.Snippet))
            .FirstOrDefault(ask => ask is not null) ?? string.Empty;

        var deadline = pack
            .SelectMany(i => i.Values)
            .FirstOrDefault(v => v.Kind == PackValueKind.Date && v.Key is "cancellationDeadline" or "noticeDeadline")
            ?.Value ?? string.Empty;

        return new DraftPlan(position, asks, trade, deadline, string.Empty);
    }

    /// <summary>"Play 1 — Market discount" → "Market discount"; anything else unchanged.</summary>
    public static string LeverName(string title)
    {
        var separator = title.IndexOf(" — ", StringComparison.Ordinal);
        return separator >= 0 && title.StartsWith("Play ", StringComparison.Ordinal)
            ? title[(separator + 3)..].Trim()
            : title.Trim();
    }

    /// <summary>The quotable part of a council play's snippet — everything before the
    /// " Timing:" / " Fallback:" / " Grounded in:" trail.</summary>
    public static string AskSentence(string snippet)
    {
        var cut = snippet.Length;
        foreach (var marker in new[] { " Timing:", " Fallback:", " Grounded in:" })
        {
            var index = snippet.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0 && index < cut)
            {
                cut = index;
            }
        }

        return snippet[..cut].Trim();
    }

    /// <summary>The quoted sentence after "Ask:" in a playbook item's snippet
    /// (<c>Playbook.NegotiationPlaybook.ToPackItem</c>'s own "… Ask: "…"" layout), unquoted.</summary>
    public static string? PlaybookAsk(string snippet)
    {
        const string Marker = " Ask: ";
        var index = snippet.IndexOf(Marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var ask = snippet[(index + Marker.Length)..].Trim().Trim('"').Trim();
        return ask.Length == 0 ? null : ask;
    }
}
