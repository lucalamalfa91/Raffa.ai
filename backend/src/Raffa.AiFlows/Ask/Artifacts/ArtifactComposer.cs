using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Raffa.AiFlows.Negotiation.Agents;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiGateway;
using Raffa.AiGateway.Agents;
using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Application;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiFlows.Ask.Artifacts;

/// <summary>What the artifact composer agent returns: the content of a dashboard, a KPI set or a document.</summary>
public sealed record ArtifactDraft(
    string Title,
    string Subtitle,
    List<DraftTile> Tiles,
    List<DraftChart> Charts,
    List<DraftTable> Tables,
    string Markdown);

/// <summary>One KPI tile. <c>Tone</c> is "urgent", "good" or an empty string.</summary>
public sealed record DraftTile(string Label, string Value, string Hint, string Tone);

public sealed record DraftBar(string Label, double Value, string Display);

public sealed record DraftChart(string Title, List<DraftBar> Items);

public sealed record DraftTable(string Title, List<string> Columns, List<List<string>> Rows);

/// <summary>What the chat asked for: the typed request, the conversation so far and the contract it is opened on.</summary>
public sealed record ArtifactRequest(
    string Request,
    IReadOnlyList<(string Role, string Markdown)> RecentTurns,
    EntityId? ScopeContractId,
    string Language);

/// <summary>The composed content with the scope it was built on (for the audit trail and the panel's subtitle).</summary>
/// <summary><c>Draft</c> null = the model judged the message is not a request for an artifact (the chat answers it).
public sealed record ArtifactResult(ArtifactDraft? Draft, string Kind, string? FocusSupplier, bool UsedPortfolio, bool UsedMarket);

internal sealed record ArtifactScopePlan(bool WantsArtifact, string? Supplier, bool UsesPortfolio, bool UsesMarket, string Kind);

/// <summary>The composer agent's input: the user's request and the fact pack, all the figures it may use.</summary>
public sealed record ArtifactComposerInput(string Request, string Language, string Kind, string FactsJson);

