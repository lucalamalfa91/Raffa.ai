using System.Globalization;
using System.Text.RegularExpressions;
using Raffa.Chat.Application.Planning;
using Raffa.Chat.Domain;

namespace Raffa.Chat.Application.Interview;

/// <summary>
/// Turns an <see cref="AmbiguityVerdict.Ambiguous"/> verdict into one interview question with
/// server-authored options (ADR-030). Pure: given the same question, plan, signals and inputs it
/// always produces the same turn. Labels and prompts follow the question's language (IT/EN);
/// every <see cref="InterviewResolution.RewrittenQuestion"/> is a fixed template the planner
/// re-plans deterministically — the round trip is what <c>InterviewPlannerTests</c> proves.
///
/// <para>Rewrites that the legacy deterministic layer must compute from text (annual spend, a
/// renewal window) keep an English trigger in parentheses even for an Italian user — that layer
/// is English-only today (golden-set gap <c>ItalianStructuredBlind</c>), and the model still
/// answers in the language the rest of the sentence is written in.</para>
/// </summary>
public sealed class InterviewPlanner(InterviewOptions options)
{
    public const string WhichContractKey = "which-contract";
    public const string NoticeContractKey = "notice-contract";
    public const string InterpretationKey = "interpretation";
    public const string SoonestOptionKey = "soonest";
    public const string WebResearchOptionKey = "web-research";

    private const int MaxNoticeCandidates = 4;

    public InterviewTurn? Plan(
        string question,
        IntentPlanResult plan,
        AmbiguitySignals signals,
        InterviewInputs inputs,
        WebResearchRequest? webOffer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(inputs);

        if (!options.Enabled || signals.Verdict != AmbiguityVerdict.Ambiguous)
        {
            return null;
        }

        var italian = LanguageHint.IsItalian(question);

        if (signals.SupplierContractCount >= 2 && inputs.SupplierContracts.Count >= 2)
        {
            return Turn(WhichContractQuestion(question, plan, inputs.SupplierContracts, italian), italian);
        }

        if (signals.UnscopedNotice && inputs.NoticeCandidates.Count > 0)
        {
            return Turn(NoticeContractQuestion(question, inputs.NoticeCandidates, italian), italian);
        }

        if (signals.PlannerFellThrough || signals.VagueOrDeictic || signals.MultiIntent.Count >= 2)
        {
            return PlanInterpretationMenu(question, plan, webOffer);
        }

        return null;
    }

    /// <summary>The generic "which of these do you mean?" interview — also what stage 3 offers
    /// when the model abstained for ambiguity. Options are fixed templates mapped to existing
    /// intents; the model never writes one.</summary>
    public InterviewTurn? PlanInterpretationMenu(string question, IntentPlanResult plan, WebResearchRequest? webOffer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(plan);

        if (!options.Enabled)
        {
            return null;
        }

        var italian = LanguageHint.IsItalian(question);
        var candidates = plan.Candidates ?? [];
        var choices = new List<InterviewOption>();

        var families = candidates.Select(Family).Where(f => f is not null).Distinct().ToList();
        if (families.Count >= 2)
        {
            foreach (var family in families)
            {
                choices.Add(FamilyOption(family!, italian));
            }
        }
        else
        {
            choices.Add(FamilyOption("savings", italian));
            choices.Add(TotalSpendOption(italian));
            choices.Add(RenewalsWindowOption(italian));
            if (candidates.Contains(AskIntent.DocumentStatus))
            {
                choices.Add(FamilyOption("status", italian));
            }
        }

        if (webOffer is not null)
        {
            choices.Add(WebResearchOption(question, italian));
        }

        var bounded = choices.Take(Math.Max(2, options.MaxOptionsPerQuestion)).ToList();
        var prompt = italian
            ? "Prima di rispondere, una verifica veloce: cosa intendi?"
            : "Before I answer, one quick check — which of these do you mean?";

        return Turn(new InterviewQuestion(InterpretationKey, prompt, InterviewPresentation.Choice, true, bounded), italian);
    }

