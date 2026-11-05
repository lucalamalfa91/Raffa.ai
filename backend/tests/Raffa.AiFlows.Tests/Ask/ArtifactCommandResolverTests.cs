using Raffa.AiFlows.Ask.Artifacts;
using Raffa.AiFlows.Ask.Interview;
using Raffa.Chat.Application.Conversations;

namespace Raffa.AiFlows.Tests.Ask;

public sealed class ArtifactCommandResolverTests
{
    private static readonly (string Role, string Markdown)[] History =
    [
        ("you", "Riassumi il contratto Zscaler"),
        ("raffa", "Ecco il riepilogo di Zscaler."),
        ("you", "Crea un KPI che evidenzia le leve di questo contratto"),
        ("raffa", "Propongo un Renewal Leverage KPI per Zscaler."),
    ];

    [Fact]
    public void Crealo_with_a_viewer_is_create_kpi_and_keeps_the_supplier()
    {
        var result = ArtifactCommandResolver.Resolve("crealo con un visualizzatore", History, null, ["Zscaler", "Notion"]);

        Assert.Equal(ArtifactCommandKind.Create, result.Command);
        Assert.Equal("kpi", result.Kind);
        Assert.Equal("Zscaler", result.Supplier);
    }

    [Theory]
    [InlineData("create it")]
    [InlineData("build it")]
    [InlineData("make it")]
    public void English_create_reference_uses_the_visual_and_supplier_from_authorised_history(string question)
    {
        var result = ArtifactCommandResolver.Resolve(question, History, null, ["Zscaler", "Notion"]);

        Assert.Equal(ArtifactCommandKind.Create, result.Command);
        Assert.Equal("kpi", result.Kind);
        Assert.Equal("Zscaler", result.Supplier);
    }

    [Fact]
    public void Artifact_update_requires_confirmation_before_execution()
    {
        var current = new CurrentArtifactHint("artifact-1", "document", "Piano Zscaler", "Zscaler", null, 1);
        var proposal = ArtifactCommandResolver.Resolve("Aggiungi la mail al piano", [], current, ["Zscaler"]);
        Assert.Equal(ArtifactCommandKind.ProposeUpdate, proposal.Command);

        var state = ConversationState.Empty with { PendingArtifactInstruction = proposal.Instruction };
        var confirmed = ArtifactCommandResolver.Resolve("Sì", [], current, ["Zscaler"], state);
        Assert.Equal(ArtifactCommandKind.ConfirmUpdate, confirmed.Command);
        Assert.Equal(proposal.Instruction, confirmed.Instruction);

        var cancelled = ArtifactCommandResolver.Resolve("No", [], current, ["Zscaler"], state);
        Assert.Equal(ArtifactCommandKind.CancelUpdate, cancelled.Command);
    }

    [Theory]
    [InlineData("aprilo nel pannello action")]
    [InlineData("voglio poterlo aprire nel pannello azioni e consultarlo")]
    [InlineData("open it")]
    public void Open_can_only_target_the_current_persisted_artifact(string question)
    {
        var current = new CurrentArtifactHint("artifact-1", "kpi", "Zscaler Renewal Leverage", "Zscaler", null, 1);

        var result = ArtifactCommandResolver.Resolve(question, History, current, ["Zscaler", "Notion"]);

        Assert.Equal(ArtifactCommandKind.Open, result.Command);
        Assert.Equal("Zscaler", result.Supplier);
    }

    [Fact]
    public void Open_without_a_persisted_artifact_does_not_invent_a_target()
    {
        var result = ArtifactCommandResolver.Resolve("aprilo", History, null, ["Zscaler"]);

        Assert.Equal(ArtifactCommandKind.None, result.Command);
    }
}

/// <summary>
/// The Coupa chat incident (2026-11-05): a "crea un kpi" in a conversation about Coupa built the KPI
/// on Zscaler, because the resolver scanned Raffa's own portfolio answer for the first supplier
/// name; "ma il contratto era coupa" then got a clause dump instead of the KPI on Coupa; and "crea
/// un report ... con i dati di mercato" was never read as an artifact request.
/// </summary>
public sealed class ArtifactCommandResolverFocusTests
{
    private static readonly string[] Suppliers = ["Zscaler", "Oracle", "SAP", "Coupa", "Notion"];

