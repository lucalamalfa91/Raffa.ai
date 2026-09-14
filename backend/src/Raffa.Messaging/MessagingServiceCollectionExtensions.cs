using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Raffa.Documents.Contracts.Application.Extraction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Messaging;

/// <summary>
/// The one place that decides which extraction-queue adapter a host runs (ADR-027 §D2). Both
/// registrations read the same <c>ServiceBus</c> section and make the same choice, so the API
/// and the Worker can never disagree about where the messages go:
/// <list type="bullet">
/// <item><c>ServiceBus:FullyQualifiedNamespace</c> set → Azure Service Bus over
/// <see cref="DefaultAzureCredential"/> (the Container Apps identity; RBAC granted by
/// <c>infra/modules/servicebus</c>);</item>
/// <item>unset → the in-process channel, <see cref="InMemoryExtractionQueue"/>, which the
/// consumer announces with a warning at startup. Tests and single-process local runs only.</item>
/// </list>
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    /// <summary>The publishing side — the API, and the Worker's own re-enqueue path.</summary>
    public static IServiceCollection AddExtractionQueuePublisher(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = BindOptions(services, configuration);
        if (options.UsesServiceBus)
        {
            AddServiceBusClient(services, options);
            services.TryAddSingleton<IExtractionQueuePublisher, ServiceBusExtractionQueuePublisher>();
        }
        else
        {
            services.TryAddSingleton<InMemoryExtractionQueue>();
            services.TryAddSingleton<IExtractionQueuePublisher>(sp => sp.GetRequiredService<InMemoryExtractionQueue>());
        }

        return services;
    }

    /// <summary>The consuming side — the Worker. Registers the handler and the matching hosted service.</summary>
    public static IServiceCollection AddExtractionQueueConsumer(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = BindOptions(services, configuration);
        services.AddScoped<ExtractionRequestedHandler>();

        if (options.UsesServiceBus)
        {
            AddServiceBusClient(services, options);
            services.AddHostedService<ServiceBusExtractionConsumerHostedService>();
        }
        else
        {
            services.TryAddSingleton<InMemoryExtractionQueue>();
            services.AddHostedService<InMemoryExtractionConsumerHostedService>();
        }

        return services;
    }

    private static ExtractionQueueOptions BindOptions(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(ExtractionQueueOptions.SectionName);
        services.Configure<ExtractionQueueOptions>(section);
        var options = new ExtractionQueueOptions();
        section.Bind(options);
        return options;
    }

    private static void AddServiceBusClient(IServiceCollection services, ExtractionQueueOptions options)
    {
        // One client per process: it owns the AMQP connection pool. DefaultAzureCredential resolves
        // the user-assigned identity through AZURE_CLIENT_ID in Container Apps and the developer's
        // own login locally (az login / VS) -- the same chain the blob adapter and Foundry use.
        services.TryAddSingleton(_ => new ServiceBusClient(
            options.FullyQualifiedNamespace,
            new DefaultAzureCredential(),
            new ServiceBusClientOptions { TransportType = ServiceBusTransportType.AmqpWebSockets }));
    }
}
