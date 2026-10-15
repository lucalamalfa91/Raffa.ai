using Microsoft.Extensions.DependencyInjection;
using Raffa.AiFlows.Ask.Handlers;
using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.Tests.TestSupport;
using Raffa.Chat.Domain;
using Raffa.Chat.Infrastructure;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.Insights;
using Raffa.Market;
using Raffa.Renewals.Infrastructure;
using Raffa.Savings.Infrastructure;
using Raffa.Suppliers.Products.Infrastructure;

namespace Raffa.AiFlows.Tests.Ask.Handlers;

/// <summary>
/// F1-T08: one handler per intent, the empty pack for an intent with none (the former
/// <c>_ =&gt; []</c> arm), and the pre-interview short-circuit set the pipeline relies on.
/// </summary>
public sealed class IntentHandlerRegistryTests
{
    private sealed class FakeHandler(params AskIntent[] intents) : IIntentHandler
    {
        public IReadOnlyList<AskIntent> Intents { get; } = intents;

        public Task<IntentOutcome> HandleAsync(InDomainTurn turn, CancellationToken cancellationToken) =>
            Task.FromResult<IntentOutcome>(new IntentOutcome.Pack([]));
    }

    [Fact]
    public void An_intent_with_no_handler_falls_back_to_the_empty_pack_handler()
    {
        var fallback = new DefaultIntentHandler();
        var registry = new IntentHandlerRegistry([new FakeHandler(AskIntent.Clause)], fallback);

        Assert.Same(fallback, registry.For(AskIntent.PortfolioMarketPosition));
        Assert.Same(fallback, registry.For((AskIntent)9999));
    }

    [Fact]
    public async Task The_fallback_handler_returns_an_empty_pack()
    {
        var outcome = await new DefaultIntentHandler().HandleAsync(null!, CancellationToken.None);

        Assert.Empty(Assert.IsType<IntentOutcome.Pack>(outcome).Items);
    }

    [Fact]
    public void Two_handlers_claiming_one_intent_are_refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new IntentHandlerRegistry(
                [new FakeHandler(AskIntent.Clause), new FakeHandler(AskIntent.Savings, AskIntent.Clause)],
                new DefaultIntentHandler()));

        Assert.Contains("Clause", ex.Message);
    }

    [Theory]
    [InlineData(AskIntent.WebResearch, true)]
    [InlineData(AskIntent.Navigate, true)]
    [InlineData(AskIntent.QuoteRoute, true)]
    [InlineData(AskIntent.StructuredFact, false)]
    [InlineData(AskIntent.Clause, false)]
    [InlineData(AskIntent.Savings, false)]
    [InlineData(AskIntent.PortfolioMarketPosition, false)]
    public void Only_web_research_and_the_routing_intents_end_the_turn_before_the_interview(AskIntent intent, bool expected) =>
        Assert.Equal(expected, IntentPolicy.IsPreInterviewShortCircuit(intent));

    [Fact]
    public void Every_intent_resolves_to_exactly_one_registered_handler_except_the_market_position_fallback()
    {
        var services = DocumentsModuleServices.Create();
        services.AddMarketModule();
        services.AddChatModule();
        services.AddSuppliersProductsModule(DocumentsModuleServices.ConnectionString);
        services.AddRenewalsModule(DocumentsModuleServices.ConnectionString);
        services.AddSavingsModule(DocumentsModuleServices.ConnectionString);
        services.AddInsightsModule();
        services.AddIdentityWorkspaceModule(DocumentsModuleServices.ConnectionString);
        services.AddScoped<Raffa.AiFlows.WebResearch.Ports.IWorkspaceWebResearchPolicy, HostWorkspaceWebResearchPolicy>();
        services.AddScoped<Raffa.Chat.Application.WebResearch.IWebResearchBudget, HostWebResearchBudget>();
        services.AddAiFlows();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();

        var registry = scope.ServiceProvider.GetRequiredService<IntentHandlerRegistry>();
        var fallback = scope.ServiceProvider.GetRequiredService<DefaultIntentHandler>();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<InDomainReplyPipeline>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AskCopilotService>());

        foreach (var intent in Enum.GetValues<AskIntent>())
        {
            var handler = registry.For(intent);
            if (intent == AskIntent.PortfolioMarketPosition)
            {
                Assert.Same(fallback, handler);
            }
            else
            {
                Assert.NotSame(fallback, handler);
                Assert.Contains(intent, handler.Intents);
            }
        }

        Assert.Equal(10, scope.ServiceProvider.GetServices<IIntentHandler>().Count());
    }

    [Fact]
    public void Calling_AddAiFlows_twice_registers_each_handler_once()
    {
        var services = new ServiceCollection();

        services.AddAiFlows().AddAiFlows();

        Assert.Equal(10, services.Count(d => d.ServiceType == typeof(IIntentHandler)));
    }

    private sealed class HostWorkspaceWebResearchPolicy : Raffa.AiFlows.WebResearch.Ports.IWorkspaceWebResearchPolicy
    {
        public Task<bool> IsEnabledAsync(Raffa.SharedKernel.TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class HostWebResearchBudget : Raffa.Chat.Application.WebResearch.IWebResearchBudget
    {
        public Task<bool> HasRemainingAsync(Raffa.SharedKernel.TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<Raffa.Chat.Application.WebResearch.WebResearchReservation?> TryReserveAsync(
            Raffa.SharedKernel.TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Raffa.Chat.Application.WebResearch.WebResearchReservation?>(null);

        public Task ReleaseAsync(Raffa.Chat.Application.WebResearch.WebResearchReservation reservation, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
