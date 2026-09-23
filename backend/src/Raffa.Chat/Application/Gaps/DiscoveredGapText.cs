using System.Text;
using System.Text.RegularExpressions;

namespace Raffa.Chat.Application.Gaps;

/// <summary>
/// The server-side half of ADR-031's privacy rule: the texts of a discovered gap are model-written
/// from the user's own words and end up in a public GitHub issue, so the prompt's law is not
/// trusted alone. Every feature text is cleaned here before it is shown or stored — links, e-mail
/// addresses, every token that carries a digit (amounts, dates, years, "Q3"), every supplier name
/// this tenant has on file and every markdown character are removed — and a text that is empty
/// once cleaned makes the whole discovery unusable (the turn is answered as if nothing was found).
/// Pure; no I/O.
/// </summary>
public static class DiscoveredGapText
{
    public const int MaxSlugLength = 28;
    public const int MaxTitleLength = 80;
    public const int MaxOperationLength = 100;
    public const int MaxDescriptionLength = 240;
    public const int MaxQuestionLength = 160;
    public const int MaxAlternativeQuestions = 2;

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly Regex Link = new(@"(https?://|www\.)\S+", Options);
    private static readonly Regex Email = new(@"\S+@\S+", Options);
    private static readonly Regex TokenWithDigit = new(@"[^\s]*\d[^\s]*", Options);
    private static readonly Regex Markup = new(@"[*_`#<>\[\]{}|\\]", Options);
    private static readonly Regex Spaces = new(@"\s+", Options);
    private static readonly Regex SpaceBeforePunctuation = new(@"\s+([,.;:!?»”)])", Options);
    private static readonly Regex SlugUnsafe = new(@"[^a-z0-9]+", Options);

    /// <summary>A feature text (title, operation, description), cleaned and bounded, or
    /// <see langword="null"/> when nothing meaningful survives.</summary>
    public static string? Clean(string? text, IReadOnlyCollection<string> knownSupplierNames, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(knownSupplierNames);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = Link.Replace(text, " ");
        cleaned = Email.Replace(cleaned, " ");
        cleaned = TokenWithDigit.Replace(cleaned, " ");
        cleaned = RemoveSupplierNames(cleaned, knownSupplierNames);
        cleaned = Markup.Replace(cleaned, " ");
        return Finish(cleaned, maxLength);
    }

    /// <summary>A suggested follow-up question. It goes back into Ask as the user's own next
    /// message and never into an issue, so a supplier name or a number may stay; links, e-mail
    /// addresses and markup may not.</summary>
    public static string? CleanQuestion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = Link.Replace(text, " ");
        cleaned = Email.Replace(cleaned, " ");
        cleaned = Markup.Replace(cleaned, " ");
        return Finish(cleaned, MaxQuestionLength);
    }

    /// <summary>The feature key as a kebab-case slug of at most <see cref="MaxSlugLength"/>
    /// characters (the stored key is <c>discovered:</c> plus this, inside the 40-character
    /// column), with every segment that carries a digit dropped; falls back to a slug of
    /// <paramref name="fallbackTitle"/>, then to <c>feature-request</c>.</summary>
    public static string Slug(string? key, string? fallbackTitle)
    {
        return SlugOf(key) ?? SlugOf(fallbackTitle) ?? "feature-request";

        static string? SlugOf(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var segments = SlugUnsafe.Replace(text.Trim().ToLowerInvariant(), "-")
                .Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Where(segment => !segment.Any(char.IsDigit));

            var builder = new StringBuilder();
            foreach (var segment in segments)
            {
                var next = builder.Length == 0 ? segment.Length : builder.Length + 1 + segment.Length;
                if (next > MaxSlugLength)
                {
                    break;
                }

                if (builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(segment);
            }

            return builder.Length >= 3 ? builder.ToString() : null;
        }
    }

    private static string RemoveSupplierNames(string text, IReadOnlyCollection<string> knownSupplierNames)
    {
        foreach (var name in knownSupplierNames.Where(n => !string.IsNullOrWhiteSpace(n)).OrderByDescending(n => n.Length))
        {
            text = Regex.Replace(
                text,
                $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(name.Trim())}(?![\p{{L}}\p{{N}}])",
                " ",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return text;
    }

    private static string? Finish(string text, int maxLength)
    {
        var cleaned = Spaces.Replace(text, " ").Trim();
        cleaned = SpaceBeforePunctuation.Replace(cleaned, "$1");
        cleaned = cleaned.Trim(' ', ',', ';', ':', '-', '–', '—', '(', '«', '“', '"', '\'');

        if (cleaned.Length > maxLength)
        {
            var cut = cleaned.LastIndexOf(' ', maxLength);
            cleaned = (cut > 0 ? cleaned[..cut] : cleaned[..maxLength]).TrimEnd(' ', ',', ';', ':', '-');
        }

        return cleaned.Count(char.IsLetter) >= 3 ? cleaned : null;
    }
}
