using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Raffa.AiFlows.Ask.Packs;
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
public sealed record ArtifactResult(
    ArtifactDraft Draft,
    string Kind,
    string? FocusSupplier,
    EntityId? FocusContractId,
    bool UsedPortfolio,
    bool UsedMarket);

/// <summary>The scope the turn-understanding step resolved for an artifact request.</summary>
public sealed record ArtifactScopeHint(string? Supplier, bool UsesPortfolio, bool UsesMarket, string Kind);

internal sealed record ArtifactScopePlan(string? Supplier, bool UsesPortfolio, bool UsesMarket, string Kind);

/// <summary>The composer agent's input: the user's request and the fact pack, all the figures it may use.</summary>
public sealed record ArtifactComposerInput(string Request, string Language, string Kind, string FactsJson);

/// <summary>One priced line of the focus contract's Products tab, for its own fact row.</summary>
internal sealed record FocusLineItemFact(string Description, decimal? Quantity, string? Unit, decimal? UnitPrice, string? BillingPeriod, decimal? AnnualCost);

/// <summary>One clause's raw text (capped, see <see cref="ArtifactComposer.MaxFocusClauses"/>), for the focus contract's own fact row.</summary>
internal sealed record FocusClauseFact(string Type, string Text, int? Page);

/// <summary>One obligation, for the focus contract's own fact row.</summary>
internal sealed record FocusObligationFact(string Type, string Description, string? DueDate, string? Criticality);

/// <summary>
/// The contract(s) in focus own figures: the same header/renewal fields the portfolio row already
/// carries, plus the Contract 360 fields (Overview/Commercials/Products/Clauses/Obligations) the
/// chat's own StructuredFact and Clause answers already draw on for a named contract. Without these,
/// the composer only ever saw the portfolio's coarse row (annual spend, dates, notice period, auto
/// renewal, status, risk) and could not ground a dashboard in the same subscription/term/fee/clause
/// figures the chat had just quoted the user two turns earlier — the gap this record closes.
/// </summary>
internal sealed record FocusContractFact(
    Guid ContractId, string Supplier, string Type, string Status, decimal? AnnualSpend, decimal? TotalContractValue,
    string Currency, string? EffectiveDate, string? Start, string? End, string? Renewal, string? CancellationDeadline,
    int? DaysToEnd, int? DaysToCancellationDeadline, int? NoticePeriodDays, bool AutoRenewal, int? RenewalTermMonths,
    string? PaymentTerms, string? GoverningLaw, string? Risk,
    int LineItemCount, decimal? LineItemAnnualCostTotal, decimal? LineItemTotalCostTotal,
    IReadOnlyList<FocusLineItemFact> LineItems, IReadOnlyList<FocusClauseFact> Clauses, IReadOnlyList<FocusObligationFact> Obligations);

/// <summary>One currency's total of <see cref="PortfolioSummaryFact.SpendByCurrency"/>.</summary>
internal sealed record CurrencyTotalFact(string Currency, decimal? TotalAnnualSpend, int Contracts);

/// <summary>One supplier's total of <see cref="PortfolioSummaryFact.TopSuppliersBySpend"/>.</summary>
internal sealed record SupplierTotalFact(string Supplier, decimal? AnnualSpend, int Contracts);

