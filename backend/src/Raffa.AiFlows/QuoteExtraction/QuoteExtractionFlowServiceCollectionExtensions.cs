using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raffa.AiFlows.QuoteExtraction.Agents;
using Raffa.AiFlows.QuoteExtraction.Orchestration;
using Raffa.Quotes.Infrastructure;

namespace Raffa.AiFlows.QuoteExtraction;

/// <summary>
/// Registration of the quote-extraction flow (F6): the line-extraction service and the pipeline
/// that <c>POST /api/quotes</c> runs synchronously in the request. Called by
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

        services.TryAddScoped<QuoteLineExtractionService>();
        services.TryAddScoped<QuoteExtractionPipeline>();

        return services;
    }
}
