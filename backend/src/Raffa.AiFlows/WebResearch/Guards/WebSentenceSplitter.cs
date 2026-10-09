using System.Text.RegularExpressions;
using Raffa.Chat.Application.Guards;

namespace Raffa.AiFlows.WebResearch.Guards;

/// <summary>One sentence of a web summary: <see cref="Text"/> spans <c>[Start, End)</c> of the markdown and
/// carries the <c>[n]</c> markers that close it (even when they follow the full stop).</summary>
internal sealed record WebSentence(int Start, int End, string Text);

/// <summary>One line of a web summary: its list prefix (<c>- </c>, <c>1. </c>) and its sentences.</summary>
internal sealed record WebLine(int Start, int End, int ContentStart, IReadOnlyList<WebSentence> Sentences);

/// <summary>
/// Splits a web summary into lines and sentences so a figure can be judged against the markers of its own
/// sentence (F3-T01). A line is a paragraph or a list item; a sentence ends at <c>.</c>, <c>!</c>, <c>?</c>
/// followed by whitespace and a new sentence, never inside <c>7.5%</c>, <c>example.com</c>, <c>approx. 5</c>,
/// <c>Oct. 2026</c> or an initial, and the <c>[n]</c> markers right after the full stop belong to the
/// sentence before it. A wrong split is never a wrong acceptance: it can only leave a figure without its
/// marker, and that sentence is then dropped. Pure.
/// </summary>
internal static class WebSentenceSplitter
{
    private static readonly Regex ListPrefix = new(
        @"^[ \t]*(?:[-*+•][ \t]+|\d{1,2}[.)][ \t]+)?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MarkerAfter = new(@"\G[ \t]*\[\d+\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Words that end in a full stop without ending the sentence.
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "approx", "ca", "cca", "vs", "incl", "excl", "no", "nr", "n", "dr", "mr", "mrs", "ms", "st", "fig", "inc", "ltd",
        "corp", "co", "bzw", "ggf", "vgl", "evtl", "ing", "avv", "sig", "dott", "prof", "tel", "es", "p", "pag", "art",
        "cfr", "eg", "ie", "etc", "mio", "mrd", "mld", "mln", "tsd", "bn", "mn",
    };

    public static IReadOnlyList<WebLine> Split(string markdown)
    {
        var lines = new List<WebLine>();
        var lineStart = 0;
        while (lineStart <= markdown.Length)
        {
            var newline = markdown.IndexOf('\n', lineStart);
            var lineEnd = newline < 0 ? markdown.Length : newline;
            if (lineEnd > lineStart && markdown[lineEnd - 1] == '\r')
            {
                lineEnd--;
            }

            var prefix = ListPrefix.Match(markdown, lineStart, lineEnd - lineStart);
            var contentStart = lineStart + prefix.Length;
            lines.Add(new WebLine(lineStart, lineEnd, contentStart, SplitLine(markdown, contentStart, lineEnd)));

            if (newline < 0)
            {
                break;
            }

            lineStart = newline + 1;
        }

        return lines;
    }

    private static List<WebSentence> SplitLine(string text, int contentStart, int lineEnd)
    {
        var sentences = new List<WebSentence>();
        var start = SkipSpaces(text, contentStart, lineEnd);
        var i = start;
        while (i < lineEnd)
        {
            var c = text[i];
            if (c is not ('.' or '!' or '?' or '…'))
            {
                i++;
                continue;
            }

            var run = i + 1;
            while (run < lineEnd && text[run] is '.' or '!' or '?' or '…')
            {
                run++;
            }

            var close = run;
            while (close < lineEnd && text[close] is '"' or '\'' or '”' or '’' or '»' or ')' or '*' or '_')
            {
                close++;
            }

            var end = close;
            Match marker;
            while ((marker = MarkerAfter.Match(text, end)).Success && marker.Index + marker.Length <= lineEnd)
            {
                end = marker.Index + marker.Length;
            }

            var atEnd = end >= lineEnd;
            if (!atEnd && !char.IsWhiteSpace(text[end]))
            {
                // "7.5%", "example.com", "a.b": the full stop sits inside a token.
                i = run;
                continue;
            }

            if (c == '.' && run == i + 1 && !atEnd && IsAbbreviationOrInitial(text, start, i, end, lineEnd))
            {
                i = run;
                continue;
            }

            sentences.Add(new WebSentence(start, end, text[start..end]));
            start = SkipSpaces(text, end, lineEnd);
            i = start;
        }

        if (start < lineEnd)
        {
            var end = lineEnd;
            while (end > start && char.IsWhiteSpace(text[end - 1]))
            {
                end--;
            }

            if (end > start)
            {
                sentences.Add(new WebSentence(start, end, text[start..end]));
            }
        }

        return sentences;
    }

    private static bool NextWordIsMonth(string text, int from, int lineEnd)
    {
        var start = SkipSpaces(text, from, lineEnd);
        var end = start;
        while (end < lineEnd && char.IsLetter(text[end]))
        {
            end++;
        }

        return end > start && NumericTokenExtractor.MonthNumber(text[start..end]) is not null;
    }

    private static int SkipSpaces(string text, int index, int limit)
    {
        while (index < limit && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }

    // The word before the full stop is an abbreviation or a single initial, or the next word starts in
    // lower case (the sentence goes on): not a boundary.
    private static bool IsAbbreviationOrInitial(string text, int sentenceStart, int dot, int boundaryEnd, int lineEnd)
    {
        var wordStart = dot;
        while (wordStart > sentenceStart && char.IsLetter(text[wordStart - 1]))
        {
            wordStart--;
        }

        // German day ordinal: "am 1. August 2025", "den 15. Januar".
        if (wordStart == dot && dot > sentenceStart && char.IsDigit(text[dot - 1]) && NextWordIsMonth(text, boundaryEnd, lineEnd))
        {
            return true;
        }

        var word = text[wordStart..dot];
        if (word.Length == 1|| Abbreviations.Contains(word) || NumericTokenExtractor.MonthNumber(word) is not null && word.Length <= 4)
        {
            return true;
        }

        var next = SkipSpaces(text, boundaryEnd, lineEnd);
        return next < lineEnd && char.IsLower(text[next]);
    }
}
