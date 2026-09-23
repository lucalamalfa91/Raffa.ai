using System.Text.RegularExpressions;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Language;
using Raffa.Chat.Application.Reply;

namespace Raffa.Chat.Application.WebResearch;

/// <summary>
/// The two replies only a web-mode turn produces (ADR-031). <see cref="OffContext"/> points a
/// plainly personal question at a search engine and an AI search assistant — two server-authored
/// outbound links, no model call. <see cref="Combine"/> puts the contracts-only answer and the web
/// research side by side in one reply without ever merging their packs: each half was grounded and
/// guarded on its own (the <c>answer</c> role never saw a web source, the <c>research</c> role never
/// saw the tenant's data), and here they only share a message, each under its own heading, the web
/// citations renumbered after the tenant's so every <c>[n]</c> still resolves to its own card.
/// </summary>
public static class WebModeReplyBuilder
{
    public const string GoogleSearchUrl = "https://www.google.com/search?q=";
    public const string PerplexitySearchUrl = "https://www.perplexity.ai/search?q=";

    private const int MaxExternalQueryChars = 200;
    private const int MaxPromptVersionChars = 50;

    private static readonly Regex MarkerPattern = new(@"\[(\d+)\]", RegexOptions.Compiled);
    private static readonly Regex NotAWordCharacter = new(@"[^\p{L}\p{N}\s'’-]", RegexOptions.Compiled);
    private static readonly Regex TokenWithDigitsOrAddress = new(@"\S*[\d@/]\S*", RegexOptions.Compiled);

    /// <summary>The language a web-mode reply is written in: <see cref="QuestionLanguage"/>'s
    /// function-word score, so a short "dimmi la ricetta della carbonara" is answered in Italian.</summary>
    public static bool IsItalian(string question) => QuestionLanguage.IsItalian(QuestionLanguage.Detect(question));

    /// <summary>The off-context copy, in the question's language.</summary>
    public static string OffContextMarkdown(bool italian) =>
        italian
            ? "Questa domanda è fuori dal perimetro di Raffa: anche con la ricerca web attiva cerco solo temi di lavoro (fornitori, contratti, mercati, prezzi, normative). Per ricette, sport, meteo e altri temi personali ti conviene un motore di ricerca come **Google** oppure un assistente di ricerca AI come **Perplexity**, **ChatGPT** o **Copilot**."
            : "This one is outside Raffa's scope: even with web search on, I only research work topics (suppliers, contracts, markets, prices, regulation). For recipes, sport, weather and other personal topics, a search engine like **Google** or an AI search assistant like **Perplexity**, **ChatGPT** or **Copilot** will serve you better.";

    /// <summary>
    /// A redirect with two outbound actions, a Google search and a Perplexity search for the
    /// question's own words (amounts, dates, e-mail addresses and URLs removed by
    /// <see cref="WebQuerySanitizer"/>, punctuation dropped). Nothing is searched by Raffa.
    /// </summary>
    public static CopilotReply OffContext(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var italian = IsItalian(question);
        var query = ExternalSearchQuery(question);

        IReadOnlyList<CopilotAction> actions =
        [
            CopilotAction.External(italian ? "Cerca su Google →" : "Search on Google →", GoogleSearchUrl + query),
            CopilotAction.External(italian ? "Chiedi a Perplexity →" : "Ask Perplexity →", PerplexitySearchUrl + query),
        ];

        return new CopilotReply(ReplyKind.Redirect, OffContextMarkdown(italian), [], actions, ReplyProvenance.NoModelCall([]), []);
    }

