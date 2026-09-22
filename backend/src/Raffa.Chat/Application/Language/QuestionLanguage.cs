using System.Text.RegularExpressions;

namespace Raffa.Chat.Application.Language;

/// <summary>
/// Decides which of the two languages the persona answers in (persona law 5: "an Italian question
/// gets an Italian answer; an English question gets an English answer") a <em>deterministic</em>
/// reply should use — the capability-gap preface, the feedback card, the feedback confirmation
/// (ADR-030 D4). The `answer` role decides for itself; this is only for text the server writes
/// without a model call. Pure and synchronous (Appendix C rule 6): two word-boundary lexicons of
/// language-exclusive function words, plus a small bonus for accented vowels; ties and unknown
/// text fall back to English, the language every other deterministic reply already uses.
/// </summary>
public static class QuestionLanguage
{
    public const string Italian = "it";
    public const string English = "en";

    private static readonly Regex ItalianWords = new(
        @"\b(il|lo|la|gli|le|un|una|uno|che|con|per|del|della|dello|delle|degli|dei|nel|nella|sul|sulla|" +
        @"non|sono|devo|dovrei|posso|puoi|potresti|riesci|mi|ci|vorrei|voglio|come|quale|quali|questo|questa|" +
        @"anche|però|perché|scriv\w*|aiutar\w*|aiutami|aiuto|fammi|inviar\w*|invia|mandar\w*|manda|" +
        @"rinnov\w*|contratt\w*|fornitor\w*|trattativ\w*|rinegoziar\w*|negoziar\w*|promemoria|esportar\w*|esporta|" +
        @"leve|leva|risparmi\w*|scadenz\w*|disdetta|preavviso|bozza)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EnglishWords = new(
        @"\b(the|a|an|and|with|for|of|to|in|on|is|are|can|could|would|should|i|me|my|we|our|you|please|" +
        @"help|write|draft|create|send|renewal|contract|supplier|reminder|export|negotiat\w*|leverage|" +
        @"levers?|savings?|deadline|notice|based|email)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AccentedVowels = new(@"[àèéìòù]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>"it" when the Italian lexicon outscores the English one, "en" otherwise.</summary>
    public static string Detect(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return English;
        }

        var italian = ItalianWords.Matches(question).Count + (2 * AccentedVowels.Matches(question).Count);
        var english = EnglishWords.Matches(question).Count;

        return italian > english ? Italian : English;
    }

    public static bool IsItalian(string language) => string.Equals(language, Italian, StringComparison.Ordinal);
}
