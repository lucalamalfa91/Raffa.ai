namespace Raffa.AiGateway.Agents;

/// <summary>
/// Implemented by a step input that knows the language the answer must come in (plan LANG-04): the
/// ISO 639-1 code of the user's question (<c>it</c>, <c>en</c>, <c>fr</c>, <c>es</c>, <c>de</c>), or
/// <see langword="null"/> when unknown. Optional: an input that does not implement it gets the
/// "language of the question" directive.
/// </summary>
public interface ILanguageAware
{
    string? Language { get; }
}

/// <summary>
/// The versioned response-language directives (plan LANG-04, T-06): one text per supported language
/// (IT/EN/FR/ES/DE) plus the language-agnostic "language of the question" text used when the language
/// is unknown. A prompt opts in with <see cref="AgentDefinition.WithResponseLanguageDirective"/>; the
/// runner then appends the directive for the input's <see cref="ILanguageAware.Language"/> after the
/// prompt. <b>Default off</b>: no definition has it unless it asks, and no existing prompt text changes.
///
/// <para>
/// The texts are code, frozen by <see cref="Version"/> and pinned by <see cref="Hash"/> in a test, so
/// editing one without bumping the version fails a test (the same drift rule as prompt files).
/// </para>
/// </summary>
public static class LanguageDirectives
{
    /// <summary>The directive set's version; bump it with any text change and re-pin the hashes.</summary>
    public const string Version = "lang-v1";

    /// <summary>The languages with a dedicated directive.</summary>
    public static IReadOnlyList<string> Supported { get; } = ["it", "en", "fr", "es", "de"];

    private const string Keep =
        " Keep identifiers, citation keys, enum values and numbers exactly as given; do not translate them.";

    private static readonly Dictionary<string, string> Texts = new(StringComparer.Ordinal)
    {
        ["auto"] = "Language: write every free-text field in the language of the user's question." + Keep,
        ["it"] = "Language: write every free-text field in Italian (italiano), whatever language the input or these instructions are in." + Keep,
        ["en"] = "Language: write every free-text field in English, whatever language the input or these instructions are in." + Keep,
        ["fr"] = "Language: write every free-text field in French (francais), whatever language the input or these instructions are in." + Keep,
        ["es"] = "Language: write every free-text field in Spanish (espanol), whatever language the input or these instructions are in." + Keep,
        ["de"] = "Language: write every free-text field in German (Deutsch), whatever language the input or these instructions are in." + Keep,
    };

    /// <summary>The key a language code resolves to: its 2-letter lower-case code when supported
    /// (<c>it-IT</c>, <c>IT</c> and <c>it_it</c> are <c>it</c>), otherwise <c>auto</c>.</summary>
    public static string Resolve(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return "auto";
        }

        var code = language.Trim().Split('-', '_')[0].ToLowerInvariant();
        return Texts.ContainsKey(code) && code != "auto" ? code : "auto";
    }

    /// <summary>The directive for <paramref name="language"/> (the "language of the question" one when
    /// null or unsupported).</summary>
    public static string For(string? language) => Texts[Resolve(language)];

    /// <summary>SHA-256 of the directive's text, as <see cref="AgentDefinition.HashPrompt"/> computes it.</summary>
    public static string Hash(string? language) => AgentDefinition.HashPrompt(For(language));

    /// <summary>Every directive key, <c>auto</c> first: for the drift test.</summary>
    public static IReadOnlyList<string> Keys { get; } = ["auto", .. Supported];
}
