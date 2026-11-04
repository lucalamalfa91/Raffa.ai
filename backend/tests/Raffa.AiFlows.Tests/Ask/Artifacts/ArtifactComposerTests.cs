using Raffa.AiFlows.Ask.Artifacts;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.Ask.Artifacts;

/// <summary>
/// <see cref="ArtifactComposer.BuildFocusFact"/>: the focus contract's own fact row must carry the
/// Contract 360 figures (Overview/Commercials/Products/Clauses/Obligations), not just the portfolio's
/// coarse row — otherwise a chat request to build a dashboard "with this contract's data" has none of
/// the subscription/term/fee/clause figures the chat itself already quoted the user (the bug this
/// closes: the composer silently fell back to a worded answer because it could never ground them).
/// </summary>
public sealed class ArtifactComposerFocusFactTests
{
    private static readonly DateOnly Today = new(2026, 1, 1);

    private static PortfolioListItem PortfolioRow(decimal? annualSpend = 100_000m) => new(
        ContractId: Guid.NewGuid(), SupplierId: Guid.NewGuid(), Type: ContractDocumentType.OrderForm,
        AnnualSpend: annualSpend, Currency: "CHF", StartDate: new DateOnly(2025, 4, 16), EndDate: new DateOnly(2026, 4, 15),
        RenewalDate: new DateOnly(2026, 4, 15), CancellationDeadline: new DateOnly(2026, 1, 15), AutoRenewal: true,
        Status: "Active", Risk: RiskSeverity.High, FileName: "coupa.pdf", DocumentProcessingStatus: DocumentProcessingStatus.Completed,
        NoticePeriodDays: 90);

    private static Contract360Result Contract360(Guid contractId) => new(
        new EntityId(contractId),
        new Contract360Header(new EntityId(contractId), null, ContractDocumentType.OrderForm, "Active", 281_000m, 281_000m, null, null, null, null, true, RiskSeverity.High),
        new Contract360Overview("CHF", new DateOnly(2025, 4, 16), 12, "Net 30", "Swiss law", null, 1, DateTimeOffset.UtcNow),
        new Contract360Commercials(281_000m, 281_000m, "CHF", "Net 30", true, 12, 2, 281_000m, 281_000m),
        [new Contract360ProductLineItem(new EntityId(Guid.NewGuid()), null, "SKU-1", "Procure-to-Pay licence", 1m, "seat", 250_000m, null, null, "annual", 250_000m, 250_000m, null, null, null, null)],
        [new Contract360Clause(new EntityId(Guid.NewGuid()), "Termination", "Notice must be given at least 90 days before the Initial Term End.", null, RiskSeverity.Medium, new EntityId(Guid.NewGuid()), "§4.2", 3, 0.9)],
        [new Contract360Obligation(new EntityId(Guid.NewGuid()), "Customer", "Renewal notice", "Send non-renewal notice", new DateOnly(2026, 1, 15), null, "High", "Open", null, null, null, null)],
        [],
        [],
        [],
        new Contract360Renewal(new DateOnly(2026, 4, 15), new DateOnly(2026, 4, 15), new DateOnly(2026, 1, 15), true, 12),
        []);

    [Fact]
    public void Contract_360_figures_enrich_the_portfolio_rows_coarse_fields()
    {
        var item = PortfolioRow(annualSpend: 100_000m);
        var contract360 = Contract360(item.ContractId);

        var fact = ArtifactComposer.BuildFocusFact(item, "Coupa", Today, contract360);

        Assert.Equal(281_000m, fact.AnnualSpend);
        Assert.Equal(281_000m, fact.TotalContractValue);
        Assert.Equal("2025-04-16", fact.EffectiveDate);
        Assert.Equal(12, fact.RenewalTermMonths);
        Assert.Equal("Net 30", fact.PaymentTerms);
        Assert.Equal("Swiss law", fact.GoverningLaw);
        Assert.Single(fact.Clauses);
        Assert.Contains("90 days", fact.Clauses[0].Text);
        Assert.Single(fact.LineItems);
        Assert.Equal(250_000m, fact.LineItems[0].AnnualCost);
        Assert.Single(fact.Obligations);
    }

    [Fact]
    public void No_contract_360_falls_back_to_the_portfolio_rows_own_coarse_fields_with_no_clauses()
    {
        var item = PortfolioRow(annualSpend: 100_000m);

        var fact = ArtifactComposer.BuildFocusFact(item, "Coupa", Today, contract360: null);

        Assert.Equal(100_000m, fact.AnnualSpend);
        Assert.Null(fact.TotalContractValue);
        Assert.Null(fact.EffectiveDate);
        Assert.Empty(fact.Clauses);
        Assert.Empty(fact.LineItems);
    }
}

