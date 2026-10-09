using System.Text.RegularExpressions;

namespace Raffa.AiFlows.CapabilityGaps.Guards;

/// <summary>
/// F4-T01: the server-side scrub of the free text typed in the feedback card, applied before it is
/// quoted in a public GitHub issue (the user does not read the notice, the repository is public).
/// Replaced by a neutral marker so developers still read a sentence: links, e-mail addresses and
/// @handles, phone numbers, IBANs, GUIDs, money amounts and any token with a digit (ids, tax codes,
/// dates, percentages), every name in <c>knownNames</c> (the tenant's suppliers, the submitting
/// user), and any capitalised run that is not a sentence's first word or product vocabulary
/// ("Excel", "CFO"...) -- plus any run of two capitalised words even at the start of a sentence.
/// Pure; no I/O.
/// </summary>
public static class FeatureRequestScrubber
{
    public const string LinkMarker = "[link]";
    public const string EmailMarker = "[email]";
    public const string NumberMarker = "[number]";
    public const string IdMarker = "[id]";
    public const string NameMarker = "[name]";

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly Regex Link = new(@"(?:https?://|ftp://|www\.)\S+|\b[a-z0-9-]+(?:\.[a-z0-9-]+)*\.(?:com|net|org|io|it|eu|de|fr|es|co|ai|app|cloud|dev)\b(?:/\S*)?", Options);
    private static readonly Regex Email = new(@"[^\s@<>()\[\]]+@[^\s@<>()\[\]]+\.[^\s@<>()\[\]]+", Options);
    private static readonly Regex Handle = new(@"(?<![\w.])@[A-Za-z0-9_.-]{2,}", Options);
    private static readonly Regex Guid = new(@"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", Options);
    private static readonly Regex Iban = new(@"\b[A-Z]{2}\d{2}(?:\s?[A-Z0-9]{4}){2,7}(?:\s?[A-Z0-9]{1,4})?\b", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // A token carrying a digit: money ("20k", "20.000", "€15"), dates, years, percentages, ids
    // ("CT-2024-0042"), phone numbers. Currency words next to a number go with it.
    private static readonly Regex Currency = new(
        @"(?:(?:€|\$|£|\b(?:eur|usd|gbp|chf)\b)\s?)?\d[\d.,'’]*\s?(?:k|m|mln|mld|mila|mille|milioni|million|millions|thousand|euro|euros|eur|usd|dollars?|dollari|chf|gbp|sterline|pounds?|€|\$|£)?(?![\p{L}\p{N}])",
        Options);

    private static readonly Regex TokenWithDigit = new(@"[^\s]*\d[^\s]*", Options);
    private static readonly Regex PhoneLike = new(@"(?<![\w])\+?\d[\d\s().\-/]{6,}\d(?![\w])", Options);
    private static readonly Regex Markup = new(@"[`*_#<>{}|\\]", Options);

    private static readonly Regex Title = new(
        @"\b(?:mr|mrs|ms|miss|dr|prof|sig|sig\.ra|signor|signora|dott|dott\.ssa|dottor|dottoressa|avv|ing|geom|rag|herr|frau|monsieur|madame|mme|se[ñn]or|se[ñn]ora|sr|sra)\.?\s+\p{Lu}\p{L}+",
        Options);

    private static readonly Regex Spaces = new(@"[ \t]+", RegexOptions.Compiled);

    // A capitalised word (also hyphenated and apostrophised: "Coca-Cola", "D'Angelo", "McDonald").
    private const string CapitalisedPattern = @"\p{Lu}[\p{L}'’\-]*";
    private static readonly Regex CapitalisedWord = new(CapitalisedPattern, RegexOptions.Compiled);
    private static readonly Regex CapitalisedRun = new($@"{CapitalisedPattern}(?:[ \t]+{CapitalisedPattern})*", RegexOptions.Compiled);

    /// <summary>The product's own vocabulary and generic roles and formats: capitalised, but never a name.</summary>
    private static readonly HashSet<string> SafeWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Raffa", "Raffa.ai", "Ask", "Excel", "Word", "PDF", "CSV", "XLSX", "DOCX", "PPT", "ERP", "PEC", "API", "SSO", "SLA", "KPI", "KPIs", "ROI",
        "CFO", "CEO", "CTO", "CIO", "COO", "CPO", "HR", "IT", "PO", "ODA", "RDA", "FAQ", "OK", "AI",
        "Portfolio", "Renewals", "Documents", "Savings", "Contract", "Quote", "Check", "Market", "Negotiations", "Insights",
        "Outlook", "Gmail", "Slack", "Teams", "Calendar", "Google", "Microsoft", "SAP",
        "I", "A", "An", "The", "Il", "Lo", "La", "Le", "Gli", "Un", "Una", "Uno", "El", "Los", "Las", "Der", "Die", "Das", "Le", "Les",
        "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday",
        "Lunedì", "Martedì", "Mercoledì", "Giovedì", "Venerdì", "Sabato", "Domenica",
        "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December",
        "Gennaio", "Febbraio", "Marzo", "Aprile", "Maggio", "Giugno", "Luglio", "Agosto", "Settembre", "Ottobre", "Novembre", "Dicembre",
        "Q1", "Q2", "Q3", "Q4",
    };

