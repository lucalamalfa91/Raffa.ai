using System.Text.Json;
using System.Text.Json.Serialization;
using Raffa.AiFlows.Ask.Interview;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Routing;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Application.Memory;
using Raffa.Chat.Application.Reply;
using Raffa.Documents.Contracts.Application;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Raffa.Suppliers.Products.Application;
using static Raffa.AiFlows.Ask.Packs.ContractResolution;

namespace Raffa.AiFlows.Ask.Agentic;

/// <summary>
/// One Ask turn as the tools see it (ADR-024 amendment 2026-11-05): the tenant, the caller, the
/// portfolio and the supplier names the host already resolved, the chat's scope contract, the
/// server-owned conversation state and the current artifact. A tool reads only this and its own
/// arguments; it never receives an id from the model (R-ASK-08) -- it resolves a supplier name
/// inside <see cref="Portfolio"/> through <see cref="ResolveContract"/>.
/// </summary>
public sealed class AskToolContext(
    TenantId tenantId,
    string actor,
    string question,
    string language,
    IReadOnlyList<(string Role, string Markdown)> recentTurns,
    PortfolioPage portfolio,
    IReadOnlyDictionary<EntityId, string> supplierNames,
    EntityId? scopeContractId,
    PortfolioListItem? scopedContractItem,
    AskTurnHints hints)
{
    public TenantId TenantId { get; } = tenantId;
    public string Actor { get; } = actor;
    public string Question { get; } = question;
    public string Language { get; } = language;
    public IReadOnlyList<(string Role, string Markdown)> RecentTurns { get; } = recentTurns;
    public PortfolioPage Portfolio { get; } = portfolio;
    public IReadOnlyDictionary<EntityId, string> SupplierNames { get; } = supplierNames;
    public EntityId? ScopeContractId { get; } = scopeContractId;
    public PortfolioListItem? ScopedContractItem { get; } = scopedContractItem;
    public AskTurnHints Hints { get; } = hints;
    public ConversationState? ConversationState => Hints.ConversationState;
    public CurrentArtifactHint? CurrentArtifact => Hints.CurrentArtifact;
    public UserMemorySnapshot? UserMemory => Hints.UserMemory;
    public bool Italian => Language == "it";

    /// <summary>The supplier the tools resolved first on this turn (the conversation's focus for the
    /// state reducer); set by the loop, read by the host.</summary>
    public string? FocusSupplier { get; set; }

    public PortfolioListItem? FocusContractItem { get; set; }

    public IReadOnlyList<string> KnownSupplierNames => SupplierNames.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public RoutingContext RoutingContext(EntityId? contractId = null) =>
        new(Portfolio.TotalCount, CapabilityCallerRole.Standard,
            contractId ?? (FocusContractItem is { } f ? new EntityId(f.ContractId) : ScopeContractId));

    /// <summary>The known supplier the model's wording names: exact, then "contains" either way
    /// (the model writes "Coupa" for "Coupa Software"), then the server-owned primary supplier for a
    /// bare "this contract"; never a name that is not in this tenant's portfolio.</summary>
    public string? ResolveSupplierName(string? given)
    {
        var names = KnownSupplierNames;
        if (string.IsNullOrWhiteSpace(given) || given.Equals("this", StringComparison.OrdinalIgnoreCase) || given.Equals("current", StringComparison.OrdinalIgnoreCase))
        {
            if (ScopedContractItem is { } scoped && scoped.SupplierId is { } sid && SupplierNames.TryGetValue(new EntityId(sid), out var scopedName))
            {
                return scopedName;
            }

            if (FocusSupplier is not null)
            {
                return FocusSupplier;
            }

            var primary = ConversationState?.PrimarySupplier;
            return primary is not null ? names.FirstOrDefault(n => n.Equals(primary, StringComparison.OrdinalIgnoreCase)) : null;
        }

        var trimmed = given.Trim();
        return names.FirstOrDefault(n => n.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            ?? names.FirstOrDefault(n => n.Contains(trimmed, StringComparison.OrdinalIgnoreCase) || trimmed.Contains(n, StringComparison.OrdinalIgnoreCase))
            ?? names.FirstOrDefault(n => SupplierNameNormalizer.Normalize(n) == SupplierNameNormalizer.Normalize(trimmed));
    }

    /// <summary>The contract a tool works on: the supplier's soonest-ending contract (the same
    /// choice the legacy engine makes), else the chat's scope contract. Records the focus.</summary>
    public (PortfolioListItem? Item, string? SupplierName, PackItem? Disambiguation) ResolveContract(string? givenSupplier)
    {
        var supplier = ResolveSupplierName(givenSupplier);
        var (item, disambiguation, _) = ResolveNamedContractItem(
            supplier is null ? ScopedContractItem : null, supplier, Portfolio, SupplierNames);
        if (item is not null)
        {
            if (supplier is null && item.SupplierId is { } sid && SupplierNames.TryGetValue(new EntityId(sid), out var scopedName))
            {
                supplier = scopedName;
            }

            FocusSupplier ??= supplier;
            FocusContractItem ??= item;
        }

        return (item, supplier, disambiguation);
    }
}

/// <summary>
/// What a tool hands back: <see cref="ResultJson"/> for the model (pack items and a short summary,
/// never an id the model could echo into a later call), the <see cref="PackItems"/> the final
/// answer may cite (the guards run on their union), and optionally things that end up on the
/// reply: an artifact payload, UI actions, catalog actions, or a reply that ends the turn (an
/// interview, a consent request).
/// </summary>
public sealed record AskToolResult(
    string ResultJson,
    IReadOnlyList<PackItem> PackItems,
    ArtifactPayload? Artifact = null,
    IReadOnlyList<UiActionPayload>? UiActions = null,
    IReadOnlyList<CopilotAction>? Actions = null,
    CopilotReply? EndTurnReply = null,
    int MarketQueries = 0,
    string? Failure = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The one JSON shape every tool result takes: <c>items</c> (the pack items, each with
    /// its citationKey) and <c>summary</c> (whatever the tool wants to say in addition).</summary>
    public static AskToolResult FromPack(IReadOnlyList<PackItem> items, object? summary = null, int marketQueries = 0) =>
        new(Serialize(items, summary), items, MarketQueries: marketQueries);

    public static AskToolResult Error(string message) =>
        new(JsonSerializer.Serialize(new { items = Array.Empty<object>(), error = message }, Json), [], Failure: message);

    public static string Serialize(IReadOnlyList<PackItem> items, object? summary) =>
        JsonSerializer.Serialize(new
        {
            items = items.Select(i => new
            {
                citationKey = i.CitationKey,
                corpus = i.Corpus,
                title = i.Title,
                subtitle = i.Subtitle,
                snippet = i.Snippet,
                page = i.Page,
                section = i.Section,
                provenance = i.Provenance,
                // display: the value in the user's language and the contract's currency, for the model to copy.
                values = i.Values.Select(v => new { v.Key, v.Value, v.Kind, v.Currency, Display = AnswerFormatter.Display(v, Raffa.SharedKernel.Language.ResponseLanguage.Current ?? "it") }),
            }),
            summary,
        }, Json);
}

/// <summary>A tool the orchestrator may call: name and description (what the model reads to
/// decide), the strict JSON schema of its arguments, and the execution over the turn's context.</summary>
public interface IAskTool
{
    string Name { get; }

    string Description { get; }

    /// <summary>Strict: <c>additionalProperties: false</c>, every property required (a nullable
    /// type expresses "optional").</summary>
    string ParametersSchemaJson { get; }

    Task<AskToolResult> ExecuteAsync(JsonElement arguments, AskToolContext context, CancellationToken cancellationToken);
}

internal static class ToolArguments
{
    public static string? GetString(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? (string.IsNullOrWhiteSpace(p.GetString()) ? null : p.GetString()!.Trim())
            : null;

    public static bool GetBool(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    public static string Schema(params (string Name, string Type, string Description, bool Nullable)[] properties)
    {
        var props = properties.ToDictionary(
            p => p.Name,
            p => (object)(p.Nullable
                ? new { type = new[] { p.Type, "null" }, description = p.Description }
                : new { type = (object)p.Type, description = p.Description }));
        return JsonSerializer.Serialize(new
        {
            type = "object",
            additionalProperties = false,
            required = properties.Select(p => p.Name).ToArray(),
            properties = props,
        });
    }

    public static string EnumSchema(string name, string description, string[] values, params (string Name, string Type, string Description, bool Nullable)[] others)
    {
        var props = others.ToDictionary(
            p => p.Name,
            p => (object)(p.Nullable
                ? new { type = new[] { p.Type, "null" }, description = p.Description }
                : new { type = (object)p.Type, description = p.Description }));
        props[name] = new { type = "string", description, @enum = values };
        return JsonSerializer.Serialize(new
        {
            type = "object",
            additionalProperties = false,
            required = others.Select(p => p.Name).Append(name).ToArray(),
            properties = props,
        });
    }
}
