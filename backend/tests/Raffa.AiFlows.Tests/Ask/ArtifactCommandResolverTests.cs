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
