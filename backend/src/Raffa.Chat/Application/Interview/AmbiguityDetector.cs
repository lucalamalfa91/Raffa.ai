using System.Text.RegularExpressions;
using Raffa.Chat.Application.Planning;
using Raffa.Chat.Domain;

namespace Raffa.Chat.Application.Interview;

public enum AmbiguityVerdict
{
    /// <summary>Answer as usual.</summary>
    Clear,

    /// <summary>One weak signal only — never interviews on its own (the optional model stage may
    /// tip it).</summary>
    Unsure,

    /// <summary>Interview before retrieving anything.</summary>
    Ambiguous,
}

/// <summary>What the host knows about the turn that the question text alone cannot say.</summary>
public sealed record InterviewContext(
    bool HasScope,
    int NamedSupplierContractCount,
    bool PortfolioIsEmpty,
    bool PreviousRaffaTurnWasInterview,
    bool IsNoticeQuestion);

public sealed record AmbiguitySignals(
    bool PlannerFellThrough,
    bool ShortAndUnscoped,
    bool VagueOrDeictic,
    IReadOnlyList<AskIntent> MultiIntent,
    int SupplierContractCount,
    bool UnscopedNotice,
    AmbiguityVerdict Verdict)
{
    /// <summary>The names of the signals that fired — what the audit row records (never the
    /// question text).</summary>
    public IReadOnlyList<string> Names
    {
        get
        {
            var names = new List<string>();
            if (PlannerFellThrough) names.Add("planner-fell-through");
            if (ShortAndUnscoped) names.Add("short-and-unscoped");
            if (VagueOrDeictic) names.Add("vague-or-deictic");
            if (MultiIntent.Count >= 2) names.Add("multi-intent");
            if (SupplierContractCount >= 2) names.Add("supplier-has-several-contracts");
            if (UnscopedNotice) names.Add("unscoped-notice");
            return names;
        }
    }
}

