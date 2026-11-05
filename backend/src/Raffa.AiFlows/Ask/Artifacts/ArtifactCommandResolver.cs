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

public sealed record ArtifactCommand(ArtifactCommandKind Command, string Kind, string? Supplier, string? Instruction = null);

/// <summary>
/// Deterministic safety net for the two low-entropy commands that caused the reliability incident.
/// It never performs retrieval and never chooses an id supplied by the model: Open can only target
/// the last artifact the authenticated conversation already persisted.
/// </summary>
public static partial class ArtifactCommandResolver
{
    [GeneratedRegex(@"\b(apri(?:lo|la)?|visualizza(?:lo|la)?|mostra(?:lo|la)?|open(?:\s+it)?|action\s*panel|pannello\s+(?:action|azioni))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OpenPattern();

    [GeneratedRegex(@"\b(crea(?:lo|la)?|costruisci(?:lo|la)?|genera(?:lo|la)?|make|create|build)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CreatePattern();

    [GeneratedRegex(@"\b(aggiung(?:i|ila|ilo)|aggiorna(?:lo|la)?|modifica(?:lo|la)?|inserisci(?:la|lo)?|append|add|update|revise)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UpdatePattern();

    [GeneratedRegex(@"^\s*(?:(?:s[iì]|yes)\s*[,;:]?\s*)?(confermo|procedi|vai|confirm|go\s+ahead|do\s+it|s[iì]|yes)\s*[.!]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConfirmPattern();

    [GeneratedRegex(@"^\s*(no|annulla|lascia\s+stare|cancel|do\s+not)\s*[.!]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CancelPattern();

    [GeneratedRegex(@"\b(kpi|indicator[ei])\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KpiPattern();

    [GeneratedRegex(@"\b(dashboard|visualizzatore|viewer|grafico|chart)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DashboardPattern();

    [GeneratedRegex(@"\b(brief|documento|document|piano\s+d['’]azione|action\s+plan)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DocumentPattern();

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

        var kind = KpiPattern().IsMatch(question) || KpiPattern().IsMatch(context)
            ? "kpi"
            : DocumentPattern().IsMatch(question) || DocumentPattern().IsMatch(context) ? "document" : "dashboard";
        var supplier = currentArtifact?.FocusSupplier ?? knownSuppliers.FirstOrDefault(
            name => question.Contains(name, StringComparison.OrdinalIgnoreCase) || context.Contains(name, StringComparison.OrdinalIgnoreCase));
        return new ArtifactCommand(ArtifactCommandKind.Create, kind, supplier);
    }
}
