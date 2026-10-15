using System.Text;
using System.Text.RegularExpressions;

namespace Raffa.AiFlows.DocumentExtraction.Orchestration;

/// <summary>The chunks of a page-marked text and whether the text had to be cut off.</summary>
internal sealed record DocumentChunks(IReadOnlyList<string> Texts, bool Truncated);

/// <summary>
/// F5-T04: splits a page-marked document text (<c>[[PAGE n]]\n...</c>, see <c>PageMarkedText</c>) into chunks
/// no longer than a budget, cutting at page boundaries. A single page longer than the budget is cut at line
/// boundaries (or, for a line longer than the budget, at the budget) and every continuation piece is
/// re-marked with its page, so a span quoted from any chunk still resolves to the right page and verifies
/// against the document. At most <c>maxChunks</c> chunks are produced; anything beyond is dropped and reported
/// as <see cref="DocumentChunks.Truncated"/>, never silently.
/// </summary>
internal static class DocumentChunker
{
    private static readonly Regex PageMarker = new(@"^\[\[PAGE (\d+)\]\]\n", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    public static DocumentChunks Split(string pageMarkedText, int chunkSizeChars, int maxChunks)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSizeChars, 1000);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChunks, 1);

        // Pieces: (page marker prefix, body) each already <= budget (marker included).
        var pieces = new List<string>();
        foreach (var (marker, body) in Pages(pageMarkedText))
        {
            AddPage(pieces, marker, body, chunkSizeChars);
        }

        var chunks = new List<string>();
        var current = new StringBuilder();
        var truncated = false;
        foreach (var piece in pieces)
        {
            if (current.Length > 0 && current.Length + piece.Length > chunkSizeChars)
            {
                if (chunks.Count + 1 >= maxChunks)
                {
                    truncated = true;
                    break;
                }

                chunks.Add(current.ToString());
                current.Clear();
            }

            current.Append(piece);
        }

        if (current.Length > 0)
        {
            chunks.Add(current.ToString());
        }

        return new DocumentChunks(chunks, truncated);
    }

    private static IEnumerable<(string Marker, string Body)> Pages(string text)
    {
        var matches = PageMarker.Matches(text);
        if (matches.Count == 0)
        {
            yield return (string.Empty, text);
            yield break;
        }

        if (matches[0].Index > 0)
        {
            yield return (string.Empty, text[..matches[0].Index]);
        }

        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            yield return (matches[i].Value, text[start..end]);
        }
    }

    private static void AddPage(List<string> pieces, string marker, string body, int budget)
    {
        if (marker.Length + body.Length <= budget)
        {
            pieces.Add(marker + body);
            return;
        }

        var room = Math.Max(1, budget - marker.Length);
        var offset = 0;
        while (offset < body.Length)
        {
            var take = Math.Min(room, body.Length - offset);
            if (offset + take < body.Length)
            {
                var newline = body.LastIndexOf('\n', offset + take - 1, take);
                if (newline > offset)
                {
                    take = newline + 1 - offset;
                }
            }

            pieces.Add(marker + body.Substring(offset, take));
            offset += take;
        }
    }
}