    /// <summary>
    /// Returns <paramref name="text"/> with every link, address, number, identifier and name
    /// removed (see the type's remarks). Empty input gives an empty string; the result is never
    /// longer than the input plus its markers.
    /// </summary>
    /// <param name="text">The user's own words.</param>
    /// <param name="knownNames">Names to remove wherever they occur, case-insensitively and as whole
    /// words — the tenant's suppliers and the submitting user.</param>
    public static string Scrub(string? text, IReadOnlyCollection<string>? knownNames = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var scrubbed = text.Replace("\r\n", "\n", StringComparison.Ordinal);

        scrubbed = Email.Replace(scrubbed, EmailMarker);
        scrubbed = Link.Replace(scrubbed, LinkMarker);
        scrubbed = Handle.Replace(scrubbed, NameMarker);
        scrubbed = Guid.Replace(scrubbed, IdMarker);
        scrubbed = Iban.Replace(scrubbed, IdMarker);
        scrubbed = RemoveKnownNames(scrubbed, knownNames);
        scrubbed = Title.Replace(scrubbed, NameMarker);
        scrubbed = PhoneLike.Replace(scrubbed, NumberMarker);
        scrubbed = Currency.Replace(scrubbed, NumberMarker);
        scrubbed = TokenWithDigit.Replace(scrubbed, IdMarker);
        scrubbed = ReplaceUnknownNames(scrubbed);
        scrubbed = Markup.Replace(scrubbed, " ");

        return Tidy(scrubbed);
    }

    private static string RemoveKnownNames(string text, IReadOnlyCollection<string>? knownNames)
    {
        if (knownNames is null)
        {
            return text;
        }

        foreach (var name in knownNames
                     .Where(n => !string.IsNullOrWhiteSpace(n))
                     .SelectMany(Variants)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(n => n.Length))
        {
            text = Regex.Replace(
                text,
                $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}])",
                NameMarker,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return text;
    }

    /// <summary>A known name and the parts of it people actually type: "Amazon Web Services" is
    /// also "Amazon"; "alice@example.com" is also "alice". Parts shorter than four letters stay
    /// out (a three-letter fragment would erase ordinary words).</summary>
    private static IEnumerable<string> Variants(string name)
    {
        var trimmed = name.Trim();
        yield return trimmed;

        var local = trimmed.Contains('@', StringComparison.Ordinal) ? trimmed[..trimmed.IndexOf('@', StringComparison.Ordinal)] : trimmed;
        if (!ReferenceEquals(local, trimmed) && local.Length >= 3)
        {
            yield return local;
        }

        foreach (var part in Regex.Split(local, @"[\s.,;:_\-]+"))
        {
            if (part.Length >= 4 && part.Any(char.IsLetter))
            {
                yield return part;
            }
        }
    }

    /// <summary>
    /// Replaces every capitalised run that is not product vocabulary and is either not the first
    /// word of its sentence or line (that one is capitalised because it starts one), an acronym or
    /// CamelCase brand, or two or more words long.
    /// </summary>
    private static string ReplaceUnknownNames(string text) =>
        CapitalisedRun.Replace(text, run =>
        {
            var allSafe = run.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(SafeWords.Contains);
            var multiWord = CapitalisedWord.Matches(run.Value).Count > 1;
            return !allSafe && (multiWord || !StartsSentence(text, run.Index) || IsAcronymLike(run.Value))
                ? NameMarker
                : run.Value;
        });

    private static bool StartsSentence(string text, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            var c = text[i];
            if (c is '.' or '!' or '?' or '\n' or ':' or '¿' or '¡')
            {
                return true;
            }

            if (!char.IsWhiteSpace(c) && c is not ('"' or '\'' or '(' or '«' or '“' or '-' or '*' or '•'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>"AWS", "IBM", "AsterCloud": a name even at the start of a sentence.</summary>
    private static bool IsAcronymLike(string word)
    {
        if (word.Length >= 2 && word.All(c => char.IsUpper(c) || c is '-' or '&'))
        {
            return true;
        }

        // CamelCase brand: a capital after the first letter.
        return word.Skip(1).Any(char.IsUpper);
    }

    private static string Tidy(string text)
    {
        var lines = text.Split('\n').Select(line => Spaces.Replace(line, " ").Trim());
        var cleaned = string.Join('\n', lines).Trim();
        return RepeatedMarker.Replace(cleaned, "$1");
    }

    // "[name] [name] [name]" (a three-word company) reads as one "[name]".
    private static readonly Regex RepeatedMarker = new(@"(\[(?:name|number|id|link|email)\])(?:\s*\1)+", RegexOptions.Compiled);
}
