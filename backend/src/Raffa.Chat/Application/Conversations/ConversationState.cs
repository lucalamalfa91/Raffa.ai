using System.Text.Json;

namespace Raffa.Chat.Application.Conversations;

/// <summary>Server-owned, resumable Ask state. IDs are copied only from authorised server data.</summary>
public sealed record ConversationState(
    string? ActiveContractId = null,
    string? Goal = null,
    string? CurrentArtifactId = null,
    string? PendingAction = null,
    IReadOnlyList<string>? MissingInputs = null,
    string? LastSuccessfulTool = null,
    string? PrimarySupplier = null,
    string? PreviousPrimarySupplier = null,
    IReadOnlyList<string>? ComparisonSuppliers = null,
    string? LastIntent = null,
    decimal? GoalAmount = null,
    decimal? GoalPercent = null,
    string? GoalCurrency = null,
    int? GoalWindowDays = null,
    IReadOnlyList<string>? Constraints = null,
    string? PendingArtifactInstruction = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ConversationState Empty { get; } = new(MissingInputs: [], ComparisonSuppliers: [], Constraints: []);

    public static ConversationState Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Empty;
        try { return JsonSerializer.Deserialize<ConversationState>(value, Json) ?? Empty; }
        catch (JsonException) { return Empty; }
    }

    public string Serialize() => JsonSerializer.Serialize(this, Json);
}
