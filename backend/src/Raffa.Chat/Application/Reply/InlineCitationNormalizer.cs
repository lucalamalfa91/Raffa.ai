using System.Text.RegularExpressions;
using Raffa.Chat.Application.Pack;

namespace Raffa.Chat.Application.Reply;

/// <summary>
/// The reply's last line of defence against a citation key leaking into the prose. The persona
/// prompt (<c>Answering.AnswerPromptV2</c> rule 9) tells the model to cite with <c>[n]</c> markers
/// only, and <c>Guards.GroundingGuard</c> proves every <c>[n]</c> is in range — but a model can
/// still write the key itself (<c>[fact:{contractId}:renewal]</c>, <c>fact:{documentId}:chunk[3]</c>,
/// <c>[tenant:contract-1]</c>) instead of, or next to, the marker. A key is an internal lookup
/// token (R-ASK-08: no id is ever rendered), so before the reply is built:
/// <list type="bullet">
/// <item>a key the model also listed in <c>citationKeys</c> becomes that entry's own <c>[n]</c>;</item>
/// <item>a key that is a real pack item but was not listed is appended to <c>citationKeys</c>
/// (so its card exists) and becomes the new last <c>[n]</c>;</item>
/// <item>a key that resolves to nothing is removed, and the sentence is tidied (no repeated
/// marker, no gap before punctuation, a space between a full stop and a marker).</item>
/// </list>
/// Pure and synchronous (Appendix C rule 6).
/// </summary>
public static class InlineCitationNormalizer
{
    private const string KeyPrefixes = "fact|calc|tenant|market|raffa";

    // "[fact:…:renewal]" / "[tenant:contract-1]" — the whole key in brackets; one level of "[…]"
    // inside the key ("saving[0]", "chunk[3]") is part of it.
    private static readonly Regex BracketedKey = new(
        $@"\[(?<key>(?:{KeyPrefixes}):(?:[^\[\]]|\[[^\]]*\])+)\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // A bare key in running text: "{prefix}:{id}" with an optional ":{field}" tail that stops at
    // whitespace or closing punctuation (a dot inside it, "…].unitPrice", is part of the key). A
    // prefix word on its own ("market:") never matches: at least one id character must follow.
    private static readonly Regex BareKey = new(
        $@"(?<![\[\w])(?<key>(?:{KeyPrefixes}):[^\s:.,;!?()\[\]]+(?::(?:[^\s.,;:!?()\[\]]|\.(?=\w)|\[[^\]]*\])+)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RepeatedMarker = new(@"(\[\d+\])(?:\s*\1)+", RegexOptions.Compiled);
    private static readonly Regex MarkerGluedToPunctuation = new(@"([.,;:!?])(\[\d+\])", RegexOptions.Compiled);
    private static readonly Regex SpaceBeforePunctuation = new(@"[ \t]+([.,;:!?])", RegexOptions.Compiled);
    private static readonly Regex DoubleSpace = new(@"[ \t]{2,}", RegexOptions.Compiled);
    private static readonly Regex TrailingLineSpace = new(@"[ \t]+$", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Rewrites <paramref name="markdown"/> so no citation key survives in it, returning the
    /// rewritten text and the (possibly extended) citation-key list the reply's cards are built
    /// from. Order-preserving: an existing key keeps its number.
    /// </summary>
    public static (string Markdown, IReadOnlyList<string> CitationKeys) Normalize(
        string? markdown, IReadOnlyList<string> citationKeys, IReadOnlyList<PackItem> pack)
    {
        ArgumentNullException.ThrowIfNull(citationKeys);
        ArgumentNullException.ThrowIfNull(pack);

        var keys = new List<string>(citationKeys);
        if (string.IsNullOrEmpty(markdown))
        {
            return (markdown ?? string.Empty, keys);
        }

        var packKeys = pack.Select(item => item.CitationKey).ToHashSet(StringComparer.Ordinal);

        string Resolve(string key, bool alwaysRemove)
        {
            var index = keys.FindIndex(existing => string.Equals(existing, key, StringComparison.Ordinal));
            if (index >= 0)
            {
                return $"[{index + 1}]";
            }

            if (packKeys.Contains(key))
            {
                keys.Add(key);
                return $"[{keys.Count}]";
            }

            // A "fact:"/"calc:" key is never legitimate prose; another prefix that resolves to
            // nothing may be an ordinary word followed by a colon, so it stays as written.
            var internalPrefix = key.StartsWith("fact:", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("calc:", StringComparison.OrdinalIgnoreCase);
            return alwaysRemove || internalPrefix ? string.Empty : key;
        }

        var rewritten = BracketedKey.Replace(markdown, match => Resolve(match.Groups["key"].Value, alwaysRemove: true));
        rewritten = BareKey.Replace(rewritten, match => Resolve(match.Groups["key"].Value, alwaysRemove: false));

        return (Tidy(rewritten), keys);
    }

    private static string Tidy(string text)
    {
        var tidy = RepeatedMarker.Replace(text, "$1");
        tidy = MarkerGluedToPunctuation.Replace(tidy, "$1 $2");
        tidy = SpaceBeforePunctuation.Replace(tidy, "$1");
        tidy = DoubleSpace.Replace(tidy, " ");
        tidy = TrailingLineSpace.Replace(tidy, string.Empty);
        return tidy.Trim();
    }
}
