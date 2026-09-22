using System.Text;
using System.Text.RegularExpressions;
using Raffa.AiGateway.Contracts;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Guards;
using Raffa.Chat.Application.Pack;

namespace Raffa.Chat.Application.Answering;

/// <summary>
/// The answer Raffa composes itself when the `answer` role tried to answer (its own
/// <c>canDetermine</c> was <see langword="true"/>) but both attempts tripped a guard — so the pack
/// was judged sufficient, only the model's rendering of it could not be trusted. Instead of the old
/// "I don't have data I trust enough" downgrade, the user gets an acknowledgement of what they
/// asked and the pack's own facts as a readable, cited answer: the calculators' verdict, the
/// validated-contract candidates, levers and council plays, and the market-feed benchmarks
/// (R-ASK-06's "downgrade that shows the pack's own facts", narrowed by the product owner from an
/// abstain to this answer).
///
/// <para>
/// Grounded by construction: apart from the fixed template (no digit, currency code or percent
/// sign in it), every sentence is a pack item's own title or snippet, quoted — only whitespace,
/// citation keys, ids, bold markers and bracketed digits are tidied — and each quoted line must pass
/// <see cref="NumericGuard"/> on its own or it is left out, so one fragment that stopped being
/// verbatim never sinks the rest. Every <c>[n]</c> marker is numbered against the
/// <see cref="AiAnswerResult.CitationKeys"/> this method returns, and a <c>[n]</c> or <c>—</c> sits
/// between every two quoted fragments so no amount can form across a join. The acknowledgement
/// never echoes the question — a "20%" or "40k" in it would be a number the pack does not hold.
/// <c>AnswerComposer</c> still re-runs the full guard pipeline over the result before using it.
/// Pure and synchronous — no I/O, no LLM call.
/// </para>
/// </summary>
public static class GroundedFallbackAnswer
{
    private const int MaxActItems = 5;
    private const int MaxMarketItems = 2;
    private const int MaxSnippetLength = 280;

    private const string DisambiguationKey = "calc:multi-contract-resolution";
    private const string CouncilVerdictKey = "calc:council:verdict";
    private const string CouncilPlayPrefix = "calc:council:play[";
    private const string CandidatePrefix = "calc:candidate[";
    private const string CandidateNextPrefix = "calc:candidate-next[";
    private const string LeverPrefix = "calc:lever[";
    private const string NegotiationPointPrefix = "calc:negotiation-point[";

    // The one-item "verdict" a headline may lead with, most specific first.
    private static readonly string[] HeadlineKeys =
        ["calc:savings-target", "calc:portfolio-target", "calc:when-you-must-move", "calc:structured-query"];

    private static readonly string[] TargetKeys = ["calc:savings-target", "calc:portfolio-target"];