/// <summary>
/// The artifact composer (agent <c>artifact-composer</c>): turns whatever the user asks for in the chat —
/// "this contract with KPIs", "a dashboard of every contract like this one", "how this contract sits against
/// the market" — into dashboard / KPI / document content. Two model steps, both over the conversation:
/// <list type="number">
/// <item><description>scope (<c>artifact-scope</c>): which contract the request is about (the one in focus in the
/// chat, or one it names), whether it needs the whole portfolio and whether it needs the market;</description></item>
/// <item><description>compose (<c>artifact-composer</c>): writes the content from a fact pack made of the focus
/// contract's own figures, the portfolio rows and, when asked for, the market notes the
/// <see cref="MarketResearcher"/> agent retrieved from the market RAG.</description></item>
/// </list>
/// The model chooses what to show and how; every figure comes from the fact pack. A verifier rejects a draft
/// that shows a number the pack does not contain (one retry, then the caller falls back to its deterministic build).
/// </summary>
public sealed class ArtifactComposer(
    IAiGateway aiGateway,
    PortfolioQueryService portfolioQueryService,
    ISupplierNameLookup supplierNameLookup,
    ITenantContext tenantContext,
    MarketResearcher marketResearcher,
    IClock clock,
    AgentRegistry? registry = null,
    ILogger<ArtifactComposer>? logger = null)
{
    public const string Flow = "artifact";
    public const string ScopeStage = "artifact-scope";
    public const string ComposerName = "artifact-composer";
    public const string ComposerVersion = "1";

    private const int MaxPortfolioRows = 80;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string ComposerPrompt =
        "You write the content of a procurement dashboard, a KPI set or a short document for a Raffa user, from the FACTS you are given.\n" +
        "Rules:\n" +
        "1. Use ONLY figures that appear in FACTS (copy them; abbreviate as 3.6M / 450k if you like, but never change a value, never compute a new one). If a figure is not there, say so in the hint or the markdown instead of inventing it.\n" +
        "2. 'focus' is the contract(s) the conversation is about: when it is present, the content is about it. 'portfolio' lists every contract of the workspace: use it when the request compares or looks across contracts (similar contracts, ranking, totals). 'market' holds notes retrieved from the market knowledge base: use it, with its provenance, when the request asks how the contract sits against the market; never present a market note as the user's own data.\n" +
        "3. Pick what fits the request: kind 'kpi' = 4 to 8 tiles; 'dashboard' = tiles plus at least one chart and/or table; 'document' = markdown (headings, lists, bold) with few or no tiles.\n" +
        "4. Write in the language given. Tile 'tone' is 'urgent', 'good' or an empty string. Chart items need a numeric value and a short display string. Unused lists are empty, unused strings are empty.\n" +
        "5. Never address the user, never ask questions: return the content only.";

    /// <summary>The composer's registry definition (kill switch <c>Agents:Agents:artifact-composer:Enabled</c>).</summary>
    public static AgentDefinition Definition() =>
        AgentDefinition.ForOutput<ArtifactDraft>(ComposerName, ComposerVersion, ComposerPrompt, TimeSpan.FromSeconds(60), AgentFailurePolicy.Fail);

    public async Task<ArtifactResult?> ComposeAsync(TenantId tenantId, ArtifactRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var scope = tenantContext.BeginScope(tenantId);

        var portfolio = await portfolioQueryService
            .GetPortfolioAsync(tenantId, PortfolioFilter.None, new PortfolioPageRequest(1, PortfolioPageRequest.MaxPageSize), cancellationToken)
            .ConfigureAwait(false);
        var supplierIds = portfolio.Items.Where(i => i.SupplierId is not null).Select(i => new EntityId(i.SupplierId!.Value)).Distinct().ToList();
        var names = supplierIds.Count > 0
            ? await supplierNameLookup.GetNamesAsync(tenantId, supplierIds, cancellationToken).ConfigureAwait(false)
            : new Dictionary<EntityId, string>();
        string NameOf(PortfolioListItem i) => i.SupplierId is { } id && names.TryGetValue(new EntityId(id), out var n) ? n : "?";

        var plan = await PlanScopeAsync(request, names.Values.Distinct().ToList(), cancellationToken).ConfigureAwait(false);
        if (plan is null)
        {
            return null;
        }

        if (!plan.WantsArtifact)
        {
            return new ArtifactResult(null, plan.Kind, null, false, false);
        }

        // The contract(s) in focus: the supplier the model resolved from the chat, else the contract the chat is opened on.
        var focusItems = plan.Supplier is { } supplier
            ? portfolio.Items.Where(i => string.Equals(NameOf(i), supplier, StringComparison.OrdinalIgnoreCase)).ToList()
            : request.ScopeContractId is { } scoped
                ? portfolio.Items.Where(i => i.ContractId == scoped.Value).ToList()
                : [];
        var focusSupplier = focusItems.Count > 0 ? NameOf(focusItems[0]) : plan.Supplier;

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        object? market = null;
        var usedMarket = false;
        if (plan.UsesMarket)
        {
            var outcome = await marketResearcher
                .QueryMarketRagAsync(request.Request, null, [], ["market position of this contract against similar contracts"], cancellationToken)
                .ConfigureAwait(false);
            market = outcome.Items.Select(i => new { i.Title, i.Subtitle, i.Snippet, i.Provenance }).ToList();
            usedMarket = outcome.Items.Count > 0;
        }

        var facts = new
        {
            today = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            focus = focusItems.Count == 0 ? null : new { supplier = focusSupplier, contracts = focusItems.Select(i => Row(i, NameOf(i), today)).ToList() },
            portfolio = plan.UsesPortfolio || focusItems.Count == 0 ? Summarize(portfolio.Items, NameOf, today) : null,
            market,
            marketRequestedButEmpty = plan.UsesMarket && !usedMarket,
        };
        var factsJson = JsonSerializer.Serialize(facts, Json);

        var step = new AgentStep<ArtifactComposerInput, ArtifactDraft>(Definition(), new GroundedFiguresVerifier());
        var runner = new StepRunner(aiGateway, registry: registry);
        var run = await runner
            .RunAsync(step, new ArtifactComposerInput(request.Request, request.Language, plan.Kind, factsJson), cancellationToken)
            .ConfigureAwait(false);
        if (!run.IsSuccess || run.Value is null)
        {
            logger?.LogInformation("Artifact composer did not deliver: {Reason}", run.Describe());
            return null;
        }

        return new ArtifactResult(run.Value, plan.Kind, focusSupplier, plan.UsesPortfolio, usedMarket);
    }

    private async Task<ArtifactScopePlan?> PlanScopeAsync(ArtifactRequest request, IReadOnlyCollection<string> suppliers, CancellationToken cancellationToken)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("A procurement user wrote a message in a chat with Raffa. Decide whether it asks for a dashboard, a KPI set, a chart/table view or a written brief/document to be BUILT (any wording, any language, with or without a verb: 'mostramelo con dei kpi', 'rappresentalo con dei grafici', 'show it as a dashboard'), and if so its scope. A question to be answered in words, a clarification or a follow-up on the text is NOT a request for an artifact. You never build or answer anything.");
        prompt.AppendLine("wantsArtifact: true only if the message asks for such content to be built.");
        prompt.AppendLine("supplier: the listed supplier whose contract the request is about, resolving 'this contract', 'questo contratto', 'it' and the like from the conversation (exactly as listed; null if no single contract is in focus or the request is portfolio-wide).");
        prompt.AppendLine("usesPortfolio: true if the request looks across several contracts (similar contracts, all contracts, rankings, totals) or no contract is in focus.");
        prompt.AppendLine("usesMarket: true only if the request asks how a contract sits against the market, benchmarks, or similar contracts on the market.");
        prompt.AppendLine("kind: 'kpi' (indicators), 'dashboard' (several views) or 'document' (a written brief); when unsure, 'dashboard'.");
        prompt.AppendLine("Suppliers: " + string.Join("; ", suppliers));
        prompt.AppendLine(request.ScopeContractId is null ? "The chat is not opened on a specific contract." : "The chat is opened on a specific contract (resolve 'this contract' to it when the conversation does not say otherwise).");
        prompt.AppendLine("Conversation (oldest first):");
        foreach (var (role, markdown) in request.RecentTurns.TakeLast(6))
        {
            prompt.AppendLine($"[{role}] {(markdown.Length > 700 ? markdown[..700] + "…" : markdown)}");
        }

        prompt.AppendLine("Request:");
        prompt.AppendLine(request.Request);

        var schema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new
            {
                wantsArtifact = new { type = "boolean" },
                supplier = new { type = new[] { "string", "null" } },
                usesPortfolio = new { type = "boolean" },
                usesMarket = new { type = "boolean" },
                kind = new { type = "string", @enum = new[] { "kpi", "dashboard", "document" } },
            },
            required = new[] { "wantsArtifact", "supplier", "usesPortfolio", "usesMarket", "kind" },
            additionalProperties = false,
        });

        try
        {
            var result = await aiGateway.ExtractAsync(new AiExtractionRequest(ScopeStage, prompt.ToString(), schema), cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return null;
            }

            var answer = JsonSerializer.Deserialize<ScopeAnswer>(result.Value.PayloadJson, Json);
            if (answer is null || answer.Kind is not ("kpi" or "dashboard" or "document"))
            {
                return null;
            }

            var supplier = answer.Supplier is { Length: > 0 } s
                ? suppliers.FirstOrDefault(x => string.Equals(x, s, StringComparison.OrdinalIgnoreCase))
                : null;
            return new ArtifactScopePlan(answer.WantsArtifact, supplier, answer.UsesPortfolio, answer.UsesMarket, answer.Kind);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogWarning(exception, "Artifact scope planning failed.");
            return null;
        }
    }

    private sealed record ScopeAnswer(bool WantsArtifact, string? Supplier, bool UsesPortfolio, bool UsesMarket, string? Kind);

    private static object Row(PortfolioListItem i, string supplier, DateOnly today) => new
    {
        contractId = i.ContractId,
        supplier,
        type = i.Type.ToString(),
        annualSpend = i.AnnualSpend,
        currency = i.Currency,
        start = Iso(i.StartDate),
        end = Iso(i.EndDate),
        renewal = Iso(i.RenewalDate),
        cancellationDeadline = Iso(i.CancellationDeadline),
        daysToEnd = i.EndDate is { } e ? e.DayNumber - today.DayNumber : (int?)null,
        daysToCancellationDeadline = i.CancellationDeadline is { } c ? c.DayNumber - today.DayNumber : (int?)null,
        noticePeriodDays = i.NoticePeriodDays,
        autoRenewal = i.AutoRenewal,
        status = i.Status,
        risk = i.Risk?.ToString(),
    };

    private static string? Iso(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The portfolio as rows plus the aggregates the model must not compute itself.</summary>
    private static object Summarize(IReadOnlyList<PortfolioListItem> items, Func<PortfolioListItem, string> nameOf, DateOnly today)
    {
        var byCurrency = items.Where(i => i.AnnualSpend is not null).GroupBy(i => i.Currency)
            .Select(g => new { currency = g.Key, totalAnnualSpend = g.Sum(i => i.AnnualSpend), contracts = g.Count() }).ToList();
        var bySupplier = items.Where(i => i.AnnualSpend is not null).GroupBy(nameOf)
            .Select(g => new { supplier = g.Key, annualSpend = g.Sum(i => i.AnnualSpend), contracts = g.Count() })
            .OrderByDescending(x => x.annualSpend).Take(15).ToList();
        int Within(int days) => items.Count(i => i.EndDate is { } e && e.DayNumber - today.DayNumber is >= 0 && e.DayNumber - today.DayNumber <= days);
        return new
        {
            contractCount = items.Count,
            spendByCurrency = byCurrency,
            topSuppliersBySpend = bySupplier,
            endingWithin90Days = Within(90),
            endingWithin180Days = Within(180),
            endingWithin365Days = Within(365),
            autoRenewalCount = items.Count(i => i.AutoRenewal),
            contracts = items.OrderByDescending(i => i.AnnualSpend ?? 0).Take(MaxPortfolioRows).Select(i => Row(i, nameOf(i), today)).ToList(),
            contractsListTruncated = items.Count > MaxPortfolioRows,
        };
    }
}

