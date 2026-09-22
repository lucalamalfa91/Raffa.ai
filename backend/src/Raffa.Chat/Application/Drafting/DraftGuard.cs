using System.Text.RegularExpressions;
using Raffa.Chat.Application.Guards;
using Raffa.Chat.Application.Pack;

namespace Raffa.Chat.Application.Drafting;

/// <summary>
/// The grounding gate of a drafted email (ADR-030 D3) — the draft's counterpart of
/// <see cref="GroundingGuard"/> + <see cref="NumericGuard"/>. An email has no citation apparatus,
/// so the inline-marker rule inverts: a <c>[n]</c> in the text is a violation, and the grounding
/// is proven by <c>usedCitationKeys</c> resolving to pack items plus every number, percentage and
/// date in the text equalling a pack value (<see cref="NumericGuard.Validate"/>, unchanged). A
/// link, a guid or an internal key in the text is engineer chrome a supplier must never see.
/// </summary>
public static class DraftGuard
{
    private static readonly Regex InlineMarkerPattern = new(@"\[\d+\]", RegexOptions.Compiled);

    private static readonly Regex LinkPattern = new(@"https?://", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GuidPattern = new(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
        RegexOptions.Compiled);

    private static readonly string[] ForbiddenTokens =
        ["citationKey", "PackItem", "calc:", "raffa:", "market:", "fact:", "Grounded in:"];

    public static GuardVerdict Validate(
        string? subject,
        string? body,
        IReadOnlyList<string>? usedCitationKeys,
        IReadOnlyList<PackItem> pack,
        int maxBodyChars)
    {
        ArgumentNullException.ThrowIfNull(pack);

        if (string.IsNullOrWhiteSpace(subject))
        {
            return GuardVerdict.Fail("the draft has no subject line.");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return GuardVerdict.Fail("the draft has no body.");
        }

        if (body.Length > maxBodyChars)
        {
            return GuardVerdict.Fail($"the draft body is longer than {maxBodyChars} characters.");
        }

        var text = subject + "\n" + body;

        if (InlineMarkerPattern.Match(text) is { Success: true } marker)
        {
            return GuardVerdict.Fail($"the draft carries an inline citation marker '{marker.Value}' — an email has none.");
        }

        if (LinkPattern.IsMatch(text))
        {
            return GuardVerdict.Fail("the draft carries a link — an email written from the pack has none.");
        }

        if (GuidPattern.IsMatch(text))
        {
            return GuardVerdict.Fail("the draft carries a raw identifier (guid) — never shown to a supplier.");
        }

        foreach (var token in ForbiddenTokens)
        {
            if (text.Contains(token, StringComparison.Ordinal))
            {
                return GuardVerdict.Fail($"the draft carries an internal token '{token}'.");
            }
        }

        var numeric = NumericGuard.Validate(text, pack);
        if (!numeric.Passed)
        {
            return numeric;
        }

        var packKeys = pack.Select(item => item.CitationKey).ToHashSet(StringComparer.Ordinal);
        if (!(usedCitationKeys ?? []).Any(packKeys.Contains))
        {
            return GuardVerdict.Fail("usedCitationKeys names no item of the pack — the draft cites nothing.");
        }

        return GuardVerdict.Ok;
    }

    /// <summary>The used keys that actually exist in <paramref name="pack"/>, in the writer's own
    /// order, without duplicates.</summary>
    public static IReadOnlyList<string> GroundedKeys(IReadOnlyList<string>? usedCitationKeys, IReadOnlyList<PackItem> pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var packKeys = pack.Select(item => item.CitationKey).ToHashSet(StringComparer.Ordinal);
        return (usedCitationKeys ?? []).Where(packKeys.Contains).Distinct(StringComparer.Ordinal).ToList();
    }
}
