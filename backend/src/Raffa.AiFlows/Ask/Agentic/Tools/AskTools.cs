using System.Globalization;
using System.Text.Json;
using Raffa.AiFlows.Ask.Artifacts;
using Raffa.AiFlows.Ask.Packs;
using Raffa.AiFlows.Ask.Planning;
using Raffa.AiFlows.Ask.Reply;
using Raffa.AiFlows.Negotiation.Agents;
using Raffa.AiFlows.Negotiation.DataCheck;
using Raffa.AiFlows.Negotiation.Orchestration;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Routing;
using Raffa.AiFlows.WebResearch.Orchestration;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Reply;
using Raffa.Documents.Contracts.Application;
using Raffa.SharedKernel;
using static Raffa.AiFlows.Ask.Agentic.ToolArguments;

namespace Raffa.AiFlows.Ask.Agentic.Tools;

// The tools of Ask Raffa's orchestrator (ADR-024 amendment 2026-11-05). Each one is a thin adapter
// over a reader or a flow the engine already had; none performs retrieval the legacy handler for the
// same question would not, and none accepts an id from the model.

/// <summary>Every contract of the workspace, one row each, plus the ranked portfolio pack
/// (criticality, what to do first) -- the base for any question across contracts.</summary>
internal sealed class ListPortfolioTool(PortfolioStrategyPackBuilder portfolio) : IAskTool
{
    public string Name => "list_portfolio";

    public string Description =>
        "Every validated contract of the workspace: supplier, type, annual spend, start/end, renewal and notice deadline, auto-renewal, status, risk. " +
        "Plus the ranked portfolio view (most critical contracts first, savings opportunities). Call it for any question across contracts, " +
        "to find which supplier a vague wording refers to, or when no supplier is named and the question is not about one contract.";

    public string ParametersSchemaJson => Schema(("focus", "string", "What the question looks at: 'deadlines', 'spend', 'risk' or 'all'.", true));

    public async Task<AskToolResult> ExecuteAsync(JsonElement arguments, AskToolContext context, CancellationToken cancellationToken)
    {
        var items = await portfolio.BuildPortfolioStrategyPackAsync(context.Portfolio, context.SupplierNames, cancellationToken).ConfigureAwait(false);
        var contracts = context.Portfolio.Items.Select(i => new
        {
            supplier = i.SupplierId is { } sid && context.SupplierNames.TryGetValue(new EntityId(sid), out var n) ? n : "?",
            type = i.Type.ToString(),
            status = i.Status,
            currency = i.Currency,
            annualSpend = i.AnnualSpend,
            startDate = Date(i.StartDate),
            endDate = Date(i.EndDate),
            renewalDate = Date(i.RenewalDate),
            noticeDeadline = Date(i.CancellationDeadline),
            autoRenewal = i.AutoRenewal,
            noticePeriodDays = i.NoticePeriodDays,
            risk = i.Risk?.ToString(),
        }).ToList();
        return AskToolResult.FromPack(items, new { contracts, count = contracts.Count });
    }

