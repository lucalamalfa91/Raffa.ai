using System.Globalization;
using System.Text.RegularExpressions;
using Raffa.AiFlows.Negotiation.Agents;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Planning;
using Raffa.AiGateway.Telemetry;

namespace Raffa.AiFlows.Negotiation.Orchestration;

/// <summary>The deterministic market data check's result: the pack items it adds (the contract's
/// facts, what it is missing, the market's estimate and practice, comparable deals) and the missing
/// fields, which the market researcher reads to decide what is still worth looking up.</summary>
public sealed record MarketDataCheckResult(IReadOnlyList<PackItem> Items, IReadOnlyList<string> MissingFields)
{
    public static MarketDataCheckResult None { get; } = new([], []);
}

/// <summary>
/// One Ask turn's input to <see cref="AskAgentFlow"/>.
/// </summary>
/// <param name="Question">The user's question.</param>
/// <param name="Pack">The intent's own pack, in priority order.</param>
/// <param name="Goal">The savings goal the planner parsed, if any.</param>
/// <param name="MarketDataCheck">Step 1, supplied by the composition root for a commercial turn —
/// over the contract the turn is about or, on a multi-contract turn (a quarter, savings across
/// contracts), the contracts the pack is about; it alone can read the contracts, their benchmarks
/// and the market deals. <see langword="null"/> skips the step.</param>
/// <param name="RunMarketResearch">Whether step 2, the market researcher, runs this turn.</param>
/// <param name="ConveneCouncil">Whether step 3, the negotiation council, convenes this turn.</param>
public sealed record AskFlowRequest(
    string Question,
    IReadOnlyList<PackItem> Pack,
    SavingsGoal? Goal,
    Func<CancellationToken, Task<MarketDataCheckResult>>? MarketDataCheck,
    bool RunMarketResearch,
    bool ConveneCouncil);

/// <summary>What the flow produced: the market items to append to the pack (steps 1 and 2), the
/// council's plays and verdict to insert (step 3), every step that ran, in order, and every failure
/// (a failed step degrades the flow, never the turn). <see cref="RunId"/> is the
/// <see cref="RunContext"/> run the flow's AI calls were audited under.</summary>
public sealed record AskFlowOutcome(
    IReadOnlyList<PackItem> MarketItems,
    IReadOnlyList<PackItem> CouncilItems,
    IReadOnlyList<string> StepsRun,
    IReadOnlyList<string> Failures,
    IReadOnlyList<string> MarketQueries,
    string? RunId = null)
{
    private static readonly Regex StepNamePattern = new("^[a-z][a-z0-9-]{0,39}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The flow's audit detail (plan F2-T01): the run id, which steps ran and how many, which steps
    /// failed and how many, and how many market queries the researcher issued. Names and counts
    /// only -- never a query, a failure message (it can quote model output) or any pack text.
    /// </summary>
    public string ToAuditDetail()
    {
        var failedSteps = Failures
            .Select(f => f.Split(':', 2)[0].Trim())
            .Select(name => StepNamePattern.IsMatch(name) ? name : "unknown")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return string.Create(CultureInfo.InvariantCulture,
            $"runId={RunId ?? "none"} stepsRun={StepsRun.Count} steps={Join(StepsRun)} " +
            $"failures={Failures.Count} failedSteps={Join(failedSteps)} " +
            $"marketQueries={MarketQueries.Count} marketItems={MarketItems.Count} councilItems={CouncilItems.Count}");
    }

    private static string Join(IReadOnlyList<string> names) => names.Count == 0 ? "none" : string.Join(',', names);
}

/// <summary>
/// The agentic process behind an Ask turn, one coordinated sequence before the answer role writes:
/// <list type="number">
/// <item><b>Market data check</b> (<see cref="MarketDataCheckStepName"/>, deterministic): for each
/// contract the turn is about, which fields are missing and what the market data says in their place
/// — a narrow annual-value estimate, the notice deadline comparable customers' notice implies, the
/// terms they negotiated, the closest comparable deals, and across several contracts a coverage
/// line — computed in code.</item>
/// <item><b>Market researcher</b> (<see cref="MarketResearcher"/>, an agent): reads the contract and
/// step 1's findings and queries the market RAG for what is still missing — the same supplier first,
/// then similar or related contracts.</item>
/// <item><b>Negotiation council</b> (<see cref="NegotiationCouncil"/>, three agents), on a savings or
/// negotiation turn: the contract and market analysts in parallel over the pack enriched by steps 1
/// and 2, then the lever strategist.</item>
/// </list>
/// Each step sees what the steps before it found; everything they produce enters the answer only as
/// pack items, under the same grounding and numeric guards as the rest of the pack. Which of the
/// gaps a question needs is for the researcher and the answer role to judge (persona v2.5): say what
/// is missing, then answer with the market's figures, labelled as estimates.
/// </summary>
public sealed class AskAgentFlow(MarketResearcher marketResearcher, NegotiationCouncil negotiationCouncil)
{
    public const string MarketDataCheckStepName = "market-data-check";

    public async Task<AskFlowOutcome> RunAsync(AskFlowRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Question);
        ArgumentNullException.ThrowIfNull(request.Pack);

        // Every AI call below is audited with a run id and its step's name (plan T-01 / F2-D01),
        // inside the turn the caller opened: the caller's run when it opened one (an Ask turn is one
        // run), a run of its own otherwise.
        using var run = RunContext.Current?.RunId is null ? RunContext.BeginRun() : null;
        var runId = RunContext.Current?.RunId;

        var steps = new List<string>();
        var failures = new List<string>();
        var seen = request.Pack.Select(i => i.CitationKey).ToHashSet(StringComparer.Ordinal);
        var marketItems = new List<PackItem>();

        void AddNew(IEnumerable<PackItem> items) => marketItems.AddRange(items.Where(i => seen.Add(i.CitationKey)));

        // ----- Step 1: the deterministic market data check -----
        var check = MarketDataCheckResult.None;
        if (request.MarketDataCheck is { } dataCheck)
        {
            using (RunContext.BeginStep(MarketDataCheckStepName))
            {
                check = await dataCheck(cancellationToken).ConfigureAwait(false);
            }

            steps.Add(MarketDataCheckStepName);
            AddNew(check.Items);
        }

        // ----- Step 2: the market researcher, over the pack plus step 1's findings -----
        IReadOnlyList<string> queries = [];
        if (request.RunMarketResearch)
        {
            MarketResearchOutcome research;
            using (RunContext.BeginStep(CouncilAgents.MarketResearcherName))
            {
                research = await marketResearcher
                    .QueryMarketRagAsync(request.Question, request.Goal, [.. request.Pack, .. marketItems], check.MissingFields, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (research.Ran)
            {
                steps.Add(CouncilAgents.MarketResearcherName);
                queries = research.Queries;
            }

            if (research.Failure is not null)
            {
                failures.Add($"{CouncilAgents.MarketResearcherName}: {research.Failure}");
            }

            AddNew(research.Items);
        }

        // ----- Step 3: the negotiation council, over everything the market steps found -----
        var councilItems = (IReadOnlyList<PackItem>)[];
        if (request.ConveneCouncil)
        {
            var council = await negotiationCouncil
                .RunAsync(request.Question, [.. request.Pack, .. marketItems], request.Goal, cancellationToken)
                .ConfigureAwait(false);

            steps.AddRange(council.AgentsRun);
            failures.AddRange(council.Failures);
            councilItems = council.Items;
        }

        return new AskFlowOutcome(marketItems, councilItems, steps, failures, queries, runId);
    }
}
