using System.Text.RegularExpressions;
using Raffa.Chat.Application.Planning;
using Raffa.Chat.Application.Reply;

namespace Raffa.Chat.Application.Gaps;

/// <summary>What the answer path knows about the reply it just built — all the trigger T2 reads.
/// The reply's text never enters the trigger.</summary>
/// <param name="Kind">The reply's kind.</param>
/// <param name="GuardIntervened">The answer role's guard pipeline intervened (retry or downgrade).</param>
/// <param name="FallbackUsed">The grounded or helpful fallback wrote the answer instead of the model.</param>
public sealed record ReplyOutcome(ReplyKind Kind, bool GuardIntervened, bool FallbackUsed);

/// <summary>
/// Why (or why not) the capability investigator runs on a turn (INV-01). <see cref="ShouldRun"/> is
/// <c>T1 || T2 || T3</c>; <see cref="Reason"/> is a short stable code list (never question text)
/// that goes into the audit row; <see cref="T3Language"/> is the lexicon language that matched T3.
/// </summary>
public sealed record TriggerVerdict(bool T1, bool T2, bool T3, string Reason, string? T3Language = null)
{
    public bool ShouldRun => T1 || T2 || T3;

    /// <summary>No trigger fired.</summary>
    public static TriggerVerdict None { get; } = new(false, false, false, InvestigatorTrigger.ReasonNone);
}

/// <summary>
/// ADR-031 without the per-question token bill (INV-01, decision D3): a pure, deterministic check
/// of whether a fresh in-domain turn is worth one <c>gaps-v1</c> call. No model call, no I/O, no
/// state — the answer path evaluates it twice, with what it knows at each point:
/// <list type="bullet">
/// <item><b>T1 — no intent recognised</b>: the planner fell back to its default
/// (<see cref="IntentPlanBasis.Fallback"/>). Known after the planner.</item>
/// <item><b>T2 — Raffa could not answer</b>: an abstain, or a reply the fallback wrote, or a guard
/// intervention that downgraded the answer. Known after the composer.</item>
/// <item><b>T3 — an operational request nothing in the catalog covers</b>: a verb of doing
/// ("genera", "export", "envoie", "programa", "erstelle"...) asked of Raffa, in any of the five
/// languages, judged by the versioned <see cref="InvestigatorTriggerLexicon"/> from the question
/// alone — so it can start before the answer, in parallel with it.</item>
/// </list>
/// <para>
/// T3 is deliberately a little generous and the investigator, which has the full capability map,
/// is the judge: a false positive costs one call, a false negative costs a missed proposal. What T3
/// does <b>not</b> take for a request: a verb whose subject is a third party ("il fornitore deve
/// inviare la disdetta"), a verb under the user's own obligation ("entro quando devo inviare la
/// PEC?"), and a deliverable noun in a question about the text of a clause.
/// </para>
/// </summary>
public sealed class InvestigatorTrigger
{
    public const string ReasonNone = "none";

    /// <summary>Not a trigger: the kill switch (<c>Chat:GapInvestigation:Enabled</c>) is off.</summary>
    public const string ReasonKillSwitch = "kill-switch";
    public const string ReasonNoIntent = "t1-no-intent";
    public const string ReasonAbstain = "t2-abstain";
    public const string ReasonFallbackAnswer = "t2-fallback-answer";
    public const string ReasonGuardDowngrade = "t2-guard-downgrade";
    public const string ReasonOperationalRequest = "t3-operational-request";

    // A sentence boundary; commas do not break a clause (German puts one before every infinitive).
    private static readonly Regex ClauseBreak = new(@"[.;:?!\n]", RegexOptions.Compiled);

    private readonly InvestigatorTriggerLexicon _lexicon;

    public InvestigatorTrigger()
        : this(InvestigatorTriggerLexicon.Default)
    {
    }

    public InvestigatorTrigger(InvestigatorTriggerLexicon lexicon)
    {
        ArgumentNullException.ThrowIfNull(lexicon);
        _lexicon = lexicon;
    }

    /// <summary>The shared instance over the embedded lexicon.</summary>
    public static InvestigatorTrigger Default { get; } = new();

    /// <summary>The lexicon version the verdicts come from (audited).</summary>
    public string LexiconVersion => _lexicon.Version;