    /// <summary>
    /// One reply from the two halves of a web-mode turn. <paramref name="internalAnswer"/> must be an
    /// <see cref="ReplyKind.Answer"/> built from Raffa's own store; <paramref name="web"/> is the
    /// guarded research outcome. When the web produced no answer the internal answer stands alone,
    /// with one sentence saying the web had nothing usable (a refusal adds nothing: the question was
    /// answered from the contracts).
    /// </summary>
    public static CopilotReply Combine(
        CopilotReply internalAnswer, WebResearchOutcome web, IReadOnlyList<CopilotAction> webActions, bool italian)
    {
        ArgumentNullException.ThrowIfNull(internalAnswer);
        ArgumentNullException.ThrowIfNull(web);
        ArgumentNullException.ThrowIfNull(webActions);

        if (web.Kind != WebResearchOutcomeKind.Answered)
        {
            var note = web.Kind switch
            {
                WebResearchOutcomeKind.Abstained => italian
                    ? "Sul web pubblico non ho trovato fonti abbastanza chiare da aggiungere: questa risposta viene solo dai dati di Raffa."
                    : "The public web had no sources clear enough to add: this answer comes from Raffa's data only.",
                WebResearchOutcomeKind.Failed => italian
                    ? "La ricerca sul web non è disponibile in questo momento: questa risposta viene solo dai dati di Raffa."
                    : "Web search is not available right now: this answer comes from Raffa's data only.",
                _ => null,
            };

            return note is null
                ? internalAnswer
                : internalAnswer with { AnswerMarkdown = internalAnswer.AnswerMarkdown.TrimEnd() + "\n\n" + note };
        }

        var offset = internalAnswer.Citations.Count == 0 ? 0 : internalAnswer.Citations.Max(c => c.N);
        var webCitations = web.Citations.Select(c => c with { N = c.N + offset }).ToList();

        var markdown =
            $"**{(italian ? "Dai tuoi contratti e dai dati di Raffa" : "From your contracts and Raffa's data")}**\n\n" +
            internalAnswer.AnswerMarkdown.Trim() + "\n\n" +
            $"**{(italian ? "Dal web pubblico · non verificato" : "From the public web · unverified")}**\n\n" +
            ShiftMarkers(web.Markdown, offset).Trim();

        var sources = internalAnswer.Provenance.Sources
            .Concat(web.Provenance.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var provenance = new ReplyProvenance(
            sources,
            internalAnswer.Provenance.ModelId ?? web.Provenance.ModelId,
            JoinPromptVersions(internalAnswer.Provenance.PromptVersion, web.Provenance.PromptVersion),
            internalAnswer.Provenance.InputHash ?? web.Provenance.InputHash,
            Unverified: true);

        return internalAnswer with
        {
            AnswerMarkdown = markdown,
            Citations = [.. internalAnswer.Citations, .. webCitations],
            Actions = internalAnswer.Actions.Concat(webActions).Distinct().ToList(),
            Provenance = provenance,
        };
    }

    /// <summary>Every <c>[n]</c> marker moved up by <paramref name="offset"/>.</summary>
    public static string ShiftMarkers(string markdown, int offset) =>
        offset == 0 || string.IsNullOrEmpty(markdown)
            ? markdown
            : MarkerPattern.Replace(markdown, match => $"[{int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) + offset}]");

    /// <summary>The question's words for an outbound search link, URL-encoded with <c>+</c> between
    /// words; empty when nothing usable is left (the link then opens the search page itself).</summary>
    public static string ExternalSearchQuery(string question)
    {
        // The sanitiser needs two words; a one-word question keeps its words minus any token that
        // carries a digit, an "@" or a "/" (an amount, an address, a link).
        var text = WebQuerySanitizer.Sanitize(question, MaxExternalQueryChars)
            ?? TokenWithDigitsOrAddress.Replace(WebResearchTopicLexicon.StripExplicitRequest(question), " ");
        text = NotAWordCharacter.Replace(text, " ");

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join('+', words.Select(Uri.EscapeDataString));
    }

    private static string? JoinPromptVersions(string? internalVersion, string? webVersion)
    {
        if (string.IsNullOrEmpty(internalVersion))
        {
            return webVersion;
        }

        if (string.IsNullOrEmpty(webVersion))
        {
            return internalVersion;
        }

        var joined = $"{internalVersion}+{webVersion}";
        return joined.Length <= MaxPromptVersionChars ? joined : internalVersion;
    }
}
