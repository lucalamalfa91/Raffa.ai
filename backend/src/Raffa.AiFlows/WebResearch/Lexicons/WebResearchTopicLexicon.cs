using System.Text.RegularExpressions;

namespace Raffa.AiFlows.WebResearch.Lexicons;

/// <summary>
/// The two deterministic lexicons of ADR-030's web research: the explicit request ("cerca sul
/// web", "search the web", "look it up online") that becomes <c>AskIntent.WebResearch</c>, and the
/// procurement topics the research persona is allowed to serve — market practice, supplier news,
/// public benchmark ranges, negotiation tactics. A question that matches no topic is never offered
/// the web, and the persona itself refuses anything else a second time (<c>offTopic</c>). Pure and
/// synchronous, IT/EN side by side like every other lexicon in this module.
/// </summary>
public static class WebResearchTopicLexicon
{
    /// <summary>Purpose: "what do buyers usually get / what is common practice".</summary>
    public const string MarketPractice = "MarketPractice";

    /// <summary>Purpose: public news about a named supplier (acquisitions, price lists, outages).</summary>
    public const string SupplierNews = "SupplierNews";

    /// <summary>Purpose: a public price/uplift/discount range, never this tenant's figures.</summary>
    public const string BenchmarkRange = "BenchmarkRange";

    /// <summary>Purpose: negotiation levers and tactics buyers cite.</summary>
    public const string NegotiationTactics = "NegotiationTactics";

    public static readonly IReadOnlyList<string> Purposes =
        [MarketPractice, SupplierNews, BenchmarkRange, NegotiationTactics];