    // Two or more distinct Italian function/domain words — "Dove possiamo risparmiare?", "Qual e il
    // massimale nel contratto Microsoft?" — while an English question ("Where can I save the most
    // this quarter?") matches none. Deliberately excludes words English shares ("come", "per").
    private static readonly Regex ItalianCue = new(
        @"\b(dove|quanto|quanti|quale|quali|qual|posso|possiamo|puoi|faccio|risparmi\w*|contratt\w*|fornitor\w*|scadenz\w*|rinnov\w*|trimestre|mercato|perch[eé]|cosa|della|delle|degli|nel|nella|sono|abbiamo|il|lo|gli|che|una|mio|nostri?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // A citation key written into a snippet (the council's own "Grounded in: calc:lever[…]" tail,
    // or any other) — never shown to a reader (R-ASK-08).
    private static readonly Regex CitationKeyToken = new(
        @"(?<![\w\[])(?:fact|calc|tenant|market|raffa):[^\s,;()]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GuidToken = new(
        @"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GroundedInTail = new(
        @"\s*Grounded in:.*$", RegexOptions.Singleline | RegexOptions.Compiled);

    // The calculators' own explanation strings end their "not supported" case with an instruction
    // meant for the model ("…; say so and name the gap." / "…; say so and name what is missing.").
    private static readonly Regex ModelInstruction = new(
        @";\s*say so and name (?:the gap|what is missing)\.", RegexOptions.Compiled);

    private static readonly Regex WindowPreamble = new(
        @"^Window (\d+) days, no amount named\.\s*", RegexOptions.Compiled);

    // "[3]" inside a quoted snippet would read as an inline citation marker to GroundingGuard.
    private static readonly Regex BracketedDigits = new(@"\[(\d+)\]", RegexOptions.Compiled);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Composes the answer, or returns <see langword="null"/> when <paramref name="pack"/> holds no
    /// quotable, answer-bearing item (only Raffa feature/playbook items, or nothing whose line
    /// passes the numeric guard) — the caller then falls back to
    /// <c>Guards.RegenerateOnce.DowngradeToAbstain</c>.
    /// </summary>
    /// <param name="question">The user's question — read only for its language, never echoed.</param>
    /// <param name="pack">The already-budgeted pack the failed attempts were given.</param>
    /// <param name="metadata">The last attempt's reproducibility metadata (ADR-011), kept as-is.</param>
    public static AiAnswerResult? Compose(string question, IReadOnlyList<PackItem> pack, AiCallMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(metadata);

        var italian = IsItalian(question);
        var candidates = pack.Where(item => item.Corpus != PackCorpus.Raffa && Clean(item.Snippet, italian).Length > 0).ToList();

        var headline = BuildHeadline(candidates, pack, italian);
        var act = BuildActItems(candidates, headline, pack, italian);
        var market = candidates
            .Where(item => item.Corpus == PackCorpus.Market && IsQuotable(Bullet(item, italian), pack))
            .Take(MaxMarketItems)
            .ToList();

        if (headline.Count == 0 && act.Count == 0 && market.Count == 0)
        {
            return null;
        }

        var savingsShaped = IsSavingsShaped(pack);
        var citationKeys = new List<string>();
        var markdown = new StringBuilder();

        markdown.Append(Acknowledgement(savingsShaped, withMarket: market.Count > 0, italian));

        if (headline.Count > 0)
        {
            markdown.Append("\n\n");
            markdown.Append(string.Join(" ", headline.Select(item => $"{Clean(item.Snippet, italian)} {Cite(item, citationKeys)}")));
        }

        if (act.Count > 0)
        {
            markdown.Append("\n\n");
            markdown.Append(savingsShaped
                ? italian ? "**Dove agire**" : "**Where to act**"
                : italian ? "**Dai tuoi contratti validati**" : "**From your validated contracts**");

            foreach (var item in act)
            {
                markdown.Append($"\n- {Bullet(item, italian)} {Cite(item, citationKeys)}");
            }
        }

        if (market.Count > 0)
        {
            markdown.Append("\n\n");
            markdown.Append(italian ? "**Confronto con il mercato**" : "**Market check**");

            foreach (var item in market)
            {
                markdown.Append($"\n- {Bullet(item, italian)} {Cite(item, citationKeys)}");
            }
        }

        var answerMarkdown = markdown.ToString();
        var featureKeys = ActionKeyNormalizer.Normalize(
            pack.Where(item => item.Corpus == PackCorpus.Raffa).Select(item => item.CitationKey).ToList());

        IReadOnlyList<string> followUps = savingsShaped
            ? WithoutTheQuestion(
                question,
                italian
                    ? ["Quale contratto è prioritario?", "Quali leve fanno risparmiare di più?"]
                    : ["Which of these should we start first?", "Which levers save the most?"])
            : SuggestedQuestions(question, featureKeys.FirstOrDefault());

        return new AiAnswerResult(
            CanDetermine: true,
            Answer: answerMarkdown,
            Citations: [],
            Metadata: metadata,
            AnswerMarkdown: answerMarkdown,
            CitationKeys: citationKeys,
            ActionKeys: featureKeys,
            AbstainReason: null,
            FollowUps: followUps);
    }

    /// <summary>
    /// Whether <paramref name="pack"/> is a savings or negotiation pack — one the calculators and the
    /// council built to say where to save and what to ask (a target, candidates, levers, plays), as
    /// opposed to a clause or fact lookup. <c>AnswerComposer</c> only overrides a retry's honest
    /// "cannot determine" for such a pack; on a clause or fact pack the model is believed.
    /// </summary>
    public static bool IsSavingsShaped(IReadOnlyList<PackItem> pack)
    {
        ArgumentNullException.ThrowIfNull(pack);

        return pack.Any(item =>
            item.CitationKey is "calc:savings-target" or "calc:portfolio-target" or CouncilVerdictKey ||
            item.CitationKey.StartsWith(CouncilPlayPrefix, StringComparison.Ordinal) ||
            item.CitationKey.StartsWith(CandidatePrefix, StringComparison.Ordinal) ||
            item.CitationKey.StartsWith(CandidateNextPrefix, StringComparison.Ordinal) ||
            item.CitationKey.StartsWith(LeverPrefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// Two next-step questions for <paramref name="capabilityKey"/>'s area, in the language of
    /// <paramref name="question"/> — the follow-ups an honest abstain offers so the user always has
    /// somewhere to go next, and the fallback answer's own when it is not a savings answer. Each one
    /// is phrased so the planner routes it to a real answer (savings, priority, renewal strategy,
    /// market position, document status), never to the catch-all; a chip identical to the question
    /// just asked is left out. An unknown or <see langword="null"/> key gets the general pair.
    /// </summary>
    public static IReadOnlyList<string> SuggestedQuestions(string question, string? capabilityKey)
    {
        ArgumentNullException.ThrowIfNull(question);

        var italian = IsItalian(question);
        string[] chips = capabilityKey switch
        {
            CapabilityCatalog.SavingsKey => italian
                ? ["Dove possiamo risparmiare?", "Qual è il risparmio più grande adesso?"]
                : ["Where can we save?", "What is the largest saving right now?"],
            CapabilityCatalog.RenewalsKey => italian
                ? ["Quale rinnovo è più prioritario?", "Come affrontare il prossimo rinnovo?"]
                : ["Which contracts renew in the next 120 days?", "Which contract should we start first?"],
            CapabilityCatalog.QuoteCheckKey => italian
                ? ["Quali contratti sono mal posizionati sul mercato?", "Dove possiamo risparmiare?"]
                : ["Which contracts are poorly positioned on the market?", "Where can we save?"],
            CapabilityCatalog.DocumentsKey or CapabilityCatalog.DocumentsAttentionKey => italian
                ? ["Quali campi mancano ancora?", "Dove possiamo risparmiare?"]
                : ["Which documents are not askable yet?", "How do I upload a contract?"],
            _ => italian
                ? ["Quali contratti sono più critici?", "Dove possiamo risparmiare?"]
                : ["Which contracts are most critical?", "Where can we save?"],
        };

        return WithoutTheQuestion(question, chips);
    }

    /// <summary>Whether <paramref name="question"/> reads as Italian (two or more distinct Italian
    /// cue words) — the persona prompt's rule 5, "answer in the language of the question", for the
    /// copy this type writes itself. Anything else is answered in English.</summary>
    public static bool IsItalian(string question) =>
        ItalianCue.Matches(question)
            .Select(match => match.Value.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Count() >= 2;

    /// <summary>The lead paragraph: which contract was picked among several (NW-80: never dropped,
    /// so always first), the calculators' one verdict item, and the council's verdict on the goal —
    /// the latter only when the user named a goal (a target amount or percentage) or there is no
    /// calculator verdict to lead with, so "the goal is reachable" is never said of a goal nobody set.</summary>
    private static List<PackItem> BuildHeadline(IReadOnlyList<PackItem> candidates, IReadOnlyList<PackItem> pack, bool italian)
    {
        var headline = new List<PackItem>();

        if (candidates.FirstOrDefault(item => item.CitationKey == DisambiguationKey) is { } disambiguation)
        {
            headline.Add(disambiguation);
        }

        var target = HeadlineKeys
            .Select(key => candidates.FirstOrDefault(item => item.CitationKey == key))
            .FirstOrDefault(item => item is not null);
        if (target is not null)
        {
            headline.Add(target);
        }

        var goalNamed = target is null ||
            (TargetKeys.Contains(target.CitationKey, StringComparer.Ordinal) &&
             target.Values.Any(v => v.Key is "targetAmount" or "targetPercent"));
        if (goalNamed && candidates.FirstOrDefault(item => item.CitationKey == CouncilVerdictKey) is { } verdict)
        {
            headline.Add(verdict);
        }

        return headline.Where(item => IsQuotable(Clean(item.Snippet, italian), pack)).ToList();
    }

    /// <summary>
    /// The "where to act" bullets, each item used once, at most <see cref="MaxActItems"/>: council
    /// plays, then in-window candidates, then next-period candidates only when no in-window one is
    /// shown, then levers only when neither plays nor candidates already say the same, then the other
    /// calculator items (negotiation points, targets, criticality), then this tenant's own facts and
    /// clauses.
    /// </summary>
    private static List<PackItem> BuildActItems(
        IReadOnlyList<PackItem> candidates, IReadOnlyList<PackItem> headline, IReadOnlyList<PackItem> pack, bool italian)
    {
        bool Quotable(PackItem item) => !headline.Contains(item) && IsQuotable(Bullet(item, italian), pack);
        List<PackItem> With(string prefix) =>
            candidates.Where(item => item.CitationKey.StartsWith(prefix, StringComparison.Ordinal) && Quotable(item)).ToList();

        var plays = With(CouncilPlayPrefix);
        var inWindow = With(CandidatePrefix);
        var nextPeriod = inWindow.Count == 0 ? With(CandidateNextPrefix) : [];
        var levers = plays.Count == 0 && inWindow.Count == 0 && nextPeriod.Count == 0 ? With(LeverPrefix) : [];
        var negotiationPoints = With(NegotiationPointPrefix);

        var otherCalc = candidates
            .Where(item => item.Corpus == PackCorpus.Calc &&
                item.CitationKey != CouncilVerdictKey &&
                !HeadlineKeys.Contains(item.CitationKey, StringComparer.Ordinal) &&
                !item.CitationKey.StartsWith(CouncilPlayPrefix, StringComparison.Ordinal) &&
                !item.CitationKey.StartsWith(CandidatePrefix, StringComparison.Ordinal) &&
                !item.CitationKey.StartsWith(CandidateNextPrefix, StringComparison.Ordinal) &&
                !item.CitationKey.StartsWith(LeverPrefix, StringComparison.Ordinal) &&
                !item.CitationKey.StartsWith(NegotiationPointPrefix, StringComparison.Ordinal) &&
                Quotable(item));

        var tenant = candidates.Where(item => item.Corpus == PackCorpus.Tenant && Quotable(item));

        return plays
            .Concat(inWindow)
            .Concat(nextPeriod)
            .Concat(levers)
            .Concat(negotiationPoints)
            .Concat(otherCalc)
            .Concat(tenant)
            .Distinct()
            .Take(MaxActItems)
            .ToList();
    }

    private static string Acknowledgement(bool savingsShaped, bool withMarket, bool italian) => (savingsShaped, italian) switch
    {
        (true, true) => withMarket
            ? "Ecco dove puoi risparmiare e cosa negoziare, in base ai tuoi contratti validati e ai dati di mercato."
            : "Ecco dove puoi risparmiare e cosa negoziare, in base ai tuoi contratti validati.",
        (true, false) => withMarket
            ? "Here's where you can save and what to negotiate, based on your validated contracts and the market data."
            : "Here's where you can save and what to negotiate, based on your validated contracts.",
        (false, true) => withMarket
            ? "Ecco cosa mostrano su questo punto i tuoi contratti validati e i dati di mercato."
            : "Ecco cosa mostrano su questo punto i tuoi contratti validati.",
        (false, false) => withMarket
            ? "Here's what your validated contracts and the market data show on this."
            : "Here's what your validated contracts show on this.",
    };

    private static string Bullet(PackItem item, bool italian) => $"**{Tidy(item.Title)}** — {Clean(item.Snippet, italian)}";

    private static bool IsQuotable(string line, IReadOnlyList<PackItem> pack) =>
        line.Length > 0 && NumericGuard.Validate(line, pack).Passed;

    private static string Cite(PackItem item, List<string> citationKeys)
    {
        var index = citationKeys.IndexOf(item.CitationKey);
        if (index < 0)
        {
            citationKeys.Add(item.CitationKey);
            index = citationKeys.Count - 1;
        }

        return $"[{index + 1}]";
    }

    private static IReadOnlyList<string> WithoutTheQuestion(string question, IReadOnlyList<string> chips)
    {
        static string Norm(string text) => text.Trim().TrimEnd('?').Trim().ToLowerInvariant();

        var asked = Norm(question);
        return chips.Where(chip => Norm(chip) != asked).ToList();
    }

    /// <summary>Text on one line, with no citation key, id, bold marker or bracketed digit a reader
    /// or a guard could trip on.</summary>
    private static string Tidy(string text)
    {
        var tidy = CitationKeyToken.Replace(text, string.Empty);
        tidy = GuidToken.Replace(tidy, string.Empty);
        tidy = tidy.Replace("**", string.Empty, StringComparison.Ordinal);
        tidy = BracketedDigits.Replace(tidy, "($1)");
        return Whitespace.Replace(tidy, " ").Trim();
    }

    /// <summary>
    /// A snippet ready to quote: the council's "Grounded in: …" key list and the calculators'
    /// model-facing "say so and name …" instruction removed, the portfolio target's "Window N days,
    /// no amount named." preamble phrased for a reader, then <see cref="Tidy"/>, then cut at a
    /// sentence (or, failing that, a word) boundary past <see cref="MaxSnippetLength"/> — never
    /// inside a number, so a quoted amount stays verbatim.
    /// </summary>
    private static string Clean(string snippet, bool italian)
    {
        var text = GroundedInTail.Replace(snippet, string.Empty);
        text = ModelInstruction.Replace(text, ".");
        text = WindowPreamble.Replace(text, italian ? "Nei prossimi $1 giorni: " : "Over the next $1 days: ");
        text = Tidy(text);

        if (text.Length <= MaxSnippetLength)
        {
            return text;
        }

        var sentenceEnd = text.LastIndexOf(". ", MaxSnippetLength, StringComparison.Ordinal);
        if (sentenceEnd >= MaxSnippetLength / 3)
        {
            return text[..(sentenceEnd + 1)];
        }

        var wordEnd = text.LastIndexOf(' ', MaxSnippetLength);
        return wordEnd > 0 ? text[..wordEnd].TrimEnd(',', ';', ':') + "…" : text;
    }
}
