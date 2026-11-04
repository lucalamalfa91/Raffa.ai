using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Chat.Domain;

namespace Raffa.AiFlows.Ask.Orchestration;

/// <summary>What the model understood of one chat turn, read against the whole conversation.</summary>
/// <param name="Label">greeting | offDomain | legal | capability | inDomain.</param>
/// <param name="Intent">The procurement intent when <paramref name="Label"/> is inDomain.</param>
/// <param name="SupplierName">One of the tenant's own suppliers, exactly as listed, still in focus; or null.</param>
/// <param name="UnknownSupplier">A supplier the user names that is NOT in the tenant's list; or null.</param>
/// <param name="StandaloneQuestion">The message made self-contained (same language, same meaning).</param>
/// <param name="Clarification">One short question, only when the conversation and the data cannot resolve what is asked; or null.</param>
public sealed record TurnUnderstandingResult(
    string Label,
    AskIntent? Intent,
    string? SupplierName,
    string? UnknownSupplier,
    string StandaloneQuestion,
    string? Clarification);

/// <summary>
/// Turn understanding (agent step <c>turn-understanding</c>): the one place that reads what the user means.
/// The gate's word lists, the intent lexicon and the ambiguity detector used to decide this from the last
/// message alone; here a model reads the whole conversation plus the tenant's suppliers and returns the
/// label, the intent, the supplier in focus, a self-contained question and, only if it truly cannot tell, one
/// clarifying question. It never answers, and it can only pick suppliers and intents from the lists it is
/// given. Retrieval, figures, guards, permissions and audit stay deterministic downstream. Any gateway
/// failure or malformed answer returns null and the turn runs on the deterministic gate/planner as before.
/// </summary>
public sealed class TurnUnderstanding(IAiGateway aiGateway, ILogger<TurnUnderstanding>? logger = null)
{
    public const string StageName = "turn-understanding";
    private const int MaxTurns = 8;
    private const int MaxTurnChars = 700;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Labels = ["greeting", "offDomain", "legal", "capability", "inDomain"];

    private static readonly (AskIntent Intent, string Meaning)[] Intents =
    [
        (AskIntent.StructuredFact, "a fact of a contract: dates, spend, notice period, status, uplift, a figure, an overview or summary of one contract or of the portfolio, KPIs"),
        (AskIntent.Clause, "what a contract's clauses say: liability, termination, confidentiality, law, SLA, penalties"),
        (AskIntent.MarketCompare, "is one named contract in line with the market / benchmark"),
        (AskIntent.RenewalStrategy, "how to approach the renewal or negotiation of one contract"),
        (AskIntent.PortfolioStrategy, "which contracts are most critical, ranking across the portfolio, where to focus"),
        (AskIntent.PortfolioMarketPosition, "which contracts are poorly positioned on the market, across the portfolio"),
        (AskIntent.Savings, "savings levers for one contract"),
        (AskIntent.PortfolioSavingsTarget, "a saving target (amount or percentage, maybe a window) across the portfolio"),
        (AskIntent.DocumentStatus, "which documents are not yet readable/askable, extraction status"),
        (AskIntent.QuoteRoute, "compare a new quote/offer from a supplier"),
        (AskIntent.Navigate, "just take me to a screen"),
        (AskIntent.WebResearch, "explicitly search the public web"),
    ];

    public async Task<TurnUnderstandingResult?> ResolveAsync(
        string question,
        IReadOnlyList<(string Role, string Markdown)> recentTurns,
        IReadOnlyCollection<string> knownSuppliers,
        bool chatIsOpenedOnAContract,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return null;
        }

        try
        {
            var result = await aiGateway
                .ExtractAsync(new AiExtractionRequest(StageName, Prompt(question, recentTurns, knownSuppliers, chatIsOpenedOnAContract), Schema()), cancellationToken)
                .ConfigureAwait(false);
            if (result.IsSuccess && Read(result.Value.PayloadJson, knownSuppliers) is { } understood)
            {
                return understood;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogWarning(exception, "Turn understanding failed; the deterministic gate and planner decide the turn.");
        }

        return null;
    }

