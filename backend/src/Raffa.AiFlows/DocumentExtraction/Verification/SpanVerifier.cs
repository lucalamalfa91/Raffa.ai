using System.Globalization;
using System.Text;
using Raffa.Documents.Contracts.Application.Extraction;

namespace Raffa.AiFlows.DocumentExtraction.Verification;

/// <summary>
/// F5-T07: deterministic, pure check that the evidence span a model quoted for a fact really is in the
/// document, on the page it named. No model, no clock, no I/O.
///
/// Both sides are normalised the same way (NFKC so ligatures and full-width forms fold, soft hyphens and
/// line-end hyphenation removed, diacritics stripped, quotes and dashes folded, whitespace collapsed,
/// case-insensitive), then compared by exact containment and, failing that, by a bounded approximate match:
/// the best edit distance of the span against any substring of the page, as a ratio of the span length,
/// must be at least <see cref="Threshold"/>. The approximate match is Sellers' algorithm over candidate
/// windows found by the pigeonhole principle (a match with at most k errors contains one of k+1 span
/// pieces verbatim), so a long document costs a few string searches, not a full matrix.
///
/// Build once per document (it normalises every page once) and call <see cref="Verify(string?, int?)"/>
/// per fact.
/// </summary>
public sealed class SpanVerifier
{
    public const double DefaultThreshold = 0.85;

    /// <summary>Longest span (normalised characters) compared approximately; longer spans use their prefix.</summary>
    public const int MaxFuzzySpanLength = 300;

    /// <summary>Upper bound on candidate windows verified per page for one span (bounds the worst case).</summary>
    private const int MaxCandidateWindows = 64;

    /// <summary>Upper bound on verbatim hits collected per span piece.</summary>
    private const int MaxHitsPerPiece = 20_000;

    private readonly (int Page, string Text)[] _pages;
    private readonly string _joined;

