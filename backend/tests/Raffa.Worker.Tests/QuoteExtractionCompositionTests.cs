using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Raffa.AiFlows.QuoteExtraction.Orchestration;
using Raffa.Documents.Contracts.Application;
using Raffa.Market;
using Raffa.Messaging;
using Raffa.SharedKernel.Jobs;
using Raffa.Storage;

namespace Raffa.Worker.Tests;

/// <summary>
/// Task F6-T01: composition proof for the quote-extraction job handler in the Worker host. The
/// consumer finds a handler by job kind only when the first message arrives, so a Worker that forgot
/// to compose the Quotes module would boot green and dead-letter every quote. This builds the host
/// the way <c>Program.cs</c> does and resolves the handler out of it.
/// </summary>
public sealed class QuoteExtractionCompositionTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true";

    private static IHost BuildHost(string? quotesConnectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        builder.Services.AddWorkerHost(ConnectionString, ConnectionString, ConnectionString, quotesConnectionString);
        builder.Services.AddMarketModule(ConnectionString);
        builder.Services.AddAzureBlobDocumentStorage("UseDevelopmentStorage=true");
        builder.Services.AddExtractionQueuePublisher(configuration);
        builder.Services.AddExtractionQueueConsumer(configuration);

        return builder.Build();
    }

    [Fact]
    public void With_a_quotes_connection_the_worker_owns_the_quote_extraction_job_kind()
    {
        using var host = BuildHost(ConnectionString);
        using var scope = host.Services.CreateScope();

        var handlers = scope.ServiceProvider.GetServices<IJobHandler>().ToList();

        var handler = Assert.Single(handlers, h => h.Kind == JobKinds.QuoteExtraction);
        Assert.IsType<QuoteExtractionJobHandler>(handler);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_worker_owns_the_market_reprice_job_kind_with_or_without_quotes(bool withQuotes)
    {
        // The pre-warm job is queued by the API and consumed here; a Worker that did not compose it would
        // boot green and dead-letter every one of them. It does not depend on the Quotes module.
        using var host = BuildHost(withQuotes ? ConnectionString : null);
        using var scope = host.Services.CreateScope();

        var handler = Assert.Single(
            scope.ServiceProvider.GetServices<IJobHandler>(), h => h.Kind == JobKinds.MarketReprice);
        Assert.IsType<MarketRepriceJobHandler>(handler);
    }

    [Fact]
    public void Without_a_quotes_connection_the_worker_composes_no_quote_machinery()
    {
        using var host = BuildHost(quotesConnectionString: null);
        using var scope = host.Services.CreateScope();

        Assert.DoesNotContain(scope.ServiceProvider.GetServices<IJobHandler>(), h => h.Kind == JobKinds.QuoteExtraction);
        Assert.Null(scope.ServiceProvider.GetService<QuoteExtractionPipeline>());
    }
}