    private InterviewTurn Turn(InterviewQuestion question, bool italian)
    {
        var lead = italian
            ? "Prima di rispondere, una verifica veloce."
            : "Before I answer, one quick check.";
        return new InterviewTurn(lead, [question]);
    }

    private InterviewQuestion WhichContractQuestion(
        string question, IntentPlanResult plan, IReadOnlyList<InterviewContractChoice> contracts, bool italian)
    {
        var supplier = contracts[0].SupplierName;
        var ordered = contracts
            .OrderBy(c => c.CancellationDeadline ?? c.RenewalDate ?? c.EndDate ?? DateOnly.MaxValue)
            .ThenBy(c => c.ContractId, StringComparer.Ordinal)
            .ToList();

        var perContract = Math.Max(1, options.MaxOptionsPerQuestion - 1);
        var choices = ordered.Take(perContract).Select((c, index) => new InterviewOption(
            $"contract-{index + 1}",
            ContractLabel(c, italian),
            null,
            new InterviewResolution(plan.Intent, c.ContractId, c.SupplierName, question))).ToList();

        choices.Add(new InterviewOption(
            SoonestOptionKey,
            italian ? "Quello che si rinnova prima" : "The one renewing soonest",
            null,
            new InterviewResolution(plan.Intent, ordered[0].ContractId, ordered[0].SupplierName, question)));

        var prompt = italian
            ? $"A quale contratto {supplier} ti riferisci?"
            : $"Which {supplier} contract do you mean?";

        return new InterviewQuestion(WhichContractKey, prompt, InterviewPresentation.Choice, true, choices);
    }

    private static InterviewQuestion NoticeContractQuestion(
        string question, IReadOnlyList<InterviewContractChoice> candidates, bool italian)
    {
        var ordered = candidates
            .Where(c => c.CancellationDeadline is not null)
            .OrderBy(c => c.CancellationDeadline)
            .ThenBy(c => c.ContractId, StringComparer.Ordinal)
            .Take(MaxNoticeCandidates)
            .ToList();

        var choices = ordered.Select((c, index) => new InterviewOption(
            $"contract-{index + 1}",
            NoticeLabel(c, italian),
            null,
            new InterviewResolution(AskIntent.StructuredFact, c.ContractId, c.SupplierName, question))).ToList();

        choices.Add(new InterviewOption(
            "all-renewals",
            italian ? "Tutti — mostrami le scadenze" : "All of them — show me the deadlines",
            null,
            new InterviewResolution(AskIntent.StructuredFact, null, null, RenewalsWindowRewrite(italian))));

        var prompt = italian
            ? "Di quale contratto ti serve la scadenza del preavviso?"
            : "Which contract's notice deadline do you need?";

        return new InterviewQuestion(NoticeContractKey, prompt, InterviewPresentation.Choice, true, choices);
    }

    private static string ContractLabel(InterviewContractChoice c, bool italian)
    {
        var when = c.RenewalDate is { } renewal
            ? (italian ? $"si rinnova il {Iso(renewal)}" : $"renews {Iso(renewal)}")
            : c.CancellationDeadline is { } deadline
                ? (italian ? $"preavviso entro {Iso(deadline)}" : $"notice by {Iso(deadline)}")
                : c.EndDate is { } end
                    ? (italian ? $"scade il {Iso(end)}" : $"ends {Iso(end)}")
                    : (italian ? "data di rinnovo sconosciuta" : "renewal date unknown");

        return $"{c.SupplierName} · {c.ContractType} ({when})";
    }

    private static string NoticeLabel(InterviewContractChoice c, bool italian) =>
        $"{c.SupplierName} · {c.ContractType} ({(italian ? "preavviso entro" : "notice by")} {Iso(c.CancellationDeadline!.Value)})";

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Family(AskIntent intent) => intent switch
    {
        AskIntent.Savings or AskIntent.PortfolioStrategy or AskIntent.PortfolioSavingsTarget => "savings",
        AskIntent.MarketCompare or AskIntent.QuoteRoute => "market",
        AskIntent.RenewalStrategy => "renewal",
        AskIntent.DocumentStatus => "status",
        _ => null,
    };