    private static string? Date(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>One supplier's contract: the validated facts (dates, spend, notice, renewal, payment
/// terms), the priced lines and the excerpts of the clauses the question is about.</summary>
internal sealed class GetContractTool(StructuredFactPackBuilder facts, ContractContextReader reader) : IAskTool
{
    public string Name => "get_contract";

    public string Description =>
        "The validated facts of one supplier's contract: end date, renewal date, notice deadline and notice period, auto-renewal, annual spend and " +
        "total value, payment terms, status, risk, priced product lines, and excerpts relevant to the question. Pass the supplier as the user names it " +
        "(or 'this' for the contract the chat is opened on / the conversation's current contract).";

    public string ParametersSchemaJson => Schema(
        ("supplier", "string", "Supplier name as written by the user, or 'this' for the current contract.", false),
        ("question", "string", "What about the contract is asked, in the user's words (drives which excerpts are returned).", false));

    public async Task<AskToolResult> ExecuteAsync(JsonElement arguments, AskToolContext context, CancellationToken cancellationToken)
    {
        var (item, supplier, disambiguation) = context.ResolveContract(GetString(arguments, "supplier"));
        if (item is null)
        {
            return AskToolResult.Error(supplier is null
                ? $"No validated contract for that supplier. Known suppliers: {string.Join(", ", context.KnownSupplierNames)}."
                : $"No contract found for {supplier}.");
        }

        var question = GetString(arguments, "question") ?? context.Question;
        var items = (await facts.BuildStructuredFactPackAsync(question, item, context.Portfolio, context.SupplierNames, cancellationToken).ConfigureAwait(false)).ToList();
        if (disambiguation is not null)
        {
            items.Insert(0, disambiguation);
        }

        var contract360 = await reader.GetContract360Async(item.ContractId, cancellationToken).ConfigureAwait(false);
        if (contract360 is not null)
        {
            var displayName = supplier ?? await reader.ResolveDisplayNameAsync(item, cancellationToken).ConfigureAwait(false);
            var notice = NoticeFacts.BuildNoticeFactItem(item, contract360.Renewal, displayName);
            if (items.All(i => i.CitationKey != notice.CitationKey))
            {
                items.Add(notice);
            }
        }

        return AskToolResult.FromPack(items, new
        {
            supplier,
            type = item.Type.ToString(),
            status = item.Status,
            autoRenewal = item.AutoRenewal,
            noticePeriodDays = item.NoticePeriodDays,
            paymentTerms = contract360?.Commercials.PaymentTerms,
            renewalTermMonths = contract360?.Commercials.RenewalTermMonths,
            lineItems = contract360?.Products.Count ?? 0,
            clausesOnFile = contract360?.Clauses.Count ?? 0,
            obligationsOnFile = contract360?.Obligations.Count ?? 0,
        });
    }
}

/// <summary>The clauses of a contract (or of the whole corpus) on a topic, verbatim with page.</summary>
internal sealed class SearchClausesTool(ClausePackBuilder clauses) : IAskTool
{
    public string Name => "search_clauses";

    public string Description =>
        "Clause text on a topic (termination, notice, SLA, liability, price increase, auto-renewal, data protection, payment, audit, ...) from one supplier's " +
        "contract, or from every contract when no supplier is given. Returns the clauses verbatim with document and page. Use it for 'what does the contract say about X'.";

    public string ParametersSchemaJson => Schema(
        ("supplier", "string", "Supplier name, 'this' for the current contract, or null for the whole workspace.", true),
        ("topic", "string", "The clause topic or question, in the user's words.", false));

    public async Task<AskToolResult> ExecuteAsync(JsonElement arguments, AskToolContext context, CancellationToken cancellationToken)
    {
        var given = GetString(arguments, "supplier");
        var (item, supplier, _) = given is null ? (null, null, null) : context.ResolveContract(given);
        if (given is not null && item is null)
        {
            return AskToolResult.Error($"No validated contract for '{given}'. Known suppliers: {string.Join(", ", context.KnownSupplierNames)}.");
        }

        var topic = GetString(arguments, "topic") ?? context.Question;
        var items = await clauses.BuildClausePackAsync(context.TenantId, topic, item, cancellationToken).ConfigureAwait(false);
        return AskToolResult.FromPack(items, new { supplier, topic, found = items.Count });
    }
}

/// <summary>The renewal situation and strategy of one contract: deadlines, levers, negotiation points.</summary>
internal sealed class GetRenewalStrategyTool(RenewalStrategyPackBuilder renewal) : IAskTool
{
    public string Name => "get_renewal_strategy";

    public string Description =>
        "The renewal picture of one supplier's contract: end and notice deadlines with days left, what happens if nothing is done (auto-renewal), " +
        "the negotiation points and levers Raffa computed for this renewal, the recommended action. Use it for 'how should I approach the X renewal', " +
        "'what can I do before the deadline', 'is it urgent'.";

    public string ParametersSchemaJson => Schema(("supplier", "string", "Supplier name, or 'this' for the current contract.", false));

    public async Task<AskToolResult> ExecuteAsync(JsonElement arguments, AskToolContext context, CancellationToken cancellationToken)
    {
        var (item, supplier, _) = context.ResolveContract(GetString(arguments, "supplier"));
        if (item is null)
        {
            return AskToolResult.Error($"No validated contract for that supplier. Known suppliers: {string.Join(", ", context.KnownSupplierNames)}.");
        }

        var items = await renewal.BuildRenewalStrategyPackAsync(item, cancellationToken).ConfigureAwait(false);
        return AskToolResult.FromPack(items, new { supplier });
    }
}

/// <summary>Where a contract sits against the market: comparable deals, discount and term practice,
/// the market's estimate for what the contract lacks, and the market knowledge base's notes.</summary>
internal sealed class GetMarketBenchmarkTool(
    MarketComparePackBuilder marketCompare, MarketDataCheck dataCheck, MarketResearcher researcher) : IAskTool
{
    public string Name => "get_market_benchmark";

    public string Description =>
        "Market intelligence for one supplier's contract (or for a supplier/category when the question is general): what comparable customers paid and " +
        "negotiated (discount, uplift cap, notice, term, payment terms), how this contract's prices compare, the market's estimate for figures the contract " +
        "lacks, and notes from the market knowledge base. Every figure comes with its basis (sample, geography, company size). " +
        "Use it for 'how does X compare to the market', 'is it expensive', 'what discount can I expect', 'benchmark', 'what do others pay'.";

    public string ParametersSchemaJson => Schema(
        ("supplier", "string", "Supplier name, 'this' for the current contract, or null for a portfolio-wide market question.", true),
        ("question", "string", "The market question in the user's words (drives the knowledge-base search).", false));

    public async Task<AskToolResult> ExecuteAsync(JsonElement arguments, AskToolContext context, CancellationToken cancellationToken)
    {
        var given = GetString(arguments, "supplier");
        var (item, supplier, _) = context.ResolveContract(given);
        var question = GetString(arguments, "question") ?? context.Question;
        var items = new List<PackItem>();
        var missing = new List<string>();

        if (item is not null)
        {
            items.AddRange(await marketCompare.BuildMarketComparePackAsync(item, cancellationToken).ConfigureAwait(false));
            var check = await dataCheck.RunAsync([item], [], cancellationToken).ConfigureAwait(false);
            foreach (var i in check.Items.Where(i => items.All(x => x.CitationKey != i.CitationKey)))
            {
                items.Add(i);
            }

            missing.AddRange(check.MissingFields);
        }

        var research = await researcher.QueryMarketRagAsync(question, null, items, missing, cancellationToken).ConfigureAwait(false);
        foreach (var i in research.Items.Where(i => items.All(x => x.CitationKey != i.CitationKey)))
        {
            items.Add(i);
        }

        var marketItems = items.Count(i => i.Corpus == PackCorpus.Market);
        return AskToolResult.FromPack(items, new
        {
            supplier,
            marketItems,
            marketEmpty = marketItems == 0,
            missingOnContract = missing,
            researchRan = research.Ran,
            note = marketItems == 0
                ? "No market data for this supplier or category in the knowledge base: say so plainly, answer from the contract's own facts, and never estimate a market figure yourself."
                : "Present every market figure as a market estimate with its basis, never as the user's contract data.",
        }, research.Queries.Count);
    }
}

/// <summary>Savings: the levers and their amounts for one contract, or the portfolio-wide target.</summary>
internal sealed class GetSavingsTool(SavingsPackBuilder savings) : IAskTool
{
    public string Name => "get_savings";

    public string Description =>
        "Where money can be saved: for one supplier, the quantified levers (market discount, above-band repricing, multi-year term, uplift cap, notice " +
        "timing, payment terms, volume flexibility) with amounts; for the whole portfolio, the savings target verdict across contracts. Pass the user's " +
        "goal when stated ('10k', '15%', 'entro fine anno'). Use it for 'quanto posso risparmiare', 'where can we save', 'leve', 'target'.";

    public string ParametersSchemaJson => Schema(
        ("supplier", "string", "Supplier name, 'this' for the current contract, or null for the whole portfolio.", true),
        ("goal", "string", "The user's savings goal in their words (amount, percentage, window), or null.", true));

    public async Task<AskToolResult> ExecuteAsync(JsonElement arguments, AskToolContext context, CancellationToken cancellationToken)
    {
        var goalText = GetString(arguments, "goal");
        var goal = goalText is null ? null : SavingsGoalParser.Parse(goalText);
        var given = GetString(arguments, "supplier");
        if (given is null && context.ScopedContractItem is null)
        {
            var portfolioItems = await savings.BuildPortfolioSavingsTargetPackAsync(context.Portfolio, goal, cancellationToken).ConfigureAwait(false);
            return AskToolResult.FromPack(portfolioItems, new { scope = "portfolio", goal = goalText });
        }

        var (item, supplier, _) = context.ResolveContract(given);
        if (item is null)
        {
            return AskToolResult.Error($"No validated contract for '{given}'. Known suppliers: {string.Join(", ", context.KnownSupplierNames)}.");
        }

        var items = await savings.BuildSavingsLeverPackAsync(context.TenantId, item, goal, context.Actor, cancellationToken).ConfigureAwait(false);
        return AskToolResult.FromPack(items, new { scope = "contract", supplier, goal = goalText });
    }
}

/// <summary>The negotiation council: two analysts and a strategist over a contract's evidence,
/// returning ranked plays. The costly tool; the prompt says when it is worth it.</summary>
internal sealed class RunNegotiationCouncilTool(SavingsPackBuilder savings, MarketDataCheck dataCheck, NegotiationCouncil council) : IAskTool
{
    public string Name => "run_negotiation_council";

    public string Description =>
        "Convenes Raffa's negotiation council on one supplier's contract: a contract analyst and a market analyst read the evidence, then a strategist " +
        "returns ranked negotiation plays (lever, the ask, the fallback, timing) grounded in the contract and the market. Slow and costly: call it only " +
        "when the user asks for a negotiation strategy, a plan, levers to use, or what to ask the supplier -- after get_savings or get_market_benchmark " +
        "when their facts are needed too.";

    public string ParametersSchemaJson => Schema(
        ("supplier", "string", "Supplier name, or 'this' for the current contract.", false),
        ("goal", "string", "The user's savings goal in their words, or null.", true));

    public async Task<AskToolResult> ExecuteAsync(JsonElement arguments, AskToolContext context, CancellationToken cancellationToken)
    {
        var (item, supplier, _) = context.ResolveContract(GetString(arguments, "supplier"));
        if (item is null)
        {
            return AskToolResult.Error($"No validated contract for that supplier. Known suppliers: {string.Join(", ", context.KnownSupplierNames)}.");
        }

        var goalText = GetString(arguments, "goal");
        var goal = goalText is null ? null : SavingsGoalParser.Parse(goalText);
        var pack = (await savings.BuildSavingsLeverPackAsync(context.TenantId, item, goal, context.Actor, cancellationToken).ConfigureAwait(false)).ToList();
        var check = await dataCheck.RunAsync([item], [], cancellationToken).ConfigureAwait(false);
        pack.AddRange(check.Items.Where(i => pack.All(x => x.CitationKey != i.CitationKey)));

        var outcome = await council.RunAsync(context.Question, pack, goal, cancellationToken).ConfigureAwait(false);
        var items = pack.Concat(outcome.Items).ToList();
        return new AskToolResult(
            AskToolResult.Serialize(items, new { supplier, agentsRun = outcome.AgentsRun, failures = outcome.Failures.Count, plays = outcome.Items.Count }),
            items,
            Failure: outcome.Failures.Count > 0 ? string.Join("; ", outcome.Failures) : null);
    }
}

/// <summary>Which documents are still being processed or need review.</summary>
internal sealed class GetDocumentStatusTool : IAskTool
{
    public string Name => "get_document_status";

    public string Description =>
        "Which uploaded documents are not yet readable (still processing, needing review, failed) and why. Use it when the user asks why a contract " +
        "is missing, what is pending, or whether a document was validated.";

    public string ParametersSchemaJson => Schema(("detail", "string", "'pending' (default) or 'all'.", true));

    public Task<AskToolResult> ExecuteAsync(JsonElement arguments, AskToolContext context, CancellationToken cancellationToken) =>
        Task.FromResult(AskToolResult.FromPack(DocumentStatusPack.Build(context.Portfolio, context.SupplierNames)));
}

/// <summary>Builds a dashboard, a KPI set or a document in the action panel.</summary>
internal sealed class ComposeArtifactTool(ArtifactComposer composer) : IAskTool
{
    public string Name => "compose_artifact";

    public string Description =>
        "Builds a visual artifact in the side panel and opens it: 'kpi' (4-8 tiles), 'dashboard' (tiles + charts/tables) or 'document' (a written brief, " +
        "report or action plan). Call it when the user asks to create/build/prepare a dashboard, KPIs, a report, a brief, a summary document, a plan -- " +
        "also when they correct the subject of one just built ('it was Coupa, not Zscaler'). Set usesMarket when the content should include market " +
        "benchmarks, usesPortfolio when it looks across contracts.";

    public string ParametersSchemaJson => EnumSchema(
        "kind", "'kpi', 'dashboard' or 'document'.", ["kpi", "dashboard", "document"],
        ("supplier", "string", "Supplier the artifact is about, 'this' for the current contract, or null for a portfolio-wide artifact.", true),
        ("request", "string", "What to build, in the user's words, with the subject made explicit.", false),
        ("usesMarket", "boolean", "Include market benchmarks.", false),
        ("usesPortfolio", "boolean", "Look across every contract.", false));

    public async Task<AskToolResult> ExecuteAsync(JsonElement arguments, AskToolContext context, CancellationToken cancellationToken)
    {
        var kind = GetString(arguments, "kind") ?? "dashboard";
        var given = GetString(arguments, "supplier");
        var usesPortfolio = GetBool(arguments, "usesPortfolio");
        string? supplier = null;
        if (!usesPortfolio)
        {
            var (item, name, _) = context.ResolveContract(given);
            supplier = name;
            if (given is not null && item is null)
            {
                return AskToolResult.Error($"No validated contract for '{given}'. Known suppliers: {string.Join(", ", context.KnownSupplierNames)}.");
            }
        }

        var request = GetString(arguments, "request") ?? context.Question;
        var composed = await composer.ComposeAsync(
                context.TenantId,
                new ArtifactRequest(request, context.RecentTurns, context.FocusContractItem is { } f ? new EntityId(f.ContractId) : context.ScopeContractId, context.Language),
                new ArtifactScopeHint(supplier, usesPortfolio, GetBool(arguments, "usesMarket"), kind),
                cancellationToken)
            .ConfigureAwait(false);
        if (composed is null)
        {
            return AskToolResult.Error("The artifact composer could not produce a grounded draft; tell the user and offer the facts in the answer instead.");
        }

        var payload = ArtifactPayloads.From(composed, composed.Draft, context.Hints.NextArtifactVersion);
        return new AskToolResult(
            JsonSerializer.Serialize(new
            {
                items = Array.Empty<object>(),
                summary = new
                {
                    built = true, title = payload.Title, kind = payload.Kind, version = payload.Version, supplier = payload.FocusSupplier,
                    usedMarket = payload.UsedMarket, usedPortfolio = payload.UsedPortfolio, tiles = payload.Tiles.Count,
                    missingInputs = payload.MissingInputs,
                    note = "The artifact is open in the side panel: tell the user what you built (title, subject) in one or two sentences; do not repeat its content.",
                },
            }),
            [],
            Artifact: payload,
            UiActions: [UiActionPayload.OpenArtifact(payload.Id)]);
    }
}

/// <summary>Re-opens the artifact this conversation already built.</summary>
internal sealed class OpenArtifactTool : IAskTool
{
    public string Name => "open_artifact";

    public string Description => "Opens in the side panel the artifact (dashboard, KPI set, document) this conversation already built. Use it for 'aprilo', 'open it', 'show it again'.";

    public string ParametersSchemaJson => Schema(("reason", "string", "Why it is reopened, or null.", true));

    public Task<AskToolResult> ExecuteAsync(JsonElement arguments, AskToolContext context, CancellationToken cancellationToken)
    {
        if (context.CurrentArtifact is not { } current)
        {
            return Task.FromResult(AskToolResult.Error("This conversation has no artifact yet; offer to build one with compose_artifact."));
        }

        return Task.FromResult(new AskToolResult(
            JsonSerializer.Serialize(new { items = Array.Empty<object>(), summary = new { opened = true, current.Title, current.Kind, current.Version } }),
            [],
            UiActions: [UiActionPayload.OpenArtifact(current.Id)]));
    }
}

/// <summary>A button to the part of Raffa where the answer lives.</summary>
internal sealed class NavigateTool(CapabilityRouting routing) : IAskTool
{
    public string Name => "navigate";

    public string Description =>
        "Adds a button that takes the user to a screen of Raffa: 'documents' (upload, review), 'portfolio' (every contract), 'contract-360' (one contract's " +
        "detail), 'renewals' (deadlines and actions), 'savings' (opportunities), 'quote-check' (benchmark a new quote). Use it when the user asks to open " +
        "or go to a screen, or when the answer is best continued there.";

    public string ParametersSchemaJson => EnumSchema(
        "screen", "The screen.", [CapabilityCatalog.DocumentsKey, CapabilityCatalog.PortfolioKey, CapabilityCatalog.ContractDetailKey, CapabilityCatalog.RenewalsKey, CapabilityCatalog.SavingsKey, CapabilityCatalog.QuoteCheckKey],
        ("supplier", "string", "For contract-360: the supplier whose contract to open, or null.", true));

    public Task<AskToolResult> ExecuteAsync(JsonElement arguments, AskToolContext context, CancellationToken cancellationToken)
    {
        var screen = GetString(arguments, "screen") ?? CapabilityCatalog.DocumentsKey;
        EntityId? contractId = null;
        if (screen == CapabilityCatalog.ContractDetailKey)
        {
            var (item, _, _) = context.ResolveContract(GetString(arguments, "supplier"));
            if (item is null)
            {
                return Task.FromResult(AskToolResult.Error("contract-360 needs a supplier with a validated contract."));
            }

            contractId = new EntityId(item.ContractId);
        }

        var actions = routing.ResolveActions([CapabilityIntent.HowTo(screen)], context.RoutingContext(contractId));
        return Task.FromResult(new AskToolResult(
            JsonSerializer.Serialize(new { items = Array.Empty<object>(), summary = new { screen, buttons = actions.Select(a => a.Label) } }),
            [],
            Actions: actions));
    }
}

/// <summary>The public web, only through ADR-030's gates: the tool ends the turn with the model's
/// answer so far plus the consent question (or says which gate is closed); the authorised research
/// runs on the next turn, on the user's yes.</summary>
internal sealed class WebResearchTool(WebResearchFlow web, AbstainRecovery abstainRecovery) : IAskTool
{
    public string Name => "web_research";

    public string Description =>
        "Searches the public web on a procurement topic (market practice, supplier news, public price lists and benchmark ranges, negotiation tactics, " +
        "regulation) when the workspace's contracts and the market knowledge base do not hold what the question needs -- an empty benchmark, a supplier " +
        "with no contract on file, a public fact. Call it AFTER the other tools, as the last call of the turn, and put in answerSoFar everything you can " +
        "already say from the data you read: the user sees that answer and is asked for consent (only the query words go to the web, nothing from the " +
        "contracts); the search runs on their yes. Also call it when the user explicitly asks to search the web.";

    public string ParametersSchemaJson => Schema(
        ("query", "string", "What to search, precise and in the user's words: supplier names allowed, never an amount, a date or a clause from the contracts.", false),
        ("answerSoFar", "string", "Your answer from the data already read (markdown, with [n] markers and citationKeys you will return), ending with one sentence saying what the web could add. Empty only when nothing was read.", false));

    public async Task<AskToolResult> ExecuteAsync(JsonElement arguments, AskToolContext context, CancellationToken cancellationToken)
    {
        var query = GetString(arguments, "query") ?? context.Question;
        var answerSoFar = GetString(arguments, "answerSoFar");
        var reply = await web.BuildEntryReplyAsync(
                context.TenantId, query, context.Portfolio, context.RoutingContext(), context.Actor, abstainRecovery.Resolver, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(answerSoFar))
        {
            // The model's reading of the data comes first; the consent question (or the closed gate) follows it.
            reply = reply with { AnswerMarkdown = AnswerFormatter.Format(answerSoFar, context.Language) + "\n\n" + reply.AnswerMarkdown };
        }

        return new AskToolResult(
            JsonSerializer.Serialize(new { items = Array.Empty<object>(), summary = new { endsTurn = true, kind = reply.Kind.ToString(), consentAsked = reply.Kind == ReplyKind.Interview } }),
            [],
            EndTurnReply: reply);
    }
}