/// <summary>The portfolio-wide aggregates the model must not compute itself, plus the rows it may cite.</summary>
internal sealed record PortfolioSummaryFact(
    int ContractCount, IReadOnlyList<CurrencyTotalFact> SpendByCurrency, IReadOnlyList<SupplierTotalFact> TopSuppliersBySpend,
    int EndingWithin90Days, int EndingWithin180Days, int EndingWithin365Days, int AutoRenewalCount,
    IReadOnlyList<object> Contracts, bool ContractsListTruncated);

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
/// that shows a number the pack does not contain (one retry, then <see cref="ComposeAsync"/> itself falls back
/// to a deterministic draft built from the same fact pack, with no model call — see
/// <see cref="BuildDeterministicDraft"/>). A request the user explicitly made (<c>wantsArtifact</c>, resolved by
/// <see cref="TurnUnderstanding"/>) therefore never silently loses its panel to a worded-only answer: the agent
/// can be disabled, the gateway can be down, the verifier can reject both attempts — the panel still opens.
/// </summary>
public sealed class ArtifactComposer(
    IAiGateway aiGateway,
    PortfolioQueryService portfolioQueryService,
    ISupplierNameLookup supplierNameLookup,
    ContractContextReader contractContextReader,
    ITenantContext tenantContext,
    MarketResearcher marketResearcher,
    IClock clock,
    AgentRegistry? registry = null,
    ILogger<ArtifactComposer>? logger = null)
{
    public const string Flow = "artifact";
    public const string ComposerName = "artifact-composer";
    public const string ComposerVersion = "1";

    private const int MaxPortfolioRows = 80;
    private const int MaxFocusClauses = 30;
    private const int MaxFocusLineItems = 20;
    private const int MaxFocusObligations = 20;
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

    /// <param name="hint">What the turn-understanding step decided in its one model call: kind, portfolio, market and the supplier in focus.</param>
    public async Task<ArtifactResult?> ComposeAsync(
        TenantId tenantId, ArtifactRequest request, ArtifactScopeHint hint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hint);
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

        var plan = new ArtifactScopePlan(
            hint.Supplier is { } given ? names.Values.FirstOrDefault(n => string.Equals(n, given, StringComparison.OrdinalIgnoreCase)) : null,
            hint.UsesPortfolio, hint.UsesMarket, hint.Kind);

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

        // The focus contract's own Contract 360 view -- the same source StructuredFactPackBuilder/
        // ClausePackBuilder already read for the named-contract text answer (Overview/Commercials/
        // Products/Clauses/Obligations) -- not just the portfolio's coarse row, so a request about
        // "this contract's data" can be grounded in the exact figures the chat already quoted.
        var focusFacts = new List<FocusContractFact>(focusItems.Count);
        foreach (var item in focusItems)
        {
            var contract360 = await contractContextReader.GetContract360Async(item.ContractId, cancellationToken).ConfigureAwait(false);
            focusFacts.Add(BuildFocusFact(item, NameOf(item), today, contract360));
        }

        var portfolioSummary = plan.UsesPortfolio || focusFacts.Count == 0 ? Summarize(portfolio.Items, NameOf, today) : null;
        var facts = new
        {
            today = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            focus = focusFacts.Count == 0 ? null : new { supplier = focusSupplier, contracts = focusFacts },
            portfolio = portfolioSummary,
            market,
            marketRequestedButEmpty = plan.UsesMarket && !usedMarket,
        };
        var factsJson = JsonSerializer.Serialize(facts, Json);

        ArtifactDraft? draft = null;
        try
        {
            var step = new AgentStep<ArtifactComposerInput, ArtifactDraft>(Definition(), new GroundedFiguresVerifier());
            var runner = new StepRunner(aiGateway, registry: registry);
            var run = await runner
                .RunAsync(step, new ArtifactComposerInput(request.Request, request.Language, plan.Kind, factsJson), cancellationToken)
                .ConfigureAwait(false);
            if (run.IsSuccess && run.Value is not null)
            {
                draft = run.Value;
            }
            else
            {
                logger?.LogInformation("Artifact composer did not deliver: {Reason}; falling back to a deterministic draft.", run.Describe());
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogWarning(exception, "Artifact composer call failed; falling back to a deterministic draft.");
        }

        // A request the user explicitly asked for never comes back empty-handed: when the agent
        // cannot deliver, the same fact pack above still builds a real, grounded draft with no
        // model call at all (see BuildDeterministicDraft's own doc comment).
        draft ??= BuildDeterministicDraft(plan.Kind, request.Language, focusSupplier, focusFacts, portfolioSummary);

        return new ArtifactResult(
            draft,
            plan.Kind,
            focusSupplier,
            focusItems.Count == 1 ? new EntityId(focusItems[0].ContractId) : null,
            plan.UsesPortfolio,
            usedMarket);
    }

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
    private static PortfolioSummaryFact Summarize(IReadOnlyList<PortfolioListItem> items, Func<PortfolioListItem, string> nameOf, DateOnly today)
    {
        var byCurrency = items.Where(i => i.AnnualSpend is not null).GroupBy(i => i.Currency)
            .Select(g => new CurrencyTotalFact(g.Key, g.Sum(i => i.AnnualSpend), g.Count())).ToList();
        var bySupplier = items.Where(i => i.AnnualSpend is not null).GroupBy(nameOf)
            .Select(g => new SupplierTotalFact(g.Key, g.Sum(i => i.AnnualSpend), g.Count()))
            .OrderByDescending(x => x.AnnualSpend).Take(15).ToList();
        int Within(int days) => items.Count(i => i.EndDate is { } e && e.DayNumber - today.DayNumber is >= 0 && e.DayNumber - today.DayNumber <= days);
        return new PortfolioSummaryFact(
            ContractCount: items.Count,
            SpendByCurrency: byCurrency,
            TopSuppliersBySpend: bySupplier,
            EndingWithin90Days: Within(90),
            EndingWithin180Days: Within(180),
            EndingWithin365Days: Within(365),
            AutoRenewalCount: items.Count(i => i.AutoRenewal),
            Contracts: items.OrderByDescending(i => i.AnnualSpend ?? 0).Take(MaxPortfolioRows).Select(i => Row(i, nameOf(i), today)).ToList(),
            ContractsListTruncated: items.Count > MaxPortfolioRows);
    }

    /// <summary>
    /// The focus contract's own figures, read from its Contract 360 view (<see cref="FocusContractFact"/>'s
    /// own doc comment) -- <paramref name="contract360"/> is null only when the contract no longer resolves
    /// (deleted since the conversation was opened), in which case this still returns the portfolio row's own
    /// coarser fields rather than nothing at all.
    /// </summary>
    internal static FocusContractFact BuildFocusFact(PortfolioListItem item, string supplier, DateOnly today, Contract360Result? contract360)
    {
        var overview = contract360?.Overview;
        var commercials = contract360?.Commercials;
        var renewal = contract360?.Renewal;
        var cancellationDeadline = renewal?.CancellationDeadline ?? item.CancellationDeadline;
        return new FocusContractFact(
            ContractId: item.ContractId,
            Supplier: supplier,
            Type: item.Type.ToString(),
            Status: item.Status,
            AnnualSpend: commercials?.AnnualSpend ?? item.AnnualSpend,
            TotalContractValue: commercials?.TotalContractValue,
            Currency: commercials?.Currency ?? item.Currency,
            EffectiveDate: Iso(overview?.EffectiveDate),
            Start: Iso(item.StartDate),
            End: Iso(item.EndDate),
            Renewal: Iso(item.RenewalDate),
            CancellationDeadline: Iso(cancellationDeadline),
            DaysToEnd: item.EndDate is { } e ? e.DayNumber - today.DayNumber : null,
            DaysToCancellationDeadline: cancellationDeadline is { } c ? c.DayNumber - today.DayNumber : null,
            NoticePeriodDays: item.NoticePeriodDays,
            AutoRenewal: commercials?.AutoRenewal ?? item.AutoRenewal,
            RenewalTermMonths: commercials?.RenewalTermMonths ?? overview?.RenewalTermMonths ?? renewal?.RenewalTermMonths,
            PaymentTerms: commercials?.PaymentTerms ?? overview?.PaymentTerms,
            GoverningLaw: overview?.GoverningLaw,
            Risk: item.Risk?.ToString(),
            LineItemCount: commercials?.LineItemCount ?? 0,
            LineItemAnnualCostTotal: commercials?.LineItemAnnualCostTotal,
            LineItemTotalCostTotal: commercials?.LineItemTotalCostTotal,
            LineItems: contract360?.Products.Take(MaxFocusLineItems)
                .Select(p => new FocusLineItemFact(p.Description, p.Quantity, p.Unit, p.UnitPrice, p.BillingPeriod, p.AnnualCost))
                .ToList() ?? [],
            Clauses: contract360?.Clauses.Where(c => !string.IsNullOrWhiteSpace(c.RawText)).Take(MaxFocusClauses)
                .Select(c => new FocusClauseFact(c.ClauseType, c.RawText, c.SourcePage))
                .ToList() ?? [],
            Obligations: contract360?.Obligations.Take(MaxFocusObligations)
                .Select(o => new FocusObligationFact(o.ObligationType, o.Description, Iso(o.DueDate), o.Criticality))
                .ToList() ?? []);
    }

    private static string KindLabel(string kind, bool italian) => (kind, italian) switch
    {
        ("kpi", true) => "KPI",
        ("kpi", false) => "KPIs",
        ("document", true) => "Documento",
        ("document", false) => "Document",
        (_, true) => "Dashboard",
        (_, false) => "Dashboard",
    };

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd() + "…";

    /// <summary>
    /// A real, grounded draft built with no model call, straight from the same fact pack
    /// <see cref="ComposeAsync"/> gave the composer agent -- the path taken when that agent cannot
    /// deliver (kill switch, gateway failure, or the verifier rejects both attempts), so a request the
    /// user explicitly made never falls silently back to a worded-only answer with no panel at all.
    /// Every figure here is copied straight off <see cref="FocusContractFact"/>/<see cref="PortfolioSummaryFact"/>,
    /// never computed, so it needs no grounding verifier of its own. Labels are IT/EN only (<paramref name="language"/>
    /// not "it" falls back to English), matching this product's own two supported languages.
    /// </summary>
    internal static ArtifactDraft BuildDeterministicDraft(
        string kind, string language, string? focusSupplier, IReadOnlyList<FocusContractFact> focus, PortfolioSummaryFact? portfolioSummary)
    {
        var it = string.Equals(language, "it", StringComparison.OrdinalIgnoreCase);
        string L(string italian, string english) => it ? italian : english;
        var numberCulture = it ? CultureInfo.GetCultureInfo("it-IT") : CultureInfo.GetCultureInfo("en-US");
        string Amount(decimal value) => value.ToString("N0", numberCulture);

        var tiles = new List<DraftTile>();
        var charts = new List<DraftChart>();
        var tables = new List<DraftTable>();
        var lines = new List<string>();
        string title;
        string subtitle;

        if (focus.Count > 0)
        {
            var f = focus[0];
            title = $"{f.Supplier} · {KindLabel(kind, it)}";
            subtitle = L("Dai dati validati del contratto", "From the contract's own validated data");

            if (f.AnnualSpend is { } spend)
            {
                tiles.Add(new DraftTile(L("Spesa annua", "Annual spend"), $"{f.Currency} {Amount(spend)}", "", ""));
            }

            if (f.TotalContractValue is { } tcv)
            {
                tiles.Add(new DraftTile(L("Valore totale (TCV)", "Total contract value"), $"{f.Currency} {Amount(tcv)}", "", ""));
            }

            if (f.Start is not null || f.End is not null)
            {
                var hint = f.EffectiveDate is { } eff ? L($"decorrenza {eff}", $"effective {eff}") : "";
                tiles.Add(new DraftTile(L("Durata", "Term"), $"{f.Start ?? "—"} → {f.End ?? "—"}", hint, ""));
            }

            if (f.RenewalTermMonths is { } months)
            {
                var value = f.AutoRenewal ? L($"sì, ogni {months} mesi", $"yes, every {months} months") : L("no", "no");
                tiles.Add(new DraftTile(L("Rinnovo automatico", "Auto-renewal"), value, "", ""));
            }

            if (f.CancellationDeadline is { } deadline)
            {
                var hint = f.DaysToCancellationDeadline is { } d ? L($"{d} giorni", $"{d} days") : "";
                var tone = f.DaysToCancellationDeadline is >= 0 and <= 90 ? "urgent" : "";
                tiles.Add(new DraftTile(L("Scadenza disdetta", "Cancellation deadline"), deadline, hint, tone));
            }

            if (f.NoticePeriodDays is { } notice)
            {
                tiles.Add(new DraftTile(L("Preavviso", "Notice period"), L($"{notice} giorni", $"{notice} days"), "", ""));
            }

            if (f.PaymentTerms is { } paymentTerms && !string.IsNullOrWhiteSpace(paymentTerms))
            {
                tiles.Add(new DraftTile(L("Termini di pagamento", "Payment terms"), paymentTerms, "", ""));
            }

            if (f.GoverningLaw is { } governingLaw && !string.IsNullOrWhiteSpace(governingLaw))
            {
                tiles.Add(new DraftTile(L("Legge applicabile", "Governing law"), governingLaw, "", ""));
            }

            if (f.Risk is { } risk && !string.IsNullOrWhiteSpace(risk))
            {
                tiles.Add(new DraftTile(L("Rischio", "Risk"), risk, "", risk is "High" or "Critical" ? "urgent" : ""));
            }

            if (kind == "dashboard")
            {
                var priced = f.LineItems.Where(x => x.AnnualCost is not null).ToList();
                if (priced.Count > 0)
                {
                    charts.Add(new DraftChart(
                        L("Costo annuo per voce", "Annual cost by line"),
                        priced.Select(x => new DraftBar(Truncate(x.Description, 40), (double)x.AnnualCost!.Value, $"{f.Currency} {Amount(x.AnnualCost.Value)}")).ToList()));
                }

                if (f.Clauses.Count > 0)
                {
                    tables.Add(new DraftTable(
                        L("Clausole estratte", "Extracted clauses"),
                        [L("Tipo", "Type"), L("Testo", "Text"), L("Pagina", "Page")],
                        f.Clauses.Select(c => new List<string> { c.Type, Truncate(c.Text, 160), c.Page?.ToString(CultureInfo.InvariantCulture) ?? "—" }).ToList()));
                }
            }

            if (kind == "document")
            {
                lines.Add($"**{L("Dati del contratto", "Contract data")}**");
                lines.Add($"{f.Supplier} · {f.Type} · {f.Status}");
                if (f.AnnualSpend is { } s) lines.Add(L($"Spesa annua: {f.Currency} {Amount(s)}", $"Annual spend: {f.Currency} {Amount(s)}"));
                if (f.Start is not null || f.End is not null) lines.Add(L($"Durata: {f.Start ?? "—"} → {f.End ?? "—"}", $"Term: {f.Start ?? "—"} → {f.End ?? "—"}"));
                if (f.CancellationDeadline is { } cd) lines.Add(L($"Scadenza disdetta: {cd}", $"Cancellation deadline: {cd}"));
                if (f.Clauses.Count > 0)
                {
                    lines.Add("");
                    lines.Add($"**{L("Clausole", "Clauses")}**");
                    foreach (var c in f.Clauses.Take(10))
                    {
                        lines.Add($"- **{c.Type}**: {Truncate(c.Text, 200)}");
                    }
                }
            }
        }
        else
        {
            title = focusSupplier is not null ? $"{focusSupplier} · {KindLabel(kind, it)}" : $"{L("Portafoglio contratti", "Contracts portfolio")} · {KindLabel(kind, it)}";
            subtitle = L("Dai contratti validati", "From the validated contracts");

            if (portfolioSummary is { } p)
            {
                tiles.Add(new DraftTile(L("Contratti validati", "Validated contracts"), p.ContractCount.ToString(CultureInfo.InvariantCulture), "", ""));
                foreach (var c in p.SpendByCurrency)
                {
                    if (c.TotalAnnualSpend is { } total)
                    {
                        tiles.Add(new DraftTile(L("Spesa annua", "Annual spend"), $"{c.Currency} {total:N0}", "", ""));
                    }
                }

                tiles.Add(new DraftTile(
                    L("Scadenza entro 90gg", "Ending within 90 days"), p.EndingWithin90Days.ToString(CultureInfo.InvariantCulture), "",
                    p.EndingWithin90Days > 0 ? "urgent" : "good"));
                tiles.Add(new DraftTile(L("Rinnovo automatico", "Auto-renewal"), p.AutoRenewalCount.ToString(CultureInfo.InvariantCulture), "", ""));

                if (kind == "dashboard" && p.TopSuppliersBySpend.Count > 0)
                {
                    var priced = p.TopSuppliersBySpend.Where(x => x.AnnualSpend is not null).ToList();
                    if (priced.Count > 0)
                    {
                        charts.Add(new DraftChart(
                            L("Spesa annua per fornitore", "Annual spend by supplier"),
                            priced.Select(x => new DraftBar(x.Supplier, (double)x.AnnualSpend!.Value, $"{x.AnnualSpend:N0}")).ToList()));
                    }
                }
            }
        }

        // Rule 3 (same as the model prompt above, and the frontend's own client-side buildArtifact):
        // kind "document" is markdown with few or no tiles, never the full tile set.
        return new ArtifactDraft(title, subtitle, kind == "document" ? [] : tiles, charts, tables, string.Join('\n', lines));
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

/// <summary>Maps the composer's draft onto the reply payload the conversation stores and the client shows.</summary>
public static class ArtifactPayloads
{
    public static Raffa.Chat.Application.Reply.ArtifactPayload From(ArtifactResult result, ArtifactDraft draft, int version = 1) => new(
        Guid.NewGuid().ToString("N"),
        result.Kind,
        draft.Title,
        draft.Subtitle,
        result.FocusSupplier,
        result.UsedPortfolio,
        result.UsedMarket,
        draft.Tiles.Select(t => new Raffa.Chat.Application.Reply.ArtifactTilePayload(t.Label, t.Value, t.Hint, t.Tone)).ToList(),
        draft.Charts.Select(c => new Raffa.Chat.Application.Reply.ArtifactChartPayload(
            c.Title, c.Items.Select(i => new Raffa.Chat.Application.Reply.ArtifactBarPayload(i.Label, i.Value, i.Display)).ToList())).ToList(),
        draft.Tables.Select(t => new Raffa.Chat.Application.Reply.ArtifactTablePayload(
            t.Title, t.Columns, t.Rows.Select(r => (IReadOnlyList<string>)r).ToList())).ToList(),
        draft.Markdown,
        Version: Math.Max(1, version),
        Status: "draft",
        ContractId: result.FocusContractId?.Value.ToString(),
        MissingInputs: result.Kind == "kpi" ? ["renewal_quote", "actual_usage", "market_benchmark"] : []);
}
