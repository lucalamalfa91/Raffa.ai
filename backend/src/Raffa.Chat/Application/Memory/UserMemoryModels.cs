using System.Text.Json;

namespace Raffa.Chat.Application.Memory;

public sealed record UserMemoryProfile(
    IReadOnlyDictionary<string, int>? IntentCounts = null,
    IReadOnlyDictionary<string, int>? WorkflowCounts = null,
    IReadOnlyDictionary<string, int>? TransitionCounts = null,
    string? LastWorkflow = null,
    string? PreferredOutput = null,
    IReadOnlyList<string>? RecentSuppliers = null,
    bool Enabled = true)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static UserMemoryProfile Empty { get; } = new(new Dictionary<string, int>(), new Dictionary<string, int>(), new Dictionary<string, int>(), RecentSuppliers: []);
    public string Serialize() => JsonSerializer.Serialize(this, Json);
    public static UserMemoryProfile Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Empty;
        try { return JsonSerializer.Deserialize<UserMemoryProfile>(value, Json) ?? Empty; }
        catch (JsonException) { return Empty; }
    }
}

/// <summary>Read-only, prompt-safe projection: counters and allowlisted supplier names, never raw turns.</summary>
public sealed record UserMemorySnapshot(
    string? PreferredOutput,
    IReadOnlyList<string> FrequentWorkflows,
    IReadOnlyList<string> FrequentIntents,
    IReadOnlyList<string> RecentSuppliers,
    IReadOnlyList<string> Suggestions,
    DateTimeOffset? ExpiresAt)
{
    public static UserMemorySnapshot Empty { get; } = new(null, [], [], [], [], null);

    public static string? SuggestionFor(string workflow, string language) => (workflow, language) switch
    {
        ("supplier_email", "it") => "Vuoi che prepari anche la mail al fornitore?",
        ("artifact", "it") => "Vuoi trasformare l'analisi in un piano operativo persistente?",
        ("rfx", "it") => "Vuoi una scorecard e una struttura RFP riutilizzabile?",
        ("benchmark", "it") => "Vuoi confrontare il contratto con il benchmark autorizzato?",
        ("savings", "it") => "Vuoi quantificare un target di saving verificabile?",
        ("supplier_email", _) => "Would you like me to draft the supplier email as well?",
        ("artifact", _) => "Would you like to turn this analysis into a persistent action plan?",
        ("rfx", _) => "Would you like a reusable scorecard and RFP structure?",
        ("benchmark", _) => "Would you like to compare the contract with the authorized benchmark?",
        ("savings", _) => "Would you like to quantify a verifiable savings target?",
        _ => null,
    };

    public static IReadOnlyList<string> SuggestionsFor(IEnumerable<string> workflows, string language) =>
        workflows.Select(x => SuggestionFor(x, language)).Where(x => x is not null).Cast<string>().Take(3).ToList();
}
