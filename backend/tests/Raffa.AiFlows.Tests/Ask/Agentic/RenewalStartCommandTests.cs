using Raffa.AiFlows.Ask.Agentic;
using Raffa.AiFlows.Ask.Orchestration;
using Raffa.Chat.Application.Conversations;
using Raffa.Documents.Contracts.Application;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Tests.Ask.Agentic;

/// <summary>
/// "Avvia il rinnovo", "aprilo in rinnovi", "intervistami per il rinnovo", "invia la mail per il rinnovo" and their English
/// forms start the renewal before the model runs; a question about the renewal, a negation or a sentence that only
/// mentions a renewal does not.
/// </summary>
public sealed class RenewalStartCommandTests
{
    [Theory]
    [InlineData("avvia il rinnovo")]
    [InlineData("Avvia il rinnovo con questi termini, tenendo conto che possiamo ridurre anche il numero di lecenze del 10 %")]
    [InlineData("avvia la pratica di rinnovo")]
    [InlineData("ok, avviamo il rinnovo di Workday")]
    [InlineData("fai partire il rinnovo")]
    [InlineData("procediamo con il rinnovo")]
    [InlineData("aprilo in rinnovi")]
    [InlineData("Aprilo in Renewals")]
    [InlineData("mettilo in rinnovo")]
    [InlineData("apri la pratica di rinnovo")]
    [InlineData("intervistami per il rinnovo")]
    [InlineData("invia la mail per il rinnovo")]
    [InlineData("manda la mail di rinnovo al fornitore")]
    [InlineData("rinnoviamo Workday")]
    [InlineData("puoi avviare il rinnovo?")]
    [InlineData("avvia il rinnovo come abbiamo detto")]
    [InlineData("start the renewal")]
    [InlineData("Let's start the Workday renewal with these terms")]
    [InlineData("kick off the renewal")]
    [InlineData("open it in renewals")]
    [InlineData("move it to renewals")]
    [InlineData("open the renewal case")]
    [InlineData("interview me for the renewal")]
    [InlineData("send the renewal email")]
    [InlineData("can you start the renewal?")]
    [InlineData("let's renew it")]
    public void A_command_starts_the_renewal(string text) => Assert.True(RenewalStartCommand.IsCommand(text), text);

    [Theory]
    [InlineData("How should we approach this renewal? Give me the strategy and the negotiation points.")]
    [InlineData("come avvio il rinnovo?")]
    [InlineData("quando dovrei avviare il rinnovo?")]
    [InlineData("should we start the renewal now?")]
    [InlineData("non avviare ancora il rinnovo")]
    [InlineData("don't start the renewal yet")]
    [InlineData("What is the start date of the renewal?")]
    [InlineData("l'inizio del rinnovo è a luglio")]
    [InlineData("scrivimi una mail al fornitore per chiedere lo sconto")]
    [InlineData("quali leve ho per il rinnovo?")]
    [InlineData("riassumi il contratto")]
    [InlineData("apri il contratto")]
    [InlineData("intervistami")]
    [InlineData("")]
    public void Anything_else_does_not(string text) => Assert.False(RenewalStartCommand.IsCommand(text), text);

    private static (PortfolioPage Portfolio, Dictionary<EntityId, string> Names, PortfolioListItem Workday, PortfolioListItem Splunk) Workspace()
    {
        var workdayId = EntityId.New();
        var splunkId = EntityId.New();
        PortfolioListItem Item(EntityId supplier) => new(Guid.NewGuid(), supplier.Value, Raffa.Documents.Contracts.Domain.ContractDocumentType.OrderForm, 344000m, "EUR",
            new DateOnly(2024, 7, 18), new DateOnly(2027, 7, 17), null, new DateOnly(2027, 6, 17), true, "completed", null, null, null);
        var workday = Item(workdayId);
        var splunk = Item(splunkId);
        return (new PortfolioPage([workday, splunk], 1, 50, 2), new Dictionary<EntityId, string> { [workdayId] = "Workday", [splunkId] = "Splunk" }, workday, splunk);
    }

    [Fact]
    public void The_chat_contract_is_the_one_started()
    {
        var (portfolio, names, workday, _) = Workspace();

        var target = AskCopilotService.RenewalCommandContract("aprilo in rinnovi", portfolio, names, workday, null);

        Assert.Equal((workday, "Workday"), target);
    }

    [Fact]
    public void A_named_supplier_wins_and_the_conversation_focus_is_the_fallback()
    {
        var (portfolio, names, workday, splunk) = Workspace();

        Assert.Equal((splunk, "Splunk"), AskCopilotService.RenewalCommandContract("avvia il rinnovo di Splunk", portfolio, names, null, null));
        Assert.Equal((workday, "Workday"), AskCopilotService.RenewalCommandContract("avvia il rinnovo", portfolio, names, null,
            ConversationState.Empty with { ActiveContractId = workday.ContractId.ToString() }));
        Assert.Equal((splunk, "Splunk"), AskCopilotService.RenewalCommandContract("avvia il rinnovo", portfolio, names, null,
            ConversationState.Empty with { PrimarySupplier = "splunk" }));
    }

    [Fact]
    public void Without_a_contract_to_tell_it_goes_to_the_model()
    {
        var (portfolio, names, _, _) = Workspace();

        Assert.Null(AskCopilotService.RenewalCommandContract("avvia il rinnovo", portfolio, names, null, null));
        Assert.Null(AskCopilotService.RenewalCommandContract("avvia il rinnovo di Workday e Splunk", portfolio, names, null, null));
    }
}