    /// <summary>
    /// Evaluates the triggers known so far. <paramref name="plan"/> is <see langword="null"/> before
    /// the planner ran (or when the turn never had one — a web-mode turn), and
    /// <paramref name="replyOutcome"/> is <see langword="null"/> before the answer exists; a trigger
    /// whose input is still unknown is simply not fired.
    /// </summary>
    public TriggerVerdict Evaluate(IntentPlanResult? plan, ReplyOutcome? replyOutcome, string question)
    {
        ArgumentNullException.ThrowIfNull(question);

        var reasons = new List<string>(3);

        var t1 = plan is { Basis: IntentPlanBasis.Fallback };
        if (t1)
        {
            reasons.Add(ReasonNoIntent);
        }

        var t2 = false;
        if (replyOutcome is not null)
        {
            if (replyOutcome.Kind == ReplyKind.Abstain)
            {
                t2 = true;
                reasons.Add(replyOutcome.GuardIntervened ? ReasonGuardDowngrade : ReasonAbstain);
            }
            else if (replyOutcome.FallbackUsed)
            {
                t2 = true;
                reasons.Add(ReasonFallbackAnswer);
            }
        }

        var t3Language = MatchOperationalRequest(question);
        if (t3Language is not null)
        {
            reasons.Add(ReasonOperationalRequest);
        }

        return reasons.Count == 0
            ? TriggerVerdict.None
            : new TriggerVerdict(t1, t2, t3Language is not null, string.Join('+', reasons), t3Language);
    }

    /// <summary>T3 alone, from the question: the language of the first lexicon that reads it as an
    /// operational request, else <see langword="null"/>.</summary>
    public string? MatchOperationalRequest(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return null;
        }

        // Words overlap across languages ("generate" is Italian and English): the language reported
        // is the one that explains most of the sentence, the first listed on a tie.
        string? best = null;
        var bestScore = 0;
        foreach (var language in _lexicon.Languages)
        {
            var score = OperationalRequestScore(language, question);
            if (score > bestScore)
            {
                best = language.Language;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>0 when <paramref name="question"/> is not an operational request in this language,
    /// otherwise how many lexicon words (verbs, formulas, deliverables) it matched.</summary>
    private static int OperationalRequestScore(InvestigatorTriggerLexicon.LanguageLexicon lexicon, string question)
    {
        var verbMatches = lexicon.Verbs.Matches(question);
        var hasFormula = lexicon.Formulas.IsMatch(question);
        var deliverableCount = lexicon.Deliverables.Matches(question).Count;
        var hasDeliverable = deliverableCount > 0;

        var hasVerb = false;
        var imperative = false;
        foreach (Match verb in verbMatches)
        {
            var prefix = question[..verb.Index];
            if (IsAttributedElsewhere(lexicon, prefix))
            {
                continue;
            }

            hasVerb = true;
            if (lexicon.PolitenessOnly.IsMatch(prefix))
            {
                imperative = true;
            }
        }

        var positive =
            (hasVerb && (hasFormula || hasDeliverable))
            || imperative
            // A formula and a deliverable with no verb of doing ("can you give me a report on X?")
            // — unless the question is really about the text of a clause or a contact.
            || (hasFormula && hasDeliverable && !lexicon.ContractCues.IsMatch(question));

        if (!positive)
        {
            return 0;
        }

        return Math.Max(1, (hasVerb ? 1 : 0) + (hasFormula ? 1 : 0) + deliverableCount);
    }

    /// <summary>The verb belongs to somebody else: a third party stands in the few words before it
    /// ("il fornitore può inviare"), or an obligation of the user's own does ("devo inviare") —
    /// unless the user addresses Raffa in between ("per il contratto Amazon, puoi esportare...").</summary>
    private static bool IsAttributedElsewhere(
        InvestigatorTriggerLexicon.LanguageLexicon lexicon, string prefix)
    {
        var clauseStart = 0;
        foreach (Match breakMatch in ClauseBreak.Matches(prefix))
        {
            clauseStart = breakMatch.Index + 1;
        }

        var clause = prefix[clauseStart..];
        var words = clause.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return false;
        }

        var windowWords = words.Length > lexicon.SubjectWindowWords ? words[^lexicon.SubjectWindowWords..] : words;
        var window = string.Join(' ', windowWords);

        var cues = lexicon.ThirdParties.Matches(window);
        var obligations = lexicon.Obligations.Matches(window);
        if (cues.Count == 0 && obligations.Count == 0)
        {
            return false;
        }

        var cueEnd = Math.Max(
            cues.Count > 0 ? cues[^1].Index + cues[^1].Length : -1,
            obligations.Count > 0 ? obligations[^1].Index + obligations[^1].Length : -1);

        // A request formula standing after the cue and still inside the window: the user is
        // addressing Raffa, whoever the sentence is about.
        foreach (Match formula in lexicon.Formulas.Matches(window))
        {
            if (formula.Index >= cueEnd)
            {
                return false;
            }
        }

        return true;
    }
}
