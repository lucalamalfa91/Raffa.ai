using System.Text.Json;
using Raffa.AiFlows.Ask.Agentic;
using Raffa.AiFlows.Ask.Agentic.Tools;
using Raffa.AiFlows.Ask.Interview;
using Raffa.Chat.Application.Reply;
using Raffa.Documents.Contracts.Application;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiFlows.Tests.Ask.Agentic;

/// <summary>
/// Acting is confirmed first: the two propose tools never change anything. They end the turn with a
/// confirmation question and an <see cref="ActionProposalPayload"/> (the contract resolved by the
/// server); the app runs the action only on the user's yes.
/// </summary>
public sealed class ProposeActionToolsTests
{
    private static (AskToolContext Context, Guid ContractId) Context(string language = "it")
    {
        var supplierId = EntityId.New();
        var contractId = Guid.NewGuid();
        var item = new PortfolioListItem(contractId, supplierId.Value, Raffa.Documents.Contracts.Domain.ContractDocumentType.OrderForm, 266000m, "USD",
            new DateOnly(2023, 7, 8), new DateOnly(2026, 7, 7), null, new DateOnly(2026, 4, 8), true, "completed", null, null, null);
        var context = new AskToolContext(
            TenantId.New(), "alice", "avvia il rinnovo", language, [], new PortfolioPage([item], 1, 50, 1),
            new Dictionary<EntityId, string> { [supplierId] = "Splunk" }, null, null, AskTurnHints.None);
        return (context, contractId);
    }

    [Fact]
    public async Task Starting_a_renewal_ends_the_turn_with_a_confirmation_and_the_server_resolved_contract()
    {
        var (context, contractId) = Context();
        using var args = JsonDocument.Parse("""{"supplier":"splunk"}""");

        var result = await new ProposeStartRenewalTool().ExecuteAsync(args.RootElement, context, CancellationToken.None);

        Assert.Null(result.Failure);
        var reply = Assert.IsType<CopilotReply>(result.EndTurnReply);
        var proposal = Assert.IsType<ActionProposalPayload>(reply.Payload!.ActionProposal);
        Assert.Equal(ActionProposalPayload.StartRenewal, proposal.Kind);
        Assert.Equal(contractId.ToString(), proposal.ContractId);
        Assert.Equal("Splunk", proposal.Supplier);
        Assert.EndsWith("Confermi?", reply.AnswerMarkdown);
    }

    [Fact]
    public async Task A_supplier_that_is_not_in_the_workspace_proposes_nothing()
    {
        var (context, _) = Context();
        using var args = JsonDocument.Parse("""{"supplier":"Zscaler"}""");

        var result = await new ProposeStartRenewalTool().ExecuteAsync(args.RootElement, context, CancellationToken.None);

        Assert.NotNull(result.Failure);
        Assert.Null(result.EndTurnReply);
    }

    [Fact]
    public async Task A_stated_objective_ends_the_turn_asking_to_set_it_as_the_savings_goal()
    {
        var (context, _) = Context("en");
        using var args = JsonDocument.Parse("""{"goalText":"cut 15% of software spend this year"}""");

        var result = await new ProposeSavingsGoalTool().ExecuteAsync(args.RootElement, context, CancellationToken.None);

        var reply = Assert.IsType<CopilotReply>(result.EndTurnReply);
        var proposal = Assert.IsType<ActionProposalPayload>(reply.Payload!.ActionProposal);
        Assert.Equal(ActionProposalPayload.SetSavingsGoal, proposal.Kind);
        Assert.Equal("cut 15% of software spend this year", proposal.GoalText);
        Assert.EndsWith("Confirm?", reply.AnswerMarkdown);
    }

    [Fact]
    public void The_proposal_reaches_the_client_as_camel_case_json()
    {
        var payload = new ReplyPayload(ActionProposal: new ActionProposalPayload(ActionProposalPayload.StartRenewal, "c-1", "Splunk"));

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"actionProposal\":{\"kind\":\"start_renewal\",\"contractId\":\"c-1\",\"supplier\":\"Splunk\"", json);
    }
}
