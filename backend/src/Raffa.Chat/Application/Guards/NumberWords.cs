using System.Globalization;
using System.Text;

namespace Raffa.Chat.Application.Guards;

/// <summary>
/// Whole numbers written as words in the five supported languages, 0 to 999 (F3-D02): <c>ten</c>,
/// <c>twenty-five</c>, <c>dieci</c>, <c>venticinque</c>, <c>vingt-cinq</c>, <c>quatre-vingt-dix</c>,
/// <c>treinta y cinco</c>, <c>fünfundzwanzig</c>, <c>zwei hundert</c>. Used only to read a percentage
/// the model spelled out ("ten percent") so it is checked like "10%" — never to invent a value: a
/// phrase that is not wholly number words is not a number. Pure.
/// </summary>
internal static class NumberWords
{
    private const int Hundred = -100;

    // Lower case, diacritics removed, ß as ss. Values below 100; Hundred marks the multiplier.
    private static readonly Dictionary<string, int> Pieces = BuildPieces();

    private static readonly HashSet<string> Connectors = new(StringComparer.Ordinal) { "and", "et", "y", "und", "e" };

    /// <summary>The value of <paramref name="words"/> (a phrase already split on spaces; hyphens inside a
    /// word are allowed), or <see langword="false"/> when any word is not a number word or the phrase is
    /// not a well-formed number.</summary>
    public static bool TryEvaluate(IReadOnlyList<string> words, out int value)
    {
        value = 0;
        var values = new List<int>();
        foreach (var word in words)
        {
            var normalized = Normalize(word);
            if (Connectors.Contains(normalized))
            {
                continue;
            }

            foreach (var part in normalized.Split('-', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!TrySegment(part, out var segment))
                {
                    return false;
                }

                values.AddRange(segment);
            }
        }

        if (values.Count == 0)
        {
            return false;
        }

        // French "quatre-vingt(s)" is eighty, not four plus twenty.
        for (var i = 0; i + 1 < values.Count; i++)
        {
            if (values[i] == 4 && values[i + 1] == 20)
            {
                values[i] = 80;
                values.RemoveAt(i + 1);
            }
        }

        var total = 0;
        var current = 0;
        foreach (var piece in values)
        {
            if (piece == Hundred)
            {
                current = (current == 0 ? 1 : current) * 100;
                total += current;
                current = 0;
            }
            else
            {
                current += piece;
            }
        }

        total += current;
        if (total is < 0 or > 999)
        {
            return false;
        }

        value = total;
        return true;
    }

    /// <summary>Whether <paramref name="word"/> is wholly number words (a compound counts).</summary>
    public static bool IsNumberWord(string word)
    {
        var normalized = Normalize(word);
        if (normalized.Length == 0)
        {
            return false;
        }

        return normalized.Split('-', StringSplitOptions.RemoveEmptyEntries).All(part => TrySegment(part, out _));
    }

    /// <summary>Whether <paramref name="word"/> is the "and" of a spelled number (<c>hundred and fifty</c>,
    /// <c>treinta y cinco</c>, <c>vingt et un</c>).</summary>
    public static bool IsConnector(string word) => Connectors.Contains(Normalize(word));

    /// <summary>The first piece's value, or a large number for a hundred: lets a caller tell "tens or
    /// more" from "units" when deciding whether a connector joins two parts of one number.</summary>
    public static int LeadValue(string word)
    {
        var normalized = Normalize(word);
        return TrySegment(normalized.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty, out var segment) && segment.Count > 0
            ? (segment[0] == Hundred ? 100 : segment[0])
            : -1;
    }

    /// <summary>The last piece's value of <paramref name="word"/>.</summary>
    public static int TailValue(string word)
    {
        var normalized = Normalize(word);
        var last = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
        return TrySegment(last, out var segment) && segment.Count > 0
            ? (segment[^1] == Hundred ? 100 : segment[^1])
            : -1;
    }

