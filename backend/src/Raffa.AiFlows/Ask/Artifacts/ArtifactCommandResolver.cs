using System.Text.RegularExpressions;
using Raffa.AiFlows.Ask.Interview;
using Raffa.Chat.Application.Conversations;

namespace Raffa.AiFlows.Ask.Artifacts;

public enum ArtifactCommandKind
{
    None,
    Create,
    Open,
    ProposeUpdate,
    ConfirmUpdate,
    CancelUpdate,
}

/// <summary>
/// A resolved artifact command. <see cref="UsesMarket"/> / <see cref="UsesPortfolio"/> are the scope
/// the composer builds its fact pack on (market notes from the RAG, the whole portfolio).
/// </summary>
public sealed record ArtifactCommand(
    ArtifactCommandKind Command,
    string Kind,
    string? Supplier,
    string? Instruction = null,
    bool UsesMarket = false,
    bool UsesPortfolio = false);

/// <summary>
/// Deterministic safety net for the low-entropy artifact commands that caused the reliability
/// incident. It never performs retrieval and never chooses an id supplied by the model: Open can
/// only target the last artifact the authenticated conversation already persisted.
///
/// <para>
/// <b>Which contract.</b> The supplier is, in order: the one the message names, the server-owned
/// primary supplier of the conversation (<see cref="ConversationState.PrimarySupplier"/>), the
/// focus of the current artifact, then a supplier the <em>user</em> named in the recent turns.
/// Raffa's own replies are never scanned: a portfolio answer lists every supplier, and the first
/// name in it is not the one the conversation is about (the "crea un kpi" → wrong-supplier incident).
/// </para>
/// </summary>
public static partial class ArtifactCommandResolver
{
    [GeneratedRegex(@"\b(apri(?:lo|la)?|visualizza(?:lo|la)?|mostra(?:lo|la)?|open(?:\s+it)?|action\s*panel|pannello\s+(?:action|azioni))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OpenPattern();

    [GeneratedRegex(@"\b(crea(?:lo|la|mi|me)?|costruisci(?:lo|la|mi)?|genera(?:lo|la|mi)?|prepara(?:lo|la|mi)?|fammi|make|create|build|prepare|generate)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CreatePattern();

    [GeneratedRegex(@"\b(aggiung(?:i|ila|ilo)|aggiorna(?:lo|la)?|modifica(?:lo|la)?|inserisci(?:la|lo)?|append|add|update|revise)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UpdatePattern();

    [GeneratedRegex(@"^\s*(?:(?:s[iì]|yes)\s*[,;:]?\s*)?(confermo|procedi|vai|confirm|go\s+ahead|do\s+it|s[iì]|yes)\s*[.!]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConfirmPattern();

    [GeneratedRegex(@"^\s*(no|annulla|lascia\s+stare|cancel|do\s+not)\s*[.!]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CancelPattern();

    [GeneratedRegex(@"\b(kpi|indicator[ei])\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KpiPattern();

    [GeneratedRegex(@"\b(dashboard|visualizzatore|viewer|grafic[oi]|chart)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DashboardPattern();

    [GeneratedRegex(@"\b(brief|documento|document|report|relazione|riepilogo|sintesi|summary|piano\s+d['’]azione|action\s+plan)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DocumentPattern();

    [GeneratedRegex(@"\b(mercato|market|benchmark\w*|concorren\w*|competitor\w*|prezz\w+\s+medi\w*|media\s+di\s+mercato|peer|similar[ie]?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarketPattern();

    /// <summary>True when <paramref name="text"/> asks for the market (benchmarks, competitors, peers).</summary>
    internal static bool MentionsMarket(string? text) => !string.IsNullOrWhiteSpace(text) && MarketPattern().IsMatch(text);

    [GeneratedRegex(@"\b(portafoglio|portfolio|tutti\s+i\s+contratti|all\s+(?:the\s+)?contracts|ogni\s+contratto|every\s+contract)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PortfolioPattern();

    // "ma il contratto era Coupa", "intendevo Notion", "I meant Coupa", "wrong contract, it's Coupa":
    // a correction word followed (through a few fixed filler words at most) by the supplier's name.
    // A question that merely contains "era"/"meant" elsewhere ("qual era il prezzo di Notion?") is not one.
    private const string CorrectionLead =
        @"(?:\b(?:era|erano|intendevo|intendo|volevo|dicevo|parlavo\s+di|sbagliat[oa]|meant|mean|wrong|should\s+be|not)\b|^\s*no[,.]?)";

    private const string CorrectionFiller =
        @"(?:[\s,]+(?:il|lo|la|di|the|contract|contratto|supplier|fornitore|it['’]s|is|era|was|one|that|quello|questo))*";

    private static bool IsCorrectionTo(string question, string supplier) =>
        Regex.IsMatch(
            question,
            CorrectionLead + CorrectionFiller + @"[\s,]+" + Regex.Escape(supplier) + @"\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));

    private const int CorrectionMaxWords = 12;

    public static ArtifactCommand Resolve(
        string question,
        IReadOnlyList<(string Role, string Markdown)> recentTurns,
        CurrentArtifactHint? currentArtifact,
        IReadOnlyCollection<string> knownSuppliers,
        ConversationState? state = null)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return new ArtifactCommand(ArtifactCommandKind.None, "dashboard", null);
        }

        var namedInQuestion = knownSuppliers.FirstOrDefault(name => ContainsName(question, name));

        if (currentArtifact is not null && OpenPattern().IsMatch(question) && !CreatePattern().IsMatch(question))
        {
            return new ArtifactCommand(ArtifactCommandKind.Open, currentArtifact.Kind, currentArtifact.FocusSupplier);
        }

        if (currentArtifact is not null && state?.PendingArtifactInstruction is not null && ConfirmPattern().IsMatch(question))
        {
            return new ArtifactCommand(ArtifactCommandKind.ConfirmUpdate, currentArtifact.Kind, currentArtifact.FocusSupplier, state.PendingArtifactInstruction);
        }

        if (currentArtifact is not null && state?.PendingArtifactInstruction is not null && CancelPattern().IsMatch(question))
        {
            return new ArtifactCommand(ArtifactCommandKind.CancelUpdate, currentArtifact.Kind, currentArtifact.FocusSupplier);
        }

        // "ma il contratto era Coupa" right after a KPI built on another supplier: the same artifact,
        // rebuilt on the supplier the user names. A correction is short and names a known supplier
        // that is not the artifact's focus; it never needs a creation verb.
        if (currentArtifact is not null && namedInQuestion is not null
            && !string.Equals(namedInQuestion, currentArtifact.FocusSupplier, StringComparison.OrdinalIgnoreCase)
            && WordCount(question) <= CorrectionMaxWords
            && (IsCorrectionTo(question, namedInQuestion) || CreatePattern().IsMatch(question)))
        {
            return new ArtifactCommand(ArtifactCommandKind.Create, KindOf(question, string.Empty) ?? currentArtifact.Kind, namedInQuestion,
                UsesMarket: MarketPattern().IsMatch(question));
        }

        if (currentArtifact is not null && UpdatePattern().IsMatch(question))
        {
            return new ArtifactCommand(ArtifactCommandKind.ProposeUpdate, currentArtifact.Kind, currentArtifact.FocusSupplier, question.Trim());
        }

        var context = string.Join('\n', recentTurns.TakeLast(6).Select(t => t.Markdown));
        var asksToCreate = CreatePattern().IsMatch(question);
        // Pronoun-only confirmations ("crealo" / "create it") inherit only the artifact kind
        // explicitly discussed in the bounded conversation history; they never trigger retrieval.
        var asksForVisual = KpiPattern().IsMatch(question) || DashboardPattern().IsMatch(question) || DocumentPattern().IsMatch(question)
            || KpiPattern().IsMatch(context) || DashboardPattern().IsMatch(context) || DocumentPattern().IsMatch(context);
        if (!asksToCreate || !asksForVisual)
        {
            return new ArtifactCommand(ArtifactCommandKind.None, "dashboard", null);
        }

        var kind = KindOf(question, context) ?? "dashboard";

        var userContext = string.Join('\n', recentTurns.TakeLast(6).Where(t => !IsRaffa(t.Role)).Select(t => t.Markdown));
        var usesPortfolio = PortfolioPattern().IsMatch(question);
        var supplier = usesPortfolio ? null
            : namedInQuestion
              ?? state?.PrimarySupplier
              ?? currentArtifact?.FocusSupplier
              ?? knownSuppliers.FirstOrDefault(name => ContainsName(userContext, name));
        return new ArtifactCommand(ArtifactCommandKind.Create, kind, supplier,
            UsesMarket: MarketPattern().IsMatch(question),
            UsesPortfolio: usesPortfolio || supplier is null && PortfolioPattern().IsMatch(userContext));
    }

    /// <summary>The kind the message names (a KPI or a document; "visualizzatore"/"grafico" is how it is
    /// shown, not what it is), else the one the history discussed; null when neither says.</summary>
    private static string? KindOf(string question, string context) =>
        KpiPattern().IsMatch(question) ? "kpi"
        : DocumentPattern().IsMatch(question) ? "document"
        : KpiPattern().IsMatch(context) ? "kpi"
        : DocumentPattern().IsMatch(context) ? "document"
        : DashboardPattern().IsMatch(question) || DashboardPattern().IsMatch(context) ? "dashboard"
        : null;

    private static bool IsRaffa(string role) =>
        role.Equals("raffa", StringComparison.OrdinalIgnoreCase) || role.Equals("assistant", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsName(string text, string name) =>
        !string.IsNullOrWhiteSpace(name) && text.Contains(name, StringComparison.OrdinalIgnoreCase);

    private static int WordCount(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
}
