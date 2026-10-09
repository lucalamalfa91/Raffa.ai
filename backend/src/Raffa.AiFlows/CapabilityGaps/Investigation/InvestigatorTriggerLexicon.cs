using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Raffa.AiFlows.CapabilityGaps.Investigation;

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
    internal const string ResourceName = "Raffa.AiFlows.CapabilityGaps.investigator-trigger-lexicon.json";

    // Interpreted, not compiled: ~170 alternations build in a few milliseconds instead of
    // paying a one-off compile on the first turn's thread, and a question is a few hundred
    // characters, so matching is microseconds either way.
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

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
            var politeness = WordList(node, "politeness");

            // A verb-final language such as German needs a wider window than the default of four words.
            languages.Add(new LanguageLexicon(
                language.Name,
                node.TryGetProperty("status", out var status) ? status.GetString() ?? "seed" : "seed",
                Compile(WordList(node, "operationalVerbs")),
                Compile(WordList(node, "requestFormulas")),
                Compile(WordList(node, "deliverables")),
                Compile(WordList(node, "thirdPartySubjects")),
                Compile(WordList(node, "obligationCues")),
                Compile(WordList(node, "contractTextCues")),
                new Regex(@"^[\s\p{P}]*(?:(?:" + (politeness.Count == 0 ? "(?!)" : string.Join('|', politeness)) + @")[\s\p{P}]*)*$", Options),
                node.TryGetProperty("subjectWindowWords", out var window) && window.TryGetInt32(out var words) && words > 0 ? words : 4));
        }

        if (languages.Count == 0)
        {
            throw new InvalidDataException("The lexicon has no language.");
        }

        return new InvestigatorTriggerLexicon(version, languages);
    }

    /// <summary>The embedded file, or — should it ever be missing or unreadable — an empty lexicon
    /// named "unavailable": trigger T3 then never fires (T1 and T2 still do) and Ask carries on.
    /// The investigator is fail-open by design; a broken data file must never take a turn down.
    /// The tests assert that the shipped file loads, so the fallback cannot hide a bad build.</summary>
    private static InvestigatorTriggerLexicon LoadEmbedded()
    {
        try
        {
            using var stream = typeof(InvestigatorTriggerLexicon).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"The embedded resource '{ResourceName}' is missing.");
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or System.Text.Json.JsonException or KeyNotFoundException or ArgumentException or RegexParseException)
        {
            return new InvestigatorTriggerLexicon("unavailable", []);
        }
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

    /// <summary>One language's words, compiled. <paramref name="Language"/> is "it", "en", "fr", "es" or
    /// "de"; <paramref name="Status"/> is "baseline" (reviewed) or "seed" (structure ready, content
    /// awaiting review); <paramref name="SubjectWindowWords"/> is how many words before a verb are
    /// searched for its subject.</summary>
    public sealed record LanguageLexicon(
        string Language,
        string Status,
        Regex Verbs,
        Regex Formulas,
        Regex Deliverables,
        Regex ThirdParties,
        Regex Obligations,
        Regex ContractCues,
        Regex PolitenessOnly,
        int SubjectWindowWords);
}
