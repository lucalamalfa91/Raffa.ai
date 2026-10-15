using System.Text.RegularExpressions;

namespace Raffa.AiFlows.WebResearch.Lexicons;

/// <summary>
/// The one filter left when the user switches Ask's web search on (ADR-032): a question that is
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
        @"cosa\s+vedere\s+a|what\s+to\s+see\s+in|things\s+to\s+do\s+in|dove\s+mangiare|where\s+to\s+eat|" +
        // French. A bare "recette" is also revenue, so a recipe needs a dish or "de cuisine".
        @"recettes?\s+de\s+cuisine|recettes?\s+(de|du|des)\s+(la\s+)?(p[aâ]tes|pizza|g[aâ]teaux?|tartes?|cr[eê]pes?|lasagnes?|tiramisu)|" +
        @"cuisiner|comment\s+faire\s+cuire|ligue\s+des\s+champions|ligue\s+1|roland-?garros|tour\s+de\s+france|formule\s*1|" +
        @"r[eé]sultats?\s+(du|des)\s+match(s|es)?|match\s+de\s+(foot|football|tennis|basket|rugby)|" +
        @"qui\s+a\s+gagn[eé]\s+(le\s+match|le\s+championnat|la\s+coupe\s+du\s+monde|la\s+finale|le\s+derby)|" +
        @"films?\s+[aà]\s+(voir|regarder)|s[eé]ries?\s+[aà]\s+(voir|regarder)|paroles\s+(de|du)\s+(la\s+)?(chanson|morceau)|" +
        @"horoscope|blague|jeux?\s+vid[eé]o|" +
        @"m[eé]t[eé]o|pr[eé]visions\s+m[eé]t[eé]o|quel\s+temps\s+fait-?il|va-?t-?il\s+pleuvoir|" +
        @"que\s+voir\s+[aà]|choses\s+[aà]\s+faire\s+[aà]|o[uù]\s+manger|" +
        // Spanish. "receta" alone is also a prescription; "el tiempo" is also lead time.
        @"recetas?\s+de\s+(cocina|paella|tortilla|pasta|pizza|postres?|lasa[nñ]a|tiramis[uú]|pollo)|" +
        @"c[oó]mo\s+(se\s+)?cocina|cocinar|liga\s+de\s+campeones|f[oó]rmula\s*1|" +
        @"resultados?\s+del\s+(partido|f[uú]tbol)|partido\s+de\s+(f[uú]tbol|tenis|baloncesto|b[aá]squet)|" +
        @"qui[eé]n\s+gan[oó]\s+(el\s+partido|el\s+campeonato|el\s+mundial|la\s+liga|el\s+cl[aá]sico|la\s+copa)|" +
        @"pel[ií]culas?\s+para\s+ver|series?\s+para\s+ver|letra\s+de\s+la\s+canci[oó]n|hor[oó]scopo|" +
        @"cu[eé]ntame\s+un\s+chiste|videojuegos|" +
        @"pron[oó]stico\s+del\s+tiempo|previsi[oó]n\s+del\s+tiempo|qu[eé]\s+tiempo\s+hace|va\s+a\s+llover|" +
        @"qu[eé]\s+ver\s+en|qu[eé]\s+visitar\s+en|cosas\s+que\s+hacer\s+en|d[oó]nde\s+comer|" +
        // German. "Rezept" alone is also a prescription; "Wetter" alone is not listed (EN does not either).
        @"kochrezept\w*|rezepte?\s+f[uü]r\s+(spaghetti|pasta|pizza|kuchen|torte|lasagne|tiramisu|carbonara|gulasch)|" +
        @"wie\s+(kocht\s+man|backt\s+man|koche\s+ich|backe\s+ich)|bundesliga|formel\s*1|fu(ß|ss)ballspiel\w*|" +
        @"wer\s+hat\s+(das\s+spiel|die\s+meisterschaft|die\s+weltmeisterschaft|den\s+pokal)\s+gewonnen|" +
        @"filme?\s+zum\s+(anschauen|ansehen)|serien?\s+zum\s+(anschauen|ansehen)|songtext\w*|liedtext\w*|" +
        @"erz[aä]hl\s+(mir\s+)?einen\s+witz|horoskop\w*|videospiele?|computerspiele?|" +
        @"wettervorhersage|wetterbericht|wie\s+(wird|ist)\s+das\s+wetter|wird\s+es\s+regnen|" +
        @"sehensw[uü]rdigkeiten|wo\s+kann\s+man\s+(gut\s+)?essen(\s+gehen)?|wo\s+essen\s+gehen)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A business angle anywhere in the sentence keeps it a work question.
    private static readonly Regex WorkPattern = new(
        @"\b(fornitor\w*|supplier\w*|vendor\w*|contratt\w*|contract\w*|procurement|acquist\w*|appalt\w*|" +
        @"gara|gare|tender\w*|catering|mensa|canteen|aziend\w*|company|companies|business|b2b|" +
        @"prezz\w*|costo|costi|cost|costs|pricing|prices?|mercato|market\w*|sponsor\w*|licen[cs]\w*|" +
        @"fattur\w*|invoice\w*|budget|dipendent\w*|employee\w*|logistic\w*|spedizion\w*|shipping|" +
        @"supply\s+chain|filiera|" +
        // fr/es/de (LANG-03): more work words only ever keep a question out of the off-context filter.
        @"fournisseur\w*|contrat\w*|achat\w*|appel\s+d['’]offres?|entreprise\w*|soci[eé]t[eé]\w*|prix|co[uû]ts?|tarif\w*|" +
        @"march[eé]\w*|licence\w*|factur\w*|employ[eé]\w*|salari[eé]\w*|logistique|exp[eé]dition\w*|cantine|" +
        @"restauration\s+collective|cha[iî]ne\s+d['’]approvisionnement|devis|" +
        @"proveedor\w*|compra\w*|licitaci\w*|empresa\w*|compa[ñn][ií]a\w*|precio\w*|coste\w*|costo\w*|mercado|licencia\w*|" +
        @"factura\w*|presupuesto|empleado\w*|log[ií]stica|env[ií]o\w*|comedor|patrocin\w*|cadena\s+de\s+suministro|" +
        @"lieferant\w*|vertr[aä]g\w*|einkauf\w*|ausschreibung\w*|unternehmen|firma|firmen|gesch[aä]ft\w*|preis\w*|kosten|" +
        @"markt\w*|lizenz\w*|rechnung\w*|mitarbeiter\w*|logistik|versand|kantine|lieferkette\w*|angebot\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The question is plainly personal or leisure, with no business angle.</summary>
    public static bool IsOffContext(string question) =>
        !string.IsNullOrWhiteSpace(question)
        && LeisurePattern.IsMatch(question)
        && !WorkPattern.IsMatch(question);
}
