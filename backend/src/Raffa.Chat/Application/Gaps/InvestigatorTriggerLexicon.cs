using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Raffa.Chat.Application.Gaps;

/// <summary>
/// The versioned data behind trigger T3 (INV-01, LANG-05): per language, the operational verbs, the
/// request formulas ("puoi", "can you", "peux-tu", "puedes", "kannst du"), the deliverable nouns
/// (excel, report, calendar...) and three veto lists (a third party as the verb's subject, an
/// obligation of the user's own, the text of a clause). The words live in
/// <c>Lexicon/investigator-trigger-lexicon.json</c>, an embedded resource with a <c>version</c> and
/// a changelog, so the product owner reviews content without touching C#; this class only loads it
/// and compiles one whole-word regex per list. Pure; no I/O after the load, no model call.
/// </summary>
public sealed class InvestigatorTriggerLexicon
{
    internal const string ResourceName = "Raffa.Chat.Gaps.investigator-trigger-lexicon.json";

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    /// <summary>How many words before a verb are searched for its subject unless a language says
    /// otherwise (a verb-final language such as German needs a wider window).</summary>
    internal const int DefaultSubjectWindowWords = 4;

    private static readonly Lazy<InvestigatorTriggerLexicon> DefaultInstance = new(LoadEmbedded);

    private InvestigatorTriggerLexicon(string version, IReadOnlyList<LanguageLexicon> languages)
    {
        Version = version;
        Languages = languages;
    }

    /// <summary>The embedded lexicon (the one production uses).</summary>
    public static InvestigatorTriggerLexicon Default => DefaultInstance.Value;

    /// <summary>The file's <c>version</c>; recorded in the trigger's audit row.</summary>
    public string Version { get; }

    /// <summary>Languages in evaluation order (the file's order: it, en, fr, es, de).</summary>
    public IReadOnlyList<LanguageLexicon> Languages { get; }

    /// <summary>Parses a lexicon document (the embedded file, or a candidate under review).</summary>
    public static InvestigatorTriggerLexicon Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var root = document.RootElement;
        var version = root.GetProperty("version").GetString() ?? throw new InvalidDataException("The lexicon has no version.");

        var languages = new List<LanguageLexicon>();
        foreach (var language in root.GetProperty("languages").EnumerateObject())
        {
            var node = language.Value;
            languages.Add(new LanguageLexicon(
                language.Name,
                node.TryGetProperty("status", out var status) ? status.GetString() ?? "seed" : "seed",
                WordList(node, "operationalVerbs"),
                WordList(node, "requestFormulas"),
                WordList(node, "deliverables"),
                WordList(node, "thirdPartySubjects"),
                WordList(node, "obligationCues"),
                WordList(node, "contractTextCues"),
                WordList(node, "politeness"),
                node.TryGetProperty("subjectWindowWords", out var window) && window.TryGetInt32(out var words) && words > 0
                    ? words
                    : DefaultSubjectWindowWords));
        }

        if (languages.Count == 0)
        {
            throw new InvalidDataException("The lexicon has no language.");
        }

        return new InvestigatorTriggerLexicon(version, languages);
    }

    private static InvestigatorTriggerLexicon LoadEmbedded()
    {
        using var stream = typeof(InvestigatorTriggerLexicon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded resource '{ResourceName}' is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static IReadOnlyList<string> WordList(JsonElement node, string name) =>
        node.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList()
            : [];

    /// <summary>Compiles a list of regex fragments into one whole-word alternation; never matches
    /// when the list is empty.</summary>
    internal static Regex Compile(IReadOnlyList<string> fragments) =>
        fragments.Count == 0
            ? new Regex("(?!)", Options)
            : new Regex(@"(?<![\p{L}\p{N}])(?:" + string.Join('|', fragments) + @")(?![\p{L}\p{N}])", Options);

    /// <summary>One language's words, compiled.</summary>
    public sealed class LanguageLexicon
    {
        internal LanguageLexicon(
            string language,
            string status,
            IReadOnlyList<string> verbs,
            IReadOnlyList<string> formulas,
            IReadOnlyList<string> deliverables,
            IReadOnlyList<string> thirdParties,
            IReadOnlyList<string> obligations,
            IReadOnlyList<string> contractCues,
            IReadOnlyList<string> politeness,
            int subjectWindowWords)
        {
            SubjectWindowWords = subjectWindowWords;
            Language = language;
            Status = status;
            Verbs = Compile(verbs);
            Formulas = Compile(formulas);
            Deliverables = Compile(deliverables);
            ThirdParties = Compile(thirdParties);
            Obligations = Compile(obligations);
            ContractCues = Compile(contractCues);
            PolitenessOnly = new Regex(
                @"^[\s\p{P}]*(?:(?:" + (politeness.Count == 0 ? "(?!)" : string.Join('|', politeness)) + @")[\s\p{P}]*)*$",
                Options);
        }

        /// <summary>"it", "en", "fr", "es", "de".</summary>
        public string Language { get; }

        /// <summary>"baseline" (reviewed) or "seed" (structure ready, content awaiting review).</summary>
        public string Status { get; }

        internal int SubjectWindowWords { get; }

        internal Regex Verbs { get; }

        internal Regex Formulas { get; }

        internal Regex Deliverables { get; }

        internal Regex ThirdParties { get; }

        internal Regex Obligations { get; }

        internal Regex ContractCues { get; }

        internal Regex PolitenessOnly { get; }
    }
}
