using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raffa.AiFlows.QuoteExtraction.Agents;
using Raffa.AiFlows.QuoteExtraction.Orchestration;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel.Jobs;

namespace Raffa.AiFlows.QuoteExtraction;

/// <summary>
/// Registration of the quote-extraction flow (F6): the line-extraction service and the pipeline
/// that <c>POST /api/quotes</c> runs in the request when no queue is configured, and the job handler the
/// Worker runs it with when one is (task F6-T01). Called by
/// <see cref="AiFlowsServiceCollectionExtensions.AddAiFlows"/>.
/// </summary>
public static class QuoteExtractionFlowServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="QuoteLineExtractionService"/> and <see cref="QuoteExtractionPipeline"/>
    /// (both Scoped, as before the move: they share the request's own <c>QuotesDbContext</c>).
    /// A host that did not compose the Quotes module (<c>AddQuotesModule</c> registers
    /// <c>QuotesDbContext</c>) has no use for the flow and its dependencies could never resolve, so
    /// nothing is registered there: this is the Worker, which never registered either type, and
    /// whose container is validated eagerly in Development. The Api host calls
    /// <c>AddQuotesModule</c> before <c>AddAiFlows</c>, so it always gets the flow.
    /// </summary>
    public static IServiceCollection AddQuoteExtractionFlow(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(QuotesDbContext)))
        {
            return services;
        }

        // Task F6-T07: the flow's settings, from the QuoteExtraction configuration section.
        services.AddOptions<QuoteExtractionOptions>()
            .Configure<IConfiguration>((options, configuration) =>
                configuration.GetSection(QuoteExtractionOptions.SectionName).Bind(options));

        services.TryAddScoped<QuoteLineExtractionService>();
        services.TryAddScoped<QuoteExtractionPipeline>();
        // Task F6-T01: the Worker's queue consumer finds this handler by its job kind. Registered in
        // every host that composes the flow; only a host running the consumer ever resolves it.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IJobHandler, QuoteExtractionJobHandler>());

        return services;
    }
}
