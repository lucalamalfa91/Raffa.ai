using System.Text.RegularExpressions;

namespace Raffa.Chat.Application.WebResearch;

/// <summary>
/// The one filter left when the user switches Ask's web search on (ADR-031): a question that is
/// plainly personal or leisure — a recipe, a match result, the weather, a joke, a film — is not
/// Raffa's to research, and gets pointed at a general search engine or an AI search assistant
/// instead. Everything else goes to the web and to Raffa's own store, whatever the procurement
/// lexicons would have said. Deliberately narrow: a leisure word next to a work word ("catering
/// supplier for the canteen: recipes and prices") is a work question. The research persona's own
/// <c>offTopic</c> verdict (<see cref="WebResearchPrompt.OpenSystemPrompt"/>) is the second line.
/// Pure and synchronous, IT/EN side by side like every other lexicon in this module.
/// </summary>
public static class WebModeLexicon
{
    /// <summary>The research purpose of a web-mode turn: selects the open persona
    /// (<see cref="WebResearchPrompt.OpenSystemPrompt"/>) instead of the four procurement purposes
    /// of <see cref="WebResearchTopicLexicon"/>.</summary>
    public const string Purpose = "Open";

    private static readonly Regex LeisurePattern = new(
        // Cooking.
        @"\b(ricett[ae]|recipes?|cucinare|come\s+si\s+cucina|how\s+(do\s+i|to)\s+cook|carbonara|amatriciana|" +
        @"lasagn[ae]|tiramis[uù]|parmigiana\s+di\s+melanzane|" +
        // Sport.
        @"champions\s+league|premier\s+league|formula\s*(1|uno)|motogp|fantacalcio|calciomercato|scudetto|" +
        @"risultat[oi]\s+(della|delle|di)\s+partit[ae]|partita\s+(di|della)\s+(calcio|tennis|basket|pallavolo)|" +
        @"chi\s+ha\s+vinto\s+(la\s+partita|il\s+campionato|lo\s+scudetto|il\s+derby|la\s+champions|il\s+mondiale)|" +
        @"who\s+won\s+the\s+(game|match|championship|world\s+cup|super\s+bowl|derby)|football\s+scores?|" +
        // Entertainment and small talk.
        @"film\s+da\s+vedere|movies?\s+to\s+watch|serie\s+tv\s+da\s+vedere|tv\s+shows?\s+to\s+watch|" +
        @"testo\s+della\s+canzone|song\s+lyrics|lyrics\s+(of|to)|barzellett[ae]|tell\s+me\s+a\s+joke|" +
        @"oroscopo|horoscope|gossip|videogiochi|video\s*games?|" +
        // Weather.
        @"meteo|previsioni\s+del\s+tempo|che\s+tempo\s+fa|weather\s+forecast|will\s+it\s+rain|" +
        // Leisure travel and going out.
        @"cosa\s+vedere\s+a|what\s+to\s+see\s+in|things\s+to\s+do\s+in|dove\s+mangiare|where\s+to\s+eat)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A business angle anywhere in the sentence keeps it a work question.
    private static readonly Regex WorkPattern = new(
        @"\b(fornitor\w*|supplier\w*|vendor\w*|contratt\w*|contract\w*|procurement|acquist\w*|appalt\w*|" +
        @"gara|gare|tender\w*|catering|mensa|canteen|aziend\w*|company|companies|business|b2b|" +
        @"prezz\w*|costo|costi|cost|costs|pricing|prices?|mercato|market\w*|sponsor\w*|licen[cs]\w*|" +
        @"fattur\w*|invoice\w*|budget|dipendent\w*|employee\w*|logistic\w*|spedizion\w*|shipping|" +
        @"supply\s+chain|filiera)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The question is plainly personal or leisure, with no business angle.</summary>
    public static bool IsOffContext(string question) =>
        !string.IsNullOrWhiteSpace(question)
        && LeisurePattern.IsMatch(question)
        && !WorkPattern.IsMatch(question);
}