    private static InterviewOption FamilyOption(string family, bool italian) => family switch
    {
        "savings" => new InterviewOption(
            "portfolio-overview",
            italian ? "I contratti più critici e dove risparmiare" : "The most critical contracts and where we can save",
            null,
            new InterviewResolution(
                AskIntent.PortfolioStrategy, null, null,
                italian
                    ? "Quali contratti sono più critici e dove possiamo risparmiare?"
                    : "Which contracts are most critical and where can we save?")),
        "market" => new InterviewOption(
            "market-position",
            italian ? "Come sono i nostri prezzi rispetto al mercato" : "How our prices compare with the market",
            null,
            new InterviewResolution(
                AskIntent.QuoteRoute, null, null,
                italian
                    ? "Come sono i nostri prezzi rispetto al mercato? (compare with the market)"
                    : "How do our prices compare with the market?")),
        "renewal" => new InterviewOption(
            "renewal-approach",
            italian ? "Come affrontare i prossimi rinnovi" : "How to approach the upcoming renewals",
            null,
            new InterviewResolution(
                AskIntent.RenewalStrategy, null, null,
                italian ? "Come affrontare i prossimi rinnovi?" : "How should we approach the upcoming renewals?")),
        "status" => new InterviewOption(
            "document-status",
            italian ? "Quali documenti non sono ancora interrogabili" : "Which documents are not askable yet",
            null,
            new InterviewResolution(
                AskIntent.DocumentStatus, null, null,
                italian
                    ? "Quali documenti non sono ancora interrogabili? (status)"
                    : "Which documents are not askable yet?")),
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown interview family."),
    };

    private static InterviewOption TotalSpendOption(bool italian) => new(
        "total-spend",
        italian ? "La spesa annua totale sui contratti" : "Our total annual spend across contracts",
        null,
        new InterviewResolution(
            AskIntent.StructuredFact, null, null,
            italian
                ? "Quanto spendiamo in totale all'anno su tutti i contratti? (total annual spend)"
                : "What is our total annual spend across all contracts?"));

    private static InterviewOption RenewalsWindowOption(bool italian) => new(
        "renewals-window",
        italian ? "I contratti in rinnovo nei prossimi 120 giorni" : "The contracts renewing in the next 120 days",
        null,
        new InterviewResolution(AskIntent.StructuredFact, null, null, RenewalsWindowRewrite(italian)));

    private static string RenewalsWindowRewrite(bool italian) => italian
        ? "Quali contratti si rinnovano nei prossimi 120 giorni? (renew in the next 120 days)"
        : "Which contracts renew in the next 120 days?";

    // Picking this option never authorises anything: it forces AskIntent.WebResearch on the
    // original question, and that intent's own consent question (presentation "consent") is the
    // only place an authorisation can come from (ADR-030). The offer itself (query + purpose) is
    // recomputed by the composition root when the consent is built, from the same words.
    private static InterviewOption WebResearchOption(string question, bool italian) => new(
        WebResearchOptionKey,
        italian ? "Cerca sul web pubblico le pratiche di mercato (chiede conferma)" : "Search the public web for market practice (asks first)",
        italian ? "Nulla dei tuoi contratti esce da Raffa." : "Nothing from your contracts leaves Raffa.",
        new InterviewResolution(AskIntent.WebResearch, null, null, question.Trim()));
}

/// <summary>A tiny IT/EN hint: two or more Italian markers make a question Italian. Mirrors how the
/// planner's own lexicons already accept both languages side by side.</summary>
public static class LanguageHint
{
    private static readonly Regex ItalianMarkers = new(
        @"\b(quali|quale|quanto|quanti|quando|dove|perch[eé]|contratt\w*|fornitor\w*|posso|possiamo|abbiamo|" +
        @"scadenz\w*|scade|scadono|rinnov\w*|risparm\w*|nostr\w*|questo|quello|della|delle|degli|dei|sono|" +
        @"pi[uù]|preavviso|disdetta|prezz\w*|spendiamo|spesa|costi|mercato|tutti|ancora|prima)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsItalian(string text) =>
        !string.IsNullOrWhiteSpace(text) && ItalianMarkers.Matches(text).Count >= 2;
}