    public SpanVerifier(IReadOnlyList<DocumentPageText> pages, double threshold = DefaultThreshold)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (threshold is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "Threshold must be in (0, 1].");
        }

        Threshold = threshold;
        _pages = pages.Select(p => (p.PageNumber, Normalize(p.Text))).ToArray();
        _joined = string.Join(' ', _pages.Select(p => p.Text));
    }

    public double Threshold { get; }

    /// <summary>One-shot convenience; prefer an instance when checking several facts of one document.</summary>
    public static SpanVerdict Verify(
        string? span, int? page, IReadOnlyList<DocumentPageText> pages, double threshold = DefaultThreshold) =>
        new SpanVerifier(pages, threshold).Verify(span, page);

    public SpanVerdict Verify(string? span, int? page)
    {
        var needle = Normalize(span);
        if (needle.Length == 0)
        {
            return SpanVerdict.NoSpan;
        }

        var declared = page is { } number ? Array.FindIndex(_pages, p => p.Page == number) : -1;

        if (declared >= 0)
        {
            if (Matches(_pages[declared].Text, needle))
            {
                return SpanVerdict.Found;
            }

            for (var i = 0; i < _pages.Length; i++)
            {
                if (i != declared && Matches(_pages[i].Text, needle))
                {
                    return SpanVerdict.FoundOnOtherPage;
                }
            }
        }
        else
        {
            // No usable page named: there is nothing to contradict, so found anywhere is found.
            foreach (var candidate in _pages)
            {
                if (Matches(candidate.Text, needle))
                {
                    return SpanVerdict.Found;
                }
            }
        }

        // A span that straddles a page break is on no single page but is in the document.
        return _joined.Contains(needle, StringComparison.Ordinal) ? SpanVerdict.Found : SpanVerdict.NotFound;
    }

    private bool Matches(string haystack, string needle)
    {
        if (haystack.Length == 0)
        {
            return false;
        }

        if (haystack.Contains(needle, StringComparison.Ordinal))
        {
            return true;
        }

        return FuzzyMatches(haystack, needle.Length > MaxFuzzySpanLength ? needle[..MaxFuzzySpanLength] : needle);
    }

    private bool FuzzyMatches(string text, string needle)
    {
        var length = needle.Length;
        var maxErrors = (int)Math.Floor((1 - Threshold) * length + 1e-9);
        if (maxErrors == 0 || text.Length < length - maxErrors)
        {
            return false; // exact containment already failed, or the page is too short to hold it
        }

        // Pigeonhole: with k errors, at least one of k+1 disjoint pieces of the span appears verbatim.
        var pieces = maxErrors + 1;
        var pieceLength = length / pieces;
        if (pieceLength == 0)
        {
            return false;
        }

        // Each verbatim piece hit votes for the diagonal (position - piece offset) it implies. A real match
        // collects many votes on nearly the same diagonal; chance hits in repetitive text scatter. The most
        // voted diagonals are verified first, a bounded number of them.
        var votes = new Dictionary<int, int>();
        for (var piece = 0; piece < pieces; piece++)
        {
            var offset = piece * pieceLength;
            var fragment = needle.AsSpan(offset, pieceLength);
            var from = 0;
            var hits = 0;
            while (hits < MaxHitsPerPiece)
            {
                var found = text.AsSpan(from).IndexOf(fragment, StringComparison.Ordinal);
                if (found < 0)
                {
                    break;
                }

                var position = from + found;
                var diagonal = position - offset;
                votes[diagonal] = votes.GetValueOrDefault(diagonal) + 1;
                from = position + 1;
                hits++;
            }
        }

        var tried = new List<int>();
        foreach (var (diagonal, _) in votes.OrderByDescending(v => v.Value).ThenBy(v => v.Key))
        {
            // Diagonals closer than k share almost the whole window: one (wider) verification covers them.
            if (tried.Any(t => Math.Abs(t - diagonal) < maxErrors))
            {
                continue;
            }

            if (tried.Count == MaxCandidateWindows)
            {
                break;
            }

            tried.Add(diagonal);
            var start = Math.Max(0, diagonal - maxErrors);
            var end = Math.Min(text.Length, diagonal + length + 2 * maxErrors);
            if (end > start && BestDistance(needle, text.AsSpan(start, end - start)) <= maxErrors)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Sellers: minimum edit distance of <paramref name="needle"/> against any substring of
    /// <paramref name="window"/> (free start and end in the window).</summary>
    private static int BestDistance(string needle, ReadOnlySpan<char> window)
    {
        var previous = new int[window.Length + 1];
        var current = new int[window.Length + 1];

        for (var i = 1; i <= needle.Length; i++)
        {
            current[0] = i;
            var c = needle[i - 1];
            for (var j = 1; j <= window.Length; j++)
            {
                var substitution = previous[j - 1] + (c == window[j - 1] ? 0 : 1);
                current[j] = Math.Min(substitution, Math.Min(previous[j] + 1, current[j - 1] + 1));
            }

            (previous, current) = (current, previous);
        }

        var best = int.MaxValue;
        foreach (var value in previous)
        {
            best = Math.Min(best, value);
        }

        return best;
    }

    /// <summary>The comparison form of a text: see the type summary. Public so tests can pin it.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decomposed = text.Normalize(NormalizationForm.FormKC).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;

        for (var i = 0; i < decomposed.Length; i++)
        {
            var c = decomposed[i];
            var category = CharUnicodeInfo.GetUnicodeCategory(c);

            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.Format)
            {
                continue; // diacritics, soft hyphen, zero-width characters
            }

            if (IsDash(c))
            {
                // Line-end hyphenation: "agree-\n  ment" reads "agreement".
                var next = i + 1;
                while (next < decomposed.Length && decomposed[next] is ' ' or '\t' or '\r')
                {
                    next++;
                }

                if (next < decomposed.Length && decomposed[next] == '\n' && builder.Length > 0 && char.IsLetter(builder[^1]))
                {
                    var after = next + 1;
                    while (after < decomposed.Length && char.IsWhiteSpace(decomposed[after]))
                    {
                        after++;
                    }

                    if (after < decomposed.Length && char.IsLetter(decomposed[after]))
                    {
                        i = after - 1;
                        pendingSpace = false;
                        continue;
                    }
                }

                c = '-';
            }
            else if (c is '‘' or '’' or '‚' or '‛' or '′' or '`' or '´')
            {
                c = '\'';
            }
            else if (c is '“' or '”' or '„' or '‟' or '″')
            {
                c = '"';
            }

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    private static bool IsDash(char c) =>
        c is '-' or '‐' or '‑' or '‒' or '–' or '—' or '―' or '−';
}