    private static readonly (string Role, string Markdown)[] CoupaHistory =
    [
        ("you", "Riassumi il contratto Coupa"),
        ("raffa", "Ecco un riassunto operativo del contratto Coupa · OrderForm."),
        ("you", "come fai a non averlo, controlla bene"),
        ("raffa", "Dai tuoi contratti validati\n* Zscaler — criticality 50.8/100\n* Oracle — criticality 47.7/100\n* Coupa — criticality 40.9/100"),
    ];

    [Fact]
    public void Crea_un_kpi_follows_the_server_owned_primary_supplier_not_the_first_name_in_raffas_answer()
    {
        var state = ConversationState.Empty with { PrimarySupplier = "Coupa" };

        var result = ArtifactCommandResolver.Resolve("crea un kpi", CoupaHistory, null, Suppliers, state);

        Assert.Equal(ArtifactCommandKind.Create, result.Command);
        Assert.Equal("kpi", result.Kind);
        Assert.Equal("Coupa", result.Supplier);
    }

    [Fact]
    public void Without_state_the_supplier_comes_from_the_users_own_turns_never_from_raffas()
    {
        var result = ArtifactCommandResolver.Resolve("crea un kpi", CoupaHistory, null, Suppliers);

        Assert.Equal(ArtifactCommandKind.Create, result.Command);
        Assert.Equal("Coupa", result.Supplier);
    }

    [Theory]
    [InlineData("ma il contratto era coupa")]
    [InlineData("no, intendevo Coupa")]
    [InlineData("I meant Coupa")]
    [InlineData("wrong contract, it's Coupa")]
    public void A_correction_naming_another_supplier_rebuilds_the_current_artifact_on_it(string question)
    {
        var current = new CurrentArtifactHint("artifact-1", "kpi", "KPI Contratto Zscaler", "Zscaler", null, 1);

        var result = ArtifactCommandResolver.Resolve(question, CoupaHistory, current, Suppliers, ConversationState.Empty with { PrimarySupplier = "Coupa" });

        Assert.Equal(ArtifactCommandKind.Create, result.Command);
        Assert.Equal("kpi", result.Kind);
        Assert.Equal("Coupa", result.Supplier);
    }

    [Fact]
    public void A_question_naming_the_artifacts_own_supplier_is_not_a_correction()
    {
        var current = new CurrentArtifactHint("artifact-1", "kpi", "KPI Contratto Coupa", "Coupa", null, 1);

        var result = ArtifactCommandResolver.Resolve("quando scade Coupa?", CoupaHistory, current, Suppliers);

        Assert.Equal(ArtifactCommandKind.None, result.Command);
    }

    [Fact]
    public void A_report_with_market_data_is_a_document_on_the_primary_supplier_with_the_market_scope()
    {
        var state = ConversationState.Empty with { PrimarySupplier = "Coupa" };

        var result = ArtifactCommandResolver.Resolve(
            "crea un report per capire a che punto è questo contratto con i dati di mercato", CoupaHistory[..2], null, Suppliers, state);

        Assert.Equal(ArtifactCommandKind.Create, result.Command);
        Assert.Equal("document", result.Kind);
        Assert.Equal("Coupa", result.Supplier);
        Assert.True(result.UsesMarket);
        Assert.False(result.UsesPortfolio);
    }

    [Fact]
    public void A_portfolio_dashboard_has_no_focus_supplier()
    {
        var state = ConversationState.Empty with { PrimarySupplier = "Coupa" };

        var result = ArtifactCommandResolver.Resolve("crea una dashboard di tutti i contratti", [], null, Suppliers, state);

        Assert.Equal(ArtifactCommandKind.Create, result.Command);
        Assert.Equal("dashboard", result.Kind);
        Assert.Null(result.Supplier);
        Assert.True(result.UsesPortfolio);
    }

    [Fact]
    public void The_kind_the_message_names_wins_over_the_one_the_history_discussed()
    {
        var history = new[] { ("you", "Crea un KPI per Coupa"), ("raffa", "Ho creato il KPI.") };

        var result = ArtifactCommandResolver.Resolve("ora crea un report", history, null, Suppliers);

        Assert.Equal("document", result.Kind);
        Assert.Equal("Coupa", result.Supplier);
    }
}
