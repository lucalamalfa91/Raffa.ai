using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;

namespace Raffa.AiFlows.Ask.Orchestration;

/// <summary>
/// What the model made of a follow-up turn: the supplier the user is still talking about (one of the
/// tenant's own, exactly as listed) and the question rewritten so it stands on its own.
/// </summary>
public sealed record ConversationFocus(string? SupplierName, string StandaloneQuestion);

/// <summary>
/// Conversation-focus resolution (condense-question / coreference step): a follow-up such as
/// "mostramelo con dei kpi" or "questo contratto" carries no supplier of its own, and every later
/// stage (gate, planner, ambiguity check, pack builders) reads only the current question, so the
/// contract the chat is about used to be lost after the first turn. One schema-constrained
/// <c>extract</c> call reads the recent turns against the tenant's own suppliers and returns the
/// supplier still in focus plus a self-contained question. The model only picks from the listed
/// suppliers (anything else is discarded) and never answers; any gateway failure or malformed answer
/// changes nothing, so the turn behaves exactly as before.
/// </summary>
public sealed class ConversationFocusResolver(IAiGateway aiGateway, ILogger<ConversationFocusResolver>? logger = null)
{
    public const string StageName = "conversation-focus";
    private const int MaxTurns = 6;
    private const int MaxTurnChars = 700;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ConversationFocus?> ResolveAsync(
        string question,
        IReadOnlyList<(string Role, string Markdown)> recentTurns,
        IReadOnlyCollection<string> knownSuppliers,
        CancellationToken cancellationToken)
    {
        if (recentTurns.Count == 0 || knownSuppliers.Count == 0 || string.IsNullOrWhiteSpace(question))
        {
            return null;
        }

        try
        {
            var result = await aiGateway
                .ExtractAsync(new AiExtractionRequest(StageName, Prompt(question, recentTurns, knownSuppliers), Schema()), cancellationToken)
                .ConfigureAwait(false);
            if (result.IsSuccess && Read(result.Value.PayloadJson, knownSuppliers) is { } focus)
            {
                return focus;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogWarning(exception, "Conversation focus resolution failed; the turn is read on its own.");
        }

        return null;
    }

    private static string Prompt(string question, IReadOnlyList<(string Role, string Markdown)> turns, IReadOnlyCollection<string> suppliers)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You resolve what a procurement chat message refers to. You never answer it.");
        sb.AppendLine("Decide whether the NEW message depends on the earlier conversation (a pronoun or deictic phrase such as");
        sb.AppendLine("\"it\", \"mostramelo\", \"questo contratto\", \"this contract\", or an elliptical request such as \"con dei kpi\").");
        sb.AppendLine("If it does: set followUp=true, set supplier to the listed supplier the conversation is about (exactly as listed;");
        sb.AppendLine("null if no single listed supplier is in focus), and rewrite the message as a self-contained question in the");
        sb.AppendLine("SAME language, naming that supplier, changing nothing else (no new facts, no answer).");
        sb.AppendLine("If the message already stands on its own (it names its own subject, or is a portfolio-wide question): followUp=false,");
        sb.AppendLine("supplier=null, standaloneQuestion = the message unchanged. A supplier the user names in the NEW message wins over earlier ones.");
        sb.AppendLine("Suppliers: " + string.Join("; ", suppliers));
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
            followUp = new { type = "boolean", description = "True only when the new message depends on the earlier conversation." },
            supplier = new { type = new[] { "string", "null" }, description = "One of the listed suppliers, exactly as listed, or null." },
            standaloneQuestion = new { type = "string", description = "The new message made self-contained, same language, same meaning." },
        },
        required = new[] { "followUp", "supplier", "standaloneQuestion" },
        additionalProperties = false,
    });

    private static ConversationFocus? Read(string payload, IReadOnlyCollection<string> suppliers)
    {
        Answer? answer;
        try
        {
            answer = JsonSerializer.Deserialize<Answer>(payload, Json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (answer is null || !answer.FollowUp || string.IsNullOrWhiteSpace(answer.StandaloneQuestion))
        {
            return null;
        }

        var supplier = answer.Supplier is { Length: > 0 } s
            ? suppliers.FirstOrDefault(x => string.Equals(x, s, StringComparison.OrdinalIgnoreCase))
            : null;
        return new ConversationFocus(supplier, answer.StandaloneQuestion.Trim());
    }

    private sealed record Answer(bool FollowUp, string? Supplier, string? StandaloneQuestion);
}