    private static string Prompt(string question, IReadOnlyList<(string Role, string Markdown)> turns, IReadOnlyCollection<string> suppliers, bool opened)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are the understanding step of Raffa, a procurement copilot that answers only from the user's own validated contracts. You never answer the user: you decide what the NEW message means, reading it against the whole conversation, like a colleague who followed the chat would.");
        sb.AppendLine("Resolve pronouns and ellipsis ('it', 'this contract', 'mostramelo', 'e quello di prima?', 'con dei kpi') from the conversation. The supplier in focus is the one the conversation is about unless the NEW message names another.");
        sb.AppendLine("label: greeting (hello/thanks), offDomain (nothing to do with procurement or these contracts), legal (asks for legal advice), capability (asks what Raffa can do), inDomain (everything else about contracts, suppliers, spend, renewals, savings, market, documents, or a request to build a dashboard/KPIs/brief). When unsure, inDomain.");
        sb.AppendLine("intent (only for inDomain, pick the closest; StructuredFact when unsure):");
        foreach (var (intent, meaning) in Intents)
        {
            sb.AppendLine($"- {intent}: {meaning}");
        }

        sb.AppendLine("supplier: a supplier from the list below, exactly as listed, that the message (or the conversation, for a follow-up) is about; null if none or if it is portfolio-wide.");
        sb.AppendLine("unknownSupplier: a company the user names as the subject that is NOT in the list below; otherwise null.");
        sb.AppendLine("standaloneQuestion: the NEW message rewritten so it needs no conversation to be understood, same language, same meaning, naming the supplier when it is in focus; unchanged if already self-contained. Never add facts or an answer.");
        sb.AppendLine("clarification: leave null in almost every case, including broad or vague requests (answer them with a sensible default reading). Set one short question in the user's language ONLY when the conversation and the data genuinely cannot tell what is asked and any reading would likely be wrong.");
        sb.AppendLine("Suppliers: " + (suppliers.Count == 0 ? "(none yet)" : string.Join("; ", suppliers)));
        sb.AppendLine(opened ? "The chat is opened on one specific contract: 'this contract' means it unless the conversation says otherwise." : "The chat is not opened on a specific contract.");
        sb.AppendLine("Conversation (oldest first):");
        foreach (var (role, markdown) in turns.TakeLast(MaxTurns))
        {
            var text = markdown.Length > MaxTurnChars ? markdown[..MaxTurnChars] + "…" : markdown;
            sb.AppendLine($"[{role}] {text}");
        }

        sb.AppendLine("NEW message:");
        sb.AppendLine(question);
        return sb.ToString();
    }

    private static string Schema() => JsonSerializer.Serialize(new
    {
        type = "object",
        properties = new
        {
            label = new { type = "string", @enum = Labels },
            intent = new { type = "string", @enum = Intents.Select(i => i.Intent.ToString()).ToArray() },
            supplier = new { type = new[] { "string", "null" } },
            unknownSupplier = new { type = new[] { "string", "null" } },
            standaloneQuestion = new { type = "string" },
            clarification = new { type = new[] { "string", "null" } },
        },
        required = new[] { "label", "intent", "supplier", "unknownSupplier", "standaloneQuestion", "clarification" },
        additionalProperties = false,
    });

    private static TurnUnderstandingResult? Read(string payload, IReadOnlyCollection<string> suppliers)
    {
        Answer? a;
        try
        {
            a = JsonSerializer.Deserialize<Answer>(payload, Json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (a is null || a.Label is null || !Labels.Contains(a.Label) || string.IsNullOrWhiteSpace(a.StandaloneQuestion))
        {
            return null;
        }

        AskIntent? intent = Enum.TryParse<AskIntent>(a.Intent, ignoreCase: false, out var parsed) && Intents.Any(i => i.Intent == parsed) ? parsed : null;
        var supplier = a.Supplier is { Length: > 0 } s ? suppliers.FirstOrDefault(x => string.Equals(x, s, StringComparison.OrdinalIgnoreCase)) : null;
        var unknown = supplier is null && a.UnknownSupplier is { Length: > 0 } u && !suppliers.Contains(u, StringComparer.OrdinalIgnoreCase) ? u.Trim() : null;
        var clarification = string.IsNullOrWhiteSpace(a.Clarification) ? null : a.Clarification.Trim();
        return new TurnUnderstandingResult(a.Label, intent, supplier, unknown, a.StandaloneQuestion.Trim(), clarification);
    }

    private sealed record Answer(string? Label, string? Intent, string? Supplier, string? UnknownSupplier, string? StandaloneQuestion, string? Clarification);
}