    // Segments one unhyphenated word into pieces by longest-first search with backtracking; German
    // "und" between two pieces is skipped ("fünfundzwanzig" = five, and, twenty).
    private static bool TrySegment(string part, out List<int> segment)
    {
        segment = [];
        if (part.Length == 0 || part.Length > 40)
        {
            return false;
        }

        var result = Search(part, 0);
        if (result is null)
        {
            return false;
        }

        segment = result;
        return true;
    }

    private static List<int>? Search(string text, int start)
    {
        if (start == text.Length)
        {
            return [];
        }

        if (string.CompareOrdinal(text, start, "und", 0, 3) == 0 && start > 0 && start + 3 < text.Length)
        {
            var skipped = Search(text, start + 3);
            if (skipped is not null)
            {
                return skipped;
            }
        }

        for (var length = Math.Min(14, text.Length - start); length >= 1; length--)
        {
            if (!Pieces.TryGetValue(text.Substring(start, length), out var piece))
            {
                continue;
            }

            var rest = Search(text, start + length);
            if (rest is not null)
            {
                rest.Insert(0, piece);
                return rest;
            }
        }

        return null;
    }

    private static string Normalize(string word)
    {
        var decomposed = word.Trim().Trim('.', ',', ';', ':', '(', ')', '"', '\'', '«', '»').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(c == 'ß' ? "ss" : char.ToLowerInvariant(c).ToString());
        }

        return builder.ToString();
    }

    private static Dictionary<string, int> BuildPieces()
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);

        void Add(int value, params string[] words)
        {
            foreach (var word in words)
            {
                map.TryAdd(word, value);
            }
        }

        Add(0, "zero", "cero", "null");
        Add(1, "one", "uno", "una", "un", "une", "ein", "eins", "eine");
        Add(2, "two", "due", "deux", "dos", "zwei");
        Add(3, "three", "tre", "trois", "tres", "drei");
        Add(4, "four", "quattro", "quatre", "cuatro", "vier");
        Add(5, "five", "cinque", "cinq", "cinco", "funf", "fuenf");
        Add(6, "six", "sei", "seis", "sechs");
        Add(7, "seven", "sette", "sept", "siete", "sieben");
        Add(8, "eight", "otto", "huit", "ocho", "acht");
        Add(9, "nine", "nove", "neuf", "nueve", "neun");
        Add(10, "ten", "dieci", "dix", "diez", "zehn");
        Add(11, "eleven", "undici", "onze", "once", "elf");
        Add(12, "twelve", "dodici", "douze", "doce", "zwolf", "zwoelf");
        Add(13, "thirteen", "tredici", "treize", "trece", "dreizehn");
        Add(14, "fourteen", "quattordici", "quatorze", "catorce", "vierzehn");
        Add(15, "fifteen", "quindici", "quinze", "quince", "funfzehn", "fuenfzehn");
        Add(16, "sixteen", "sedici", "seize", "dieciseis", "sechzehn");
        Add(17, "seventeen", "diciassette", "diecisiete", "siebzehn");
        Add(18, "eighteen", "diciotto", "dieciocho", "achtzehn");
        Add(19, "nineteen", "diciannove", "diecinueve", "neunzehn");
        Add(20, "twenty", "venti", "vent", "vingt", "veinte", "veinti", "zwanzig");
        Add(30, "thirty", "trenta", "trent", "trente", "treinta", "dreissig", "dreizig");
        Add(40, "forty", "quaranta", "quarant", "quarante", "cuarenta", "vierzig");
        Add(50, "fifty", "cinquanta", "cinquant", "cinquante", "cincuenta", "funfzig", "fuenfzig");
        Add(60, "sixty", "sessanta", "sessant", "soixante", "sesenta", "sechzig");
        Add(70, "seventy", "settanta", "settant", "setenta", "siebzig");
        Add(80, "eighty", "ottanta", "ottant", "ochenta", "achtzig");
        Add(90, "ninety", "novanta", "novant", "noventa", "neunzig");
        Add(Hundred, "hundred", "cento", "cent", "cents", "cien", "ciento", "cientos", "hundert");

        return map;
    }
}
