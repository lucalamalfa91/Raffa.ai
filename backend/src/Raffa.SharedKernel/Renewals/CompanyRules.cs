using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Raffa.SharedKernel.Renewals;

/// <summary>Defaults and bounds of the company rules Raffa turns into alerting parameters.</summary>
public static class CompanyRulesDefaults
{
    /// <summary>How many days before the notice deadline a renewal is brought to Oggi when the
    /// company has not said otherwise.</summary>
    public const int StartWindowDays = 120;

    public const int MinStartWindowDays = 14;

    public const int MaxStartWindowDays = 365;

    /// <summary>The longest rules text stored (a hand-written prompt or the gist of a policy).</summary>
    public const int MaxTextLength = 12_000;

    public static int ClampStartWindow(int days) => Math.Clamp(days, MinStartWindowDays, MaxStartWindowDays);
}

/// <summary>A tenant's company rules: the text Raffa reads, plus the alerting parameters taken from it.</summary>
/// <param name="Text">The rules, free text (a hand-written prompt, or what Raffa distilled from a policy).</param>
/// <param name="StartWindowDays">Days before a notice deadline from which a renewal shows on Oggi.</param>
/// <param name="SourceFile">The policy document the rules were taken from, when one was uploaded.</param>
/// <param name="IsDefault">No rules were saved yet: the defaults apply.</param>
public sealed record CompanyRules(string Text, int StartWindowDays, string? SourceFile, DateTimeOffset? UpdatedAt, string? UpdatedBy, bool IsDefault)
{
    public static CompanyRules Default { get; } =
        new(string.Empty, CompanyRulesDefaults.StartWindowDays, null, null, null, true);
}

/// <summary>One turn of the rules interview. <see cref="Role"/> is <c>raffa</c> or <c>user</c>.</summary>
public sealed record RuleTurn(string Role, string Text);

/// <summary>What Raffa answers in the interview: the next question, or — when it has enough — a
/// proposal of the whole rules text for the user to keep or edit.</summary>
public sealed record RuleInterviewReply(string Message, string? Proposal, int? StartWindowDays, bool Done);

/// <summary>What Raffa took from an uploaded company policy.</summary>
public sealed record RuleDistillation(string Text, int? StartWindowDays, string Summary);

/// <summary>Reads and writes the interview and the policy distillation. The model-backed
/// implementation lives in AiFlows; <see cref="ScriptedCompanyRulesAssistant"/> is the deterministic
/// default and the fallback when the model is unavailable.</summary>
public interface ICompanyRulesAssistant
{
    Task<RuleInterviewReply> InterviewAsync(
        TenantId tenantId, string currentRules, IReadOnlyList<RuleTurn> history, string lang, CancellationToken cancellationToken = default);

    Task<RuleDistillation> DistillAsync(
        TenantId tenantId, string fileName, string documentText, string currentRules, string lang, CancellationToken cancellationToken = default);
}

/// <summary>
/// The deterministic interview: five fixed questions, one per turn, the answers composed into a
/// rules text. The state is the history itself (the number of user answers), so it needs no storage.
/// Also the fallback of the model-backed assistant, and the whole assistant in fixture mode.
/// </summary>
public sealed partial class ScriptedCompanyRulesAssistant : ICompanyRulesAssistant
{
    private static readonly (string En, string It)[] Questions =
    [
        ("How many days before a notice deadline do you want a renewal on your Today list? Earlier than that, Raffa stays quiet.",
         "Quanti giorni prima di una disdetta vuoi vedere il rinnovo in Oggi? Prima di allora Raffa sta zitto."),
        ("From which yearly value does a contract need someone else's approval, and whose?",
         "Da quale valore annuo un contratto richiede l'approvazione di qualcun altro, e di chi?"),
        ("Which terms do you never accept (for example automatic renewal, a low liability cap, long payment terms)?",
         "Quali condizioni non accetti mai (per esempio rinnovo automatico, limite di responsabilità basso, pagamenti lunghi)?"),
        ("Are there suppliers or categories to handle differently (strategic vendors, ones to replace, ones to leave alone)?",
         "Ci sono fornitori o categorie da trattare in modo diverso (strategici, da sostituire, da lasciare stare)?"),
        ("Anything else Raffa should know before alerting you?",
         "Altro che Raffa deve sapere prima di avvisarti?"),
    ];

