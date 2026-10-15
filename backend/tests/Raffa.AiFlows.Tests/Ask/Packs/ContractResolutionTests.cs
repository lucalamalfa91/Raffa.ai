using Raffa.AiFlows.Ask.Packs;
using Raffa.AiFlows.Shared.Pack;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.Ask.Packs;

/// <summary>
/// Pins the pure contract-resolution rules <see cref="ContractResolution"/> moved out of
/// <c>AskCopilotService</c> (F1 Ask split, PR-1): the scoped id wins, else the soonest
/// cancellation deadline / renewal, and a multi-contract pick is told to the user.
/// </summary>
public sealed class ContractResolutionTests
{
    private static readonly Guid SupplierGuid = Guid.NewGuid();
    private static readonly Dictionary<EntityId, string> Names = new() { [new EntityId(SupplierGuid)] = "Salesforce" };

    private static PortfolioListItem Item(
        Guid? id = null, DateOnly? renewal = null, DateOnly? deadline = null, DateOnly? end = null, Guid? supplier = null) =>
        new(id ?? Guid.NewGuid(), supplier ?? SupplierGuid, ContractDocumentType.Msa, 1000m, "EUR", null, end,
            renewal, deadline, false, "completed", null, null, null);

    private static PortfolioPage Page(params PortfolioListItem[] items) => new(items, 1, 25, items.Length);

    [Fact]
    public void The_scoped_contract_wins_without_a_disambiguation_note()
    {
        var scoped = Item();
        var page = Page(Item(deadline: new DateOnly(2026, 1, 1)), scoped);

        var (item, note, matches) = ContractResolution.ResolveNamedContractItem(scoped, "Salesforce", page, Names);

        Assert.Same(scoped, item);
        Assert.Null(note);
        Assert.Empty(matches);
    }

    [Fact]
    public void No_named_supplier_and_no_scope_resolves_nothing()
    {
        var (item, note, matches) = ContractResolution.ResolveNamedContractItem(null, null, Page(Item()), Names);

        Assert.Null(item);
        Assert.Null(note);
        Assert.Empty(matches);
    }

    [Fact]
    public void A_single_match_is_returned_without_a_note_matching_the_name_case_insensitively()
    {
        var only = Item();
        var other = Item(supplier: Guid.NewGuid());

        var (item, note, matches) = ContractResolution.ResolveNamedContractItem(null, "salesforce", Page(only, other), Names);

        Assert.Same(only, item);
        Assert.Null(note);
        Assert.Single(matches);
    }

    [Fact]
    public void Several_matches_pick_the_soonest_deadline_and_say_so()
    {
        var later = Item(deadline: new DateOnly(2027, 5, 1), renewal: new DateOnly(2027, 6, 1));
        var soonest = Item(deadline: new DateOnly(2026, 11, 1), renewal: new DateOnly(2027, 12, 1));
        var undated = Item();

        var (item, note, matches) = ContractResolution.ResolveNamedContractItem(
            null, "Salesforce", Page(undated, later, soonest), Names);

        Assert.Same(soonest, item);
        Assert.Equal([soonest, later, undated], matches);
        Assert.NotNull(note);
        Assert.Equal("calc:multi-contract-resolution", note.CitationKey);
        Assert.Equal(PackCorpus.Calc, note.Corpus);
    }

    [Fact]
    public void The_disambiguation_item_names_the_chosen_contract_and_the_count()
    {
        var chosen = Item(renewal: new DateOnly(2027, 3, 31));

        var note = ContractResolution.BuildMultiContractDisambiguationItem(chosen, 3, "Salesforce");

        Assert.Equal("Salesforce — multiple contracts", note.Title);
        Assert.Equal(
            "Using Msa CT-01 (renews 2027-03-31). Salesforce has 3 contracts on file — ask if you meant another.",
            note.Snippet);
        Assert.Null(note.Href);
    }

    [Fact]
    public void The_disambiguation_item_falls_back_to_deadline_then_end_date_then_unknown()
    {
        Assert.Contains("(cancellation deadline 2026-09-01)",
            ContractResolution.BuildMultiContractDisambiguationItem(Item(deadline: new DateOnly(2026, 9, 1)), 2, "S").Snippet);
        Assert.Contains("(ends 2026-10-02)",
            ContractResolution.BuildMultiContractDisambiguationItem(Item(end: new DateOnly(2026, 10, 2)), 2, "S").Snippet);
        Assert.Contains("(renewal date unknown)",
            ContractResolution.BuildMultiContractDisambiguationItem(Item(), 2, "S").Snippet);
    }

    [Fact]
    public void Display_name_and_title_fall_back_to_the_contract_type()
    {
        var named = Item();
        var anonymous = Item(supplier: Guid.NewGuid());

        Assert.Equal("Salesforce", ContractResolution.DisplayNameFor(named, Names));
        Assert.Equal("Msa", ContractResolution.DisplayNameFor(anonymous, Names));
        Assert.Equal("Salesforce · Msa", ContractResolution.ContractTitle(named, "Salesforce"));
        Assert.Equal("Unnamed supplier · Msa", ContractResolution.ContractTitle(anonymous, "Msa"));
    }
}
