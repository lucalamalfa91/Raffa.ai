using System.Globalization;
using System.Text.RegularExpressions;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;

namespace Raffa.Chat.Application.Ask4;

/// <summary>
/// The rules every Chiedi answer path shares (prototype <c>answer()</c>, AskRaffa V4.dc.html): which
/// contract a question is about, the honest refusal for market questions, the "no evidence" answer,
/// and the conversation title (<c>askGlobal</c>).
/// </summary>
public static partial class AskRules
{
    public const string MarketSourceIt = "limite dichiarato";
    public const string MarketSourceEn = "declared limit";

    /// <summary>The validated contract whose supplier the text names (first in list order), or null.</summary>
    public static ContractFacts? NamedContract(string text, IReadOnlyList<ContractFacts> validated)
    {
        var lower = text.ToLowerInvariant();
        return validated.FirstOrDefault(c =>
            !string.IsNullOrWhiteSpace(c.Supplier) && lower.Contains(c.Supplier.ToLowerInvariant(), StringComparison.Ordinal));
    }

    public static bool IsMarketQuestion(string text) => MarketPattern().IsMatch(text.ToLowerInvariant());

    /// <summary>"Non ho un benchmark di mercato affidabile in questo rilascio e non lo invento. …" —
    /// source "limite dichiarato", CTA "Confronta un'offerta".</summary>
    public static AskAnswer MarketRefusal(ContractFacts contract, string lang) => new(
        false,
        lang == "en"
            ? "I have no reliable market benchmark in this release and I don't make one up. I compare only with your contract, your target and the offers you receive."
            : "Non ho un benchmark di mercato affidabile in questo rilascio e non lo invento. Confronto solo con il tuo contratto, il tuo obiettivo e le offerte che ricevi.",
        lang == "en" ? MarketSourceEn : MarketSourceIt,
        new AskAnswerCta("compareQuote", null),
        new EntityId(contract.ContractId),
        contract.Supplier);

    public static AskAnswer NoEvidence(ContractFacts contract, string lang) => new(
        false,
        lang == "en"
            ? "I find no reliable basis in the validated contracts to answer. Try deadlines, notice, prices, clauses, liability or obligations."
            : "Non trovo una base affidabile nei contratti validati per rispondere. Prova con scadenze, disdetta, prezzi, clausole, responsabilità o obblighi.",
        lang == "en" ? "no evidence above threshold" : "nessuna evidenza sopra soglia",
        null,
        new EntityId(contract.ContractId),
        contract.Supplier);

    /// <summary>The prototype's conversation title: "Supplier · " + the question (cut at 50 chars + "…" when longer than 52).</summary>
    public static string Title(string question, string? supplier)
    {
        var t = question.Trim();
        var body = t.Length > 52 ? string.Concat(t.AsSpan(0, 50), "…") : t;
        return string.IsNullOrEmpty(supplier) ? body : supplier + " · " + body;
    }

    /// <summary>Days from <paramref name="today"/> to the notice deadline (live, not the stored snapshot).</summary>
    public static int CancelDays(ContractFacts c, DateOnly today) =>
        c.NoticeDeadline is { } d ? d.DayNumber - today.DayNumber : c.CancelDays;

    public static string Thousands(decimal n) => n.ToString("#,0", CultureInfo.InvariantCulture);

    /// <summary>JavaScript <c>String(number)</c> for a price (0.7, not 0.70).</summary>
    public static string JsNumber(decimal n) => ((double)n).ToString("R", CultureInfo.InvariantCulture);

    [GeneratedRegex("mercato|benchmark|market|concorren")]
    private static partial Regex MarketPattern();
}
