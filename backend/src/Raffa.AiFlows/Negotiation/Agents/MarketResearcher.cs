using Raffa.AiFlows.Negotiation.Options;
using Raffa.AiFlows.Negotiation.Tools;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Planning;
using Raffa.AiGateway;
using Raffa.AiGateway.Agents;

namespace Raffa.AiFlows.Negotiation.Agents;

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
public sealed class MarketResearcher
{
    public const string SimilarContractsScope = "similar-contracts";

    private const int MaxQueryLength = 160;

    private readonly CouncilOptions options;
    private readonly IMarketRagSearch? marketRag;
    private readonly StepRunner runner;
    private readonly MarketResearcherStep step;

    /// <param name="aiGateway">The gateway the researcher's call goes through (the runner never bypasses it).</param>
    /// <param name="options">The council's options (the researcher's switches, bounds and deadline).</param>
    /// <param name="marketRag">The market RAG the queries run against; none: the researcher is skipped.</param>
    /// <param name="runner">The request's Scoped step runner; one over <paramref name="aiGateway"/> when omitted.</param>
    public MarketResearcher(IAiGateway aiGateway, CouncilOptions options, IMarketRagSearch? marketRag = null, StepRunner? runner = null)
    {
        ArgumentNullException.ThrowIfNull(aiGateway);
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
        this.marketRag = marketRag;
        this.runner = runner ?? new StepRunner(aiGateway);
        step = new MarketResearcherStep(options.AgentDeadline);
    }

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

        var input = new ResearcherStepInput(
            question,
            goal is null || !goal.HasTarget ? null : new ResearcherGoal(goal.TargetAmount, goal.TargetPercent, goal.Currency, goal.WindowDays, goal.WindowLabel),
            missingFields,
            pack.Where(i => i.Corpus != PackCorpus.Raffa).Take(options.MaxItemsPerAgent).Select(ToItem).ToList());

        var result = await runner.RunAsync(step, input, cancellationToken).ConfigureAwait(false);

        // A failed call (gateway error, unusable payload, deadline) still counts as a run, with the
        // reason as the failure line, exactly as the hand-built call reported it.
        if (!result.IsSuccess)
        {
            return new MarketResearchOutcome([], [], true, result.Error ?? result.Reason.ToString());
        }

        var payload = result.Value;

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

    private static ResearcherItem ToItem(PackItem item) =>
        new(item.CitationKey, item.Corpus, item.Title, item.Subtitle, item.Snippet,
            item.Values.Select(v => new ResearcherValue(v.Key, v.Value, v.Kind.ToString(), v.Currency)).ToList());
}
