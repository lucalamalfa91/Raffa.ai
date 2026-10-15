using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Raffa.AiFlows.QuoteExtraction.Orchestration;
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

        var handler = Assert.Single(handlers);
        Assert.IsType<QuoteExtractionJobHandler>(handler);
        Assert.Equal(JobKinds.QuoteExtraction, handler.Kind);
    }

    [Fact]
    public void Without_a_quotes_connection_the_worker_composes_no_quote_machinery()
    {
        using var host = BuildHost(quotesConnectionString: null);
        using var scope = host.Services.CreateScope();

        Assert.Empty(scope.ServiceProvider.GetServices<IJobHandler>());
        Assert.Null(scope.ServiceProvider.GetService<QuoteExtractionPipeline>());
    }
}