/// <summary>
/// Stage 1 of the interview (ADR-030): deterministic, pure, IT/EN. Decides whether a question is
/// clear enough to answer, or ambiguous enough that retrieving anything would be a guess.
///
/// <para>The verdict is deliberately conservative: a question that names a supplier, or runs in a
/// scoped conversation, or carries a domain noun the clause index can serve ("liabilities",
/// "notice", "prezzo"), is never interviewed — today's paths already answer those. Only a
/// question the planner has no reading of (the legacy router's own default, no supplier, no
/// scope, no domain noun) or that is plainly vague ("over all my contract", "everything", "this")
/// is <see cref="AmbiguityVerdict.Ambiguous"/>. Several plausible intents or a short unscoped
/// question are only <see cref="AmbiguityVerdict.Unsure"/>, so the planner's tuned priority order
/// keeps deciding those.</para>
/// </summary>
public static class AmbiguityDetector
{
    private static readonly Regex VaguePattern = new(
        @"\b(all\s+my\s+contract\w*|over\s?all|everything|in\s+general|overview|summary|summari[sz]e|" +
        @"tutt[oi](\s+i)?(\s+miei)?\s+contratt\w*|in\s+generale|panoramica|riassunt\w*|riassumi|" +
        @"sintesi|complessiv\w*)\b|^(this|that|it|these|those|questo|quello|questi|quelli)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A noun the clause index or a structured pack can serve even without a lexicon match — RAG on
    // it is a reasonable path today, so the interview stays out of the way.
    private static readonly Regex DomainNounPattern = new(
        @"\b(liabilit\w*|indemn\w*|warrant\w*|obligation\w*|clause\w*|terms?|price\w*|fees?|discount\w*|" +
        @"uplift\w*|sla|penalt\w*|payment\w*|invoice\w*|licen[cs]\w*|users?|seats?|volume\w*|renewal\w*|" +
        @"notice|termination|data|gdpr|security|insurance|coverage|cap|" +
        @"responsabilit\w*|obblig\w*|clausol\w*|prezz\w*|scont\w*|penal\w*|pagament\w*|fattur\w*|" +
        @"licenz\w*|utent\w*|assicura\w*|copertur\w*|garanz\w*|preavviso|disdetta|recesso|canone|canoni)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static AmbiguitySignals Detect(
        string question, IntentPlanResult plan, InterviewContext context, InterviewOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        var trimmed = question.Trim();
        var noSupplier = plan.NamedSupplier is null;
        var unscoped = !context.HasScope;
        var wordCount = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var hasDomainNoun = DomainNounPattern.IsMatch(trimmed);

        var plannerFellThrough = plan.Basis == IntentPlanBasis.Fallback;
        var shortAndUnscoped = wordCount <= options.ShortQuestionMaxWords && noSupplier && unscoped;
        var vague = VaguePattern.IsMatch(trimmed) && noSupplier && unscoped && !hasDomainNoun;

        IReadOnlyList<AskIntent> multiIntent = plan.Intent == AskIntent.PortfolioMarketPosition || !noSupplier
            ? []
            : DistinctFamilies(plan.Candidates ?? []);

        var supplierSeveral = options.AskWhichContract && unscoped ? context.NamedSupplierContractCount : 0;
        var unscopedNotice = options.AskOnUnscopedNotice && context.IsNoticeQuestion && noSupplier && unscoped;

        AmbiguityVerdict verdict;
        if (context.PreviousRaffaTurnWasInterview || context.PortfolioIsEmpty)
        {
            verdict = AmbiguityVerdict.Clear;
        }
        else if (supplierSeveral >= 2 || unscopedNotice)
        {
            verdict = AmbiguityVerdict.Ambiguous;
        }
        else if (plannerFellThrough && noSupplier && unscoped && !hasDomainNoun)
        {
            verdict = AmbiguityVerdict.Ambiguous;
        }
        else if (vague)
        {
            verdict = AmbiguityVerdict.Ambiguous;
        }
        else if (shortAndUnscoped || multiIntent.Count >= 2)
        {
            verdict = AmbiguityVerdict.Unsure;
        }
        else
        {
            verdict = AmbiguityVerdict.Clear;
        }

        return new AmbiguitySignals(
            plannerFellThrough, shortAndUnscoped, vague, multiIntent, supplierSeveral, unscopedNotice, verdict);
    }

    /// <summary>Candidates collapsed to one intent per "family", so savings-vs-savings never counts
    /// as two readings but savings-vs-market does.</summary>
    private static IReadOnlyList<AskIntent> DistinctFamilies(IReadOnlyList<AskIntent> candidates)
    {
        var result = new List<AskIntent>();
        var families = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var family = candidate switch
            {
                AskIntent.Savings or AskIntent.PortfolioStrategy or AskIntent.PortfolioSavingsTarget => "savings",
                AskIntent.MarketCompare or AskIntent.QuoteRoute or AskIntent.PortfolioMarketPosition => "market",
                AskIntent.RenewalStrategy => "renewal",
                AskIntent.DocumentStatus => "status",
                AskIntent.StructuredFact => "fact",
                AskIntent.Clause => "clause",
                _ => null,
            };

            if (family is not null && families.Add(family))
            {
                result.Add(candidate);
            }
        }

        return result;
    }
}

/// <summary>
/// Stage 3 of the interview (ADR-030): recognises a model abstain whose own reason says the
/// question was ambiguous, so the host can offer the interpretation interview instead of the
/// abstain block. Text-only, IT/EN; the model never writes an option.
/// </summary>
public static class AmbiguousAbstainDetector
{
    private static readonly Regex AmbiguousReasonPattern = new(
        @"ambigu\w*|unclear|not\s+clear|non\s+(è\s+|e\s+)?chiar\w*|what\s+you\s+mean|cosa\s+intend\w*|" +
        @"could\s+mean|several\s+(possible\s+)?(interpretations|meanings|readings)|pi[uù]\s+interpretazioni|" +
        @"too\s+vague|troppo\s+vag\w*|reliably\s+determine\s+what\s+you",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsAmbiguous(string? abstainReason) =>
        !string.IsNullOrWhiteSpace(abstainReason) && AmbiguousReasonPattern.IsMatch(abstainReason);
}