/// <summary>
/// <see cref="ArtifactComposer.BuildDeterministicDraft"/>: the no-model-call path
/// <see cref="ArtifactComposer.ComposeAsync"/> takes when the composer agent cannot deliver. A request
/// the user explicitly made must still open a real, grounded panel — never silently fall back to a
/// worded-only chat answer.
/// </summary>
public sealed class ArtifactComposerDeterministicDraftTests
{
    private static FocusContractFact Focus(
        decimal? annualSpend = 281_000m, int? cancellationDays = 14, string? risk = "High",
        IReadOnlyList<FocusClauseFact>? clauses = null, IReadOnlyList<FocusLineItemFact>? lineItems = null) => new(
        ContractId: Guid.NewGuid(), Supplier: "Coupa", Type: "OrderForm", Status: "Active",
        AnnualSpend: annualSpend, TotalContractValue: annualSpend, Currency: "CHF",
        EffectiveDate: "2025-04-16", Start: "2025-04-16", End: "2026-04-15", Renewal: "2026-04-15",
        CancellationDeadline: "2026-01-15", DaysToEnd: 104, DaysToCancellationDeadline: cancellationDays,
        NoticePeriodDays: 90, AutoRenewal: true, RenewalTermMonths: 12, PaymentTerms: "Net 30", GoverningLaw: "Swiss law",
        Risk: risk, LineItemCount: lineItems?.Count ?? 0, LineItemAnnualCostTotal: annualSpend, LineItemTotalCostTotal: annualSpend,
        LineItems: lineItems ?? [], Clauses: clauses ?? [], Obligations: []);

    [Fact]
    public void Never_returns_null_and_grounds_every_tile_in_the_focus_contracts_own_figures()
    {
        var draft = ArtifactComposer.BuildDeterministicDraft("dashboard", "en", "Coupa", [Focus()], null);

        Assert.NotNull(draft);
        Assert.Contains("Coupa", draft.Title);
        Assert.Contains(draft.Tiles, t => t.Label == "Annual spend" && t.Value == "CHF 281,000");
        Assert.Contains(draft.Tiles, t => t.Label == "Cancellation deadline" && t.Value == "2026-01-15" && t.Tone == "urgent");
    }

    [Fact]
    public void Dashboard_kind_adds_a_chart_from_line_items_and_a_table_from_clauses()
    {
        var clauses = new List<FocusClauseFact> { new("Termination", "Notice must be given at least 90 days before the Initial Term End.", 3) };
        var lineItems = new List<FocusLineItemFact> { new("Procure-to-Pay licence", 1m, "seat", 250_000m, "annual", 250_000m) };

        var draft = ArtifactComposer.BuildDeterministicDraft("dashboard", "en", "Coupa", [Focus(clauses: clauses, lineItems: lineItems)], null);

        var chart = Assert.Single(draft.Charts);
        Assert.Contains(chart.Items, i => i.Display == "CHF 250,000");
        var table = Assert.Single(draft.Tables);
        Assert.Contains(table.Rows, r => r.Contains("Termination"));
    }

    [Fact]
    public void Kpi_kind_never_adds_charts_or_tables_even_with_clauses_and_line_items()
    {
        var clauses = new List<FocusClauseFact> { new("Termination", "Notice must be given at least 90 days before the Initial Term End.", 3) };
        var lineItems = new List<FocusLineItemFact> { new("Procure-to-Pay licence", 1m, "seat", 250_000m, "annual", 250_000m) };

        var draft = ArtifactComposer.BuildDeterministicDraft("kpi", "en", "Coupa", [Focus(clauses: clauses, lineItems: lineItems)], null);

        Assert.Empty(draft.Charts);
        Assert.Empty(draft.Tables);
        Assert.NotEmpty(draft.Tiles);
    }

    [Fact]
    public void Document_kind_quotes_the_clauses_in_the_markdown()
    {
        var clauses = new List<FocusClauseFact> { new("Termination", "Notice must be given at least 90 days before the Initial Term End.", 3) };

        var draft = ArtifactComposer.BuildDeterministicDraft("document", "en", "Coupa", [Focus(clauses: clauses)], null);

        Assert.Contains("90 days before the Initial Term End", draft.Markdown);
        Assert.Empty(draft.Tiles);
    }

    [Fact]
    public void Italian_language_uses_italian_labels()
    {
        var draft = ArtifactComposer.BuildDeterministicDraft("kpi", "it", "Coupa", [Focus()], null);

        Assert.Contains(draft.Tiles, t => t.Label == "Spesa annua");
        Assert.DoesNotContain(draft.Tiles, t => t.Label == "Annual spend");
    }

    [Fact]
    public void No_focus_contract_falls_back_to_the_portfolio_summary()
    {
        var summary = new PortfolioSummaryFact(
            ContractCount: 12,
            SpendByCurrency: [new CurrencyTotalFact("CHF", 1_200_000m, 12)],
            TopSuppliersBySpend: [new SupplierTotalFact("Coupa", 281_000m, 1)],
            EndingWithin90Days: 2, EndingWithin180Days: 4, EndingWithin365Days: 6, AutoRenewalCount: 5,
            Contracts: [], ContractsListTruncated: false);

        var draft = ArtifactComposer.BuildDeterministicDraft("dashboard", "en", null, [], summary);

        Assert.Contains(draft.Tiles, t => t.Label == "Validated contracts" && t.Value == "12");
        Assert.Contains(draft.Tiles, t => t.Label == "Ending within 90 days" && t.Value == "2" && t.Tone == "urgent");
        Assert.Single(draft.Charts);
    }
}