    private static readonly string[] Headings = ["Timing", "Approval", "Never accept", "Suppliers", "Other"];

    public Task<RuleInterviewReply> InterviewAsync(
        TenantId tenantId, string currentRules, IReadOnlyList<RuleTurn> history, string lang, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(history);
        var it = IsItalian(lang);
        var answers = history.Where(t => t.Role == "user").Select(t => t.Text.Trim()).ToList();
        if (answers.Count < Questions.Length)
        {
            var q = Questions[answers.Count];
            var intro = answers.Count == 0 && !string.IsNullOrWhiteSpace(currentRules)
                ? (it ? "Parto dalle regole che hai già: le affino con cinque domande. " : "I start from the rules you already have and refine them with five questions. ")
                : string.Empty;
            return Task.FromResult(new RuleInterviewReply(intro + (it ? q.It : q.En), null, null, false));
        }

        var window = ParseDays(answers[0]);
        var text = Compose(answers, window);
        var message = it
            ? "Ho scritto le regole qui sotto. Controllale, modificale se serve e salvale."
            : "I wrote the rules below. Check them, edit what you need and save.";
        return Task.FromResult(new RuleInterviewReply(message, text, window, true));
    }

    public Task<RuleDistillation> DistillAsync(
        TenantId tenantId, string fileName, string documentText, string currentRules, string lang, CancellationToken cancellationToken = default)
    {
        var clipped = documentText.Length > CompanyRulesDefaults.MaxTextLength
            ? documentText[..CompanyRulesDefaults.MaxTextLength]
            : documentText;
        var summary = IsItalian(lang)
            ? "Ho incluso il testo del documento così com'è: rileggilo e tieni solo quello che conta."
            : "I kept the document's text as it is: read it through and keep only what matters.";
        return Task.FromResult(new RuleDistillation(clipped.Trim(), null, summary));
    }

    /// <summary>The number of days in an answer: "90", "90 days", "3 months", "tre mesi" is not
    /// parsed (the model-backed assistant handles prose). Null when there is no number.</summary>
    public static int? ParseDays(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return null;
        }

        var m = NumberWithUnit().Match(answer);
        if (!m.Success || !int.TryParse(m.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n <= 0)
        {
            return null;
        }

        var unit = m.Groups["u"].Value.ToLowerInvariant();
        var days = unit.StartsWith("mes", StringComparison.Ordinal) || unit.StartsWith("mon", StringComparison.Ordinal)
            ? n * 30
            : unit.StartsWith("sett", StringComparison.Ordinal) || unit.StartsWith("week", StringComparison.Ordinal) ? n * 7 : n;
        return CompanyRulesDefaults.ClampStartWindow(days);
    }

    private static string Compose(IReadOnlyList<string> answers, int? window)
    {
        var sb = new StringBuilder("# Company rules\n");
        for (var i = 0; i < Headings.Length; i++)
        {
            sb.Append("\n## ").Append(Headings[i]).Append('\n');
            if (i == 0)
            {
                sb.Append("- Bring a renewal to Today ")
                    .Append((window ?? CompanyRulesDefaults.StartWindowDays).ToString(CultureInfo.InvariantCulture))
                    .Append(" days before its notice deadline; earlier than that, stay quiet.\n");
            }

            sb.Append("- ").Append(answers[i].ReplaceLineEndings(" ")).Append('\n');
        }

        return sb.ToString();
    }

    private static bool IsItalian(string lang) => string.Equals(lang, "it", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"(?<n>\d{1,4})\s*(?<u>giorni|giorno|days?|d\b|mesi|mese|months?|settimane|settimana|weeks?)?", RegexOptions.IgnoreCase)]
    private static partial Regex NumberWithUnit();
}
