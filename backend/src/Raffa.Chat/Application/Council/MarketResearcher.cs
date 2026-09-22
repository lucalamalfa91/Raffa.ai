using System.Text.Json;
using System.Text.Json.Serialization;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Chat.Application.Pack;
using Raffa.Chat.Application.Planning;

namespace Raffa.Chat.Application.Council;

/// <summary>What the market researcher brought back: the market notes to add to the pack, the
/// queries it ran, whether it ran at all, and why it failed when it did.</summary>
public sealed record MarketResearchOutcome(
    IReadOnlyList<PackItem> Items,
    IReadOnlyList<string> Queries,
    bool Ran,
    string? Failure)
{
    public static MarketResearchOutcome Skipped { get; } = new([], [], false, null);
}

/// <summary>
/// The market researcher (<see cref="CouncilAgents.MarketResearcherName"/>): the Ask flow's agent
/// for the market RAG. The model writes the searches — the same supplier first, then similar or
/// related contracts — from the question, the contract's own items, what the deterministic market
/// data check already found and what the contract is missing; this type runs them against
/// <see cref="IMarketRagSearch"/> and returns the notes as <see cref="PackCorpus.Market"/> items.
///
/// <para>
/// The model only chooses what to look for; everything it brings back is the RAG's own records, so
/// the answer's grounding and numeric guards police those figures like any other market item. A
/// query is trimmed and bounded, the notes are de-duplicated against the pack and each other and
/// capped (<see cref="CouncilOptions.MarketResearchMaxItems"/>), and a note found by a
/// "similar-contracts" query is labelled as such, so the answer never presents another supplier's
/// deal as this supplier's. A failed call or search degrades the flow, never the turn.
/// </para>
/// </summary>
public sealed class MarketResearcher(IAiGateway aiGateway, CouncilOptions options, IMarketRagSearch? marketRag = null)
{
    public const string SimilarContractsScope = "similar-contracts";

    private const int MaxQueryLength = 160;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public async Task<MarketResearchOutcome> QueryMarketRagAsync(
        string question,
        SavingsGoal? goal,
        IReadOnlyList<PackItem> pack,
        IReadOnlyList<string> missingFields,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(missingFields);

        if (marketRag is null || !options.MarketResearchEnabled)
        {
            return MarketResearchOutcome.Skipped;
        }

        var input = new ResearcherInput(
            question,
            goal is null || !goal.HasTarget ? null : new GoalDto(goal.TargetAmount, goal.TargetPercent, goal.Currency, goal.WindowDays, goal.WindowLabel),
            missingFields,
            pack.Where(i => i.Corpus != PackCorpus.Raffa).Take(options.MaxItemsPerAgent).Select(ToItemDto).ToList());

        var result = await aiGateway.AnalyzeAsync(
                new AiAnalysisRequest(
                    CouncilAgents.MarketResearcherName,
                    CouncilAgents.MarketResearcherPrompt,
                    JsonSerializer.Serialize(input, JsonOptions),
                    CouncilAgents.QueriesSchema,
                    CouncilAgents.Version),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return new MarketResearchOutcome([], [], true, result.Error);
        }

        QueriesPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<QueriesPayload>(result.Value.PayloadJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            return new MarketResearchOutcome([], [], true, $"payload was not valid JSON — {ex.Message}");
        }

        var queries = (payload?.Queries ?? [])
            .Where(q => !string.IsNullOrWhiteSpace(q.Query))
            .Select(q => q with { Query = Bound(q.Query) })
            .DistinctBy(q => q.Query, StringComparer.OrdinalIgnoreCase)
            .Take(options.MarketResearchMaxQueries)
            .ToList();

        var seen = pack.Select(i => i.CitationKey).ToHashSet(StringComparer.Ordinal);
        var items = new List<PackItem>();
        var failures = new List<string>();

        foreach (var query in queries)
        {
            if (items.Count >= options.MarketResearchMaxItems)
            {
                break;
            }

            var notes = await marketRag.SearchAsync(query.Query, options.MarketResearchTopK, cancellationToken).ConfigureAwait(false);
            if (notes.IsFailure)
            {
                failures.Add($"'{query.Query}': {notes.Error}");
                continue;
            }

            var similar = string.Equals(query.Scope, SimilarContractsScope, StringComparison.OrdinalIgnoreCase);
            foreach (var note in notes.Value)
            {
                if (items.Count >= options.MarketResearchMaxItems || !seen.Add(note.CitationKey))
                {
                    continue;
                }

                items.Add(Label(note, similar));
            }
        }

        return new MarketResearchOutcome(
            items,
            queries.Select(q => q.Query).ToList(),
            true,
            failures.Count == 0 ? null : string.Join("; ", failures));
    }

    /// <summary>Provenance says the note came from the market RAG through this agent; a note found
    /// for similar contracts says so in its subtitle, which the answer reads.</summary>
    private static PackItem Label(PackItem note, bool similar) =>
        note with
        {
            Subtitle = similar ? $"similar contract · {note.Subtitle ?? "market RAG"}" : note.Subtitle,
            Provenance = $"market RAG · {CouncilAgents.MarketResearcherName} · {note.Provenance}",
        };

    private static string Bound(string query)
    {
        var trimmed = string.Join(' ', query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return trimmed.Length <= MaxQueryLength ? trimmed : trimmed[..MaxQueryLength];
    }

    private static ItemDto ToItemDto(PackItem item) =>
        new(item.CitationKey, item.Corpus, item.Title, item.Subtitle, item.Snippet,
            item.Values.Select(v => new ValueDto(v.Key, v.Value, v.Kind.ToString(), v.Currency)).ToList());

    // ----- wire shapes (camelCase JSON; the fixture gateway mirrors "question"/"items") -----

    private sealed record GoalDto(decimal? TargetAmount, decimal? TargetPercent, string? Currency, int? WindowDays, string? WindowLabel);

    private sealed record ValueDto(string Key, string Value, string Kind, string? Currency);

    private sealed record ItemDto(string CitationKey, string Corpus, string Title, string? Subtitle, string Snippet, IReadOnlyList<ValueDto> Values);

    private sealed record ResearcherInput(string Question, GoalDto? Goal, IReadOnlyList<string> MissingFields, IReadOnlyList<ItemDto> Items);

    private sealed record QueriesPayload(IReadOnlyList<ResearchQuery>? Queries);

    private sealed record ResearchQuery(string Query, string? Scope);
}
