using Raffa.Messaging;
using Raffa.Documents.Contracts.Application.Extraction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Raffa.Worker.Tests;

/// <summary>Task E16/F02/US02/T01 AC-7 (fix 2026-09-14): one predicate picks the transport -- the
/// in-process queue with no namespace configured, Service Bus otherwise -- on the consuming side.</summary>
public sealed class ExtractionTransportSelectionTests
{
    [Fact]
    public void No_namespace_selects_the_in_process_consumer()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddExtractionQueuePublisher(configuration);
        services.AddExtractionQueueConsumer(configuration);

        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(InMemoryExtractionConsumerHostedService));
        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(ServiceBusExtractionConsumerHostedService));
        Assert.Contains(services, d => d.ServiceType == typeof(IExtractionDeadLetterResubmitter));
    }

    [Fact]
    public void A_namespace_selects_the_service_bus_consumer_instead()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ServiceBus:FullyQualifiedNamespace"] = "sbns-raffa-dev.servicebus.windows.net",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddExtractionQueuePublisher(configuration);
        services.AddExtractionQueueConsumer(configuration);

        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(ServiceBusExtractionConsumerHostedService));
        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(InMemoryExtractionConsumerHostedService));
        Assert.Contains(services, d => d.ImplementationType == typeof(ServiceBusExtractionDeadLetterResubmitter));
    }
}