/// <summary>
/// Rejects a draft that shows a number the fact pack does not contain. Compared by significant digits so
/// "3.6M", "3,600,000" and 3600000 agree; one-digit numbers are not checked. The model may format and choose,
/// never compute: an aggregate it needs must already be in the pack.
/// </summary>
internal sealed partial class GroundedFiguresVerifier : IStepVerifier<ArtifactComposerInput, ArtifactDraft>
{
    public string Name => "grounded-figures";

    public StepVerdict Verify(ArtifactComposerInput input, ArtifactDraft output)
    {
        var known = Significant(input.FactsJson + " " + input.Request).ToHashSet(StringComparer.Ordinal);
        var shown = output.Tiles.SelectMany(t => new[] { t.Value })
            .Concat(output.Charts.SelectMany(c => c.Items.SelectMany(i => new[] { i.Display, i.Value.ToString(CultureInfo.InvariantCulture) })))
            .Concat(output.Tables.SelectMany(t => t.Rows.SelectMany(r => r)));
        var unknown = Significant(string.Join(' ', shown)).Where(s => s.Length > 1 && !known.Contains(s)).Distinct().Take(5).ToList();
        return unknown.Count == 0
            ? StepVerdict.Pass
            : StepVerdict.Reject("These figures are not in FACTS (use only figures from FACTS, never compute one): " + string.Join(", ", unknown) + ".");
    }

    private static IEnumerable<string> Significant(string text)
    {
        foreach (Match m in NumberPattern().Matches(text))
        {
            var digits = new string(m.Value.Where(char.IsDigit).ToArray()).TrimStart('0').TrimEnd('0');
            if (digits.Length > 0)
            {
                yield return digits;
            }
        }
    }

    [GeneratedRegex(@"\d+(?:[.,'’]\d+)*")]
    private static partial Regex NumberPattern();
}