    // "cerca sul web / in rete / su internet", "search the web / online", "look it up online",
    // "google it" — the user is explicitly asking to leave the tenant's data.
    private static readonly Regex ExplicitIntentPattern = new(
        @"\b(cerca(re|mi|lo|la)?\s+(sul|nel|in|su)\s+(web|internet|rete|google)|" +
        @"ricerca\s+(sul\s+|in\s+|su\s+)?(web|internet|rete|online)|" +
        @"(guarda|controlla|verifica|cerca)\s+(su\s+)?(internet|online|in\s+rete)|" +
        @"su\s+internet|sul\s+web|online\s+search|web\s+search|" +
        @"search(\s+for\s+it|\s+it)?\s+(on\s+)?(the\s+)?(web|internet|online)|" +
        @"look(\s+it|\s+this|\s+that)?\s+up\s+(online|on\s+the\s+web|on\s+the\s+internet)|" +
        @"check\s+(online|the\s+web|the\s+internet)|google\s+(it|this|that)|" +
        @"from\s+the\s+(public\s+)?web|dal\s+web|dalla\s+rete|" +
        // fr/es/de (LANG-03). Verb forms only: a bare "sur internet" / "en internet" / "im Internet"
        // also occurs in ordinary sentences ("les tarifs trouvés sur internet sont...").
        @"cherch(e|er|ez)(-moi|-le|-la)?\s+(sur|dans)\s+(le\s+)?(web|internet|google)|" +
        @"recherch(e|er|ez)\s+(sur|en)\s+(le\s+)?(web|internet|ligne)|recherche\s+web|" +
        @"(v[eé]rifi(e|er|ez)|regard(e|ez)|cherch(e|ez))\s+(sur\s+)?(internet|en\s+ligne)|" +
        @"googl(e|ez|er)\s+([cç]a|cela|ceci)|depuis\s+(le\s+)?(web|internet)|" +
        @"busc(a|ar)(lo|la|me)?\s+(en|por)\s+(la\s+)?(web|internet|red|google)|" +
        @"b[uú]squeda\s+(en\s+)?(la\s+)?(web|internet|red|l[ií]nea)|" +
        @"(mira|comprueba|verifica|consulta|busca)\s+(en\s+)?(internet|l[ií]nea|la\s+red)|" +
        @"googlea(lo|r)?|desde\s+(la\s+)?(web|internet)|" +
        @"(such(e|en)?|recherchier(e|en)?)\s+((das|es|danach)\s+)?(im|auf)\s+(internet|web|netz)|" +
        @"(such(e|en)?|recherchier(e|en)?)\s+((das|es)\s+)?online|online[\s-]recherche|internetrecherche|web[\s-]?suche|" +
        @"google\s+(das|es|danach)|(guck|schau)\s+(im\s+)?(internet|web)\s+nach|aus\s+dem\s+(internet|web))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SupplierNewsPattern = new(
        @"\b(news|notizie|novit[àa]|announc\w*|annunci\w*|acquisition\w*|acquisizion\w*|merger\w*|fusion\w*|" +
        @"outage\w*|disservizi\w*|layoffs?|price\s+list\w*|listin\w*|roadmap|end[\s-]of[\s-]life|eol|" +
        @"deprecat\w*|dismission\w*|rebrand\w*|lawsuit|breach\w*|violazion\w*|" +
        // fr/es/de (LANG-03)
        @"actualit[eé]s?|nouvelles|annonce\w*|panne\w*|licenciement\w*|liste\s+de\s+prix|feuille\s+de\s+route|" +
        @"fin\s+de\s+vie|faille|rachat|violation\w*|" +
        @"noticias|novedades|anuncio\w*|adquisici[oó]n|adquisiciones|fusi[oó]n|fusiones|interrupci[oó]n\w*|despidos|" +
        @"lista\s+de\s+precios|hoja\s+de\s+ruta|fin\s+de\s+vida|brecha\s+de\s+seguridad|" +
        @"nachrichten|neuigkeiten|ank[uü]ndigung\w*|[uü]bernahme\w*|ausfall|ausf[aä]lle|st[oö]rung\w*|entlassung\w*|" +
        @"preisliste\w*|datenpanne|sicherheitsvorfall)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BenchmarkRangePattern = new(
        @"\b(benchmark\w*|range|fascia|forchetta|typical\s+(price|uplift|discount|increase)\w*|" +
        @"prezz[oi]\s+(medi[oi]|tipic[oi]|di\s+mercato)|uplift\w*|aumento\s+(dei\s+|di\s+)?prezz\w*|" +
        @"price\s+increase\w*|rincar\w*|list\s+price\w*|discount\s+(rate|range|level)\w*|" +
        @"sconto\s+(medio|tipico)|quanto\s+costa|how\s+much\s+does|going\s+rate|market\s+rate\w*|" +
        @"tariff\w*|rate\s+card\w*|cost\s+per\s+(user|seat|licen[cs]e)|costo\s+per\s+(utente|licenz\w*)|" +
        // fr/es/de (LANG-03)
        @"fourchette\w*|gamme\s+de\s+prix|prix\s+(moyens?|typiques?|du\s+march[eé]|habituels?|catalogue|publics?)|" +
        @"(hausse|augmentation)\s+(de\s+|des\s+)?(prix|tarifs?)|hausse\s+tarifaire|augmentation\s+tarifaire|" +
        @"remise\s+(moyenne|typique|habituelle)|combien\s+co[uû]te|tarifs?\s+(moyens?|publics?|du\s+march[eé])|" +
        @"co[uû]t\s+par\s+(utilisateur|licence|poste|si[eè]ge)|" +
        @"rango\s+de\s+precios?|horquilla|precios?\s+(medios?|t[ií]picos?|de\s+mercado|habituales)|" +
        @"(subida|aumento|incremento)\s+de\s+precios|encarecimiento|descuento\s+(medio|t[ií]pico|habitual)|" +
        @"cu[aá]nto\s+cuesta|precio\s+de\s+lista|(coste|costo)\s+por\s+(usuario|licencia|puesto)|tarifa\s+(media|est[aá]ndar)|" +
        @"preisspanne\w*|bandbreite|preisniveau|durchschnittspreis\w*|durchschnittliche\w*\s+preis\w*|" +
        @"markt[uü]bliche\w*\s+preis\w*|preiserh[oö]hung\w*|preissteigerung\w*|preisanstieg|listenpreis\w*|" +
        @"(durchschnittlicher|[uü]blicher)\s+rabatt|was\s+kostet|wie\s+viel\s+kostet|" +
        @"kosten\s+pro\s+(nutzer|lizenz|benutzer|platz)|preis\s+pro\s+(nutzer|lizenz|benutzer))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NegotiationTacticsPattern = new(
        @"\b(negotiat\w*|negozia\w*|tactic\w*|tattic\w*|lever\w*|lev[ae]|playbook|argument\w*|argoment\w*|" +
        @"contrattare|contratta\w*|strateg\w*|how\s+to\s+(push|ask|get)|come\s+(spingere|chiedere|ottenere)|" +
        @"walk[\s-]?away|bluff\w*|concession\w*|" +
        // fr/es/de (LANG-03); "negocio" (ES: business) is not a match.
        @"n[eé]gocier|n[eé]gociation\w*|tactique\w*|levier\w*|strat[eé]gie\w*|comment\s+(obtenir|demander|pousser)|" +
        @"negociar|negociaci[oó]n|negociaciones|t[aá]cticas?|palanca\w*|estrategia\w*|c[oó]mo\s+(conseguir|pedir|obtener|presionar)|" +
        @"concesiones|farol|" +
        @"verhandel\w*|verhandlung\w*|taktik\w*|hebel|strategie\w*|wie\s+(erreiche|bekomme|fordere)\s+ich|" +
        @"zugest[aä]ndnis\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Anything procurement-shaped: the persona serves these under MarketPractice when no narrower
    // purpose matched.
    private static readonly Regex TopicPattern = new(
        @"\b(market\s+practice\w*|pratich[ea]\s+di\s+mercato|best\s+practice\w*|" +
        @"renewal\w*|rinnov\w*|saas|licen[cs]\w*|cloud|subscription\w*|abbonament\w*|" +
        @"supplier\w*|fornitor\w*|vendor\w*|contract\w*|contratt\w*|procurement|acquist\w*|" +
        @"tender\w*|gara|gare|sla|pricing|prezz\w*|discount\w*|scont\w*|clause\w*|clausol\w*|" +
        @"notice\s+period\w*|preavviso|auto[\s-]?renew\w*|termination|recesso|" +
        @"insurance|assicura\w*|maintenance|manutenzion\w*|support|assistenza|" +
        @"indexation|indicizzazion\w*|cpi|inflation|inflazion\w*|payment\s+terms|termini\s+di\s+pagamento|" +
        // fr/es/de (LANG-03)
        @"bonnes\s+pratiques|pratiques?\s+(du|de)\s+march[eé]|renouvellement\w*|abonnement\w*|prestataire\w*|contrat\w*|achats?|" +
        @"appels?\s+d['’]offres?|tarification|prix|remise\w*|pr[eé]avis|tacite\s+reconduction|reconduction|r[eé]siliation|" +
        @"assurance\w*|conditions\s+de\s+paiement|d[eé]lais?\s+de\s+paiement|" +
        @"buenas\s+pr[aá]cticas|pr[aá]cticas?\s+de\s+mercado|renovaci[oó]n|renovaciones|nube|suscripci[oó]n|suscripciones|proveedor\w*|" +
        @"contrato\w*|contrataci[oó]n|licitaci[oó]n|licitaciones|precios?|tarifa\w*|descuento\w*|cl[aá]usula\w*|preaviso|" +
        @"rescisi[oó]n|p[oó]liza\w*|mantenimiento|soporte|indexaci[oó]n|ipc|inflaci[oó]n|condiciones\s+de\s+pago|plazo\s+de\s+pago|" +
        @"marktpraxis|marktpraktik\w*|verl[aä]ngerung\w*|lizenz\w*|abonnement\w*|lieferant\w*|anbieter|dienstleister\w*|" +
        @"vertr[aä]g\w*|einkauf|beschaffung|ausschreibung\w*|preis\w*|rabatt\w*|klausel\w*|k(ue|[uü])ndigungsfrist\w*|" +
        @"k(ue|[uü])ndigung\w*|versicherung\w*|wartung|indexierung|preisanpassung\w*|zahlungsbedingungen|zahlungsziel\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A capitalised run that is not the first word — the same proper-noun heuristic the gate and
    // the deterministic planner use — so "news about Salesforce" counts as supplier news even when
    // no other procurement noun is in the sentence.
    private static readonly Regex CapitalizedNamePattern = new(
        @"(?<=\s)[A-Z][\w&.-]{2,}", RegexOptions.Compiled);

    /// <summary>The user explicitly asked to search the public web.</summary>
    public static bool IsExplicitRequest(string question) =>
        !string.IsNullOrWhiteSpace(question) && ExplicitIntentPattern.IsMatch(question);

    /// <summary>The explicit phrase itself, so the sanitiser can drop it from the query.</summary>
    public static string StripExplicitRequest(string question) =>
        string.IsNullOrWhiteSpace(question) ? string.Empty : ExplicitIntentPattern.Replace(question, " ");

    /// <summary>
    /// The research purpose this question can be served under, or <see langword="null"/> when it
    /// touches none of the procurement topics — then no web offer is ever made, whatever the user
    /// asked for.
    /// </summary>
    public static string? Classify(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return null;
        }

        var text = StripExplicitRequest(question);
        var onTopic = TopicPattern.IsMatch(text);

        if (SupplierNewsPattern.IsMatch(text) && (onTopic || CapitalizedNamePattern.IsMatch(text.Trim())))
        {
            return SupplierNews;
        }

        if (BenchmarkRangePattern.IsMatch(text))
        {
            return BenchmarkRange;
        }

        if (NegotiationTacticsPattern.IsMatch(text) && onTopic)
        {
            return NegotiationTactics;
        }

        return onTopic ? MarketPractice : null;
    }
}
