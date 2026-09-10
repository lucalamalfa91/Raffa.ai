using Raffa.Benchmark;
using Raffa.Quotes.Application;
using Raffa.Quotes.Application.Assessment;
using Raffa.Quotes.Application.Extraction;
using Raffa.Quotes.Application.Normalization;
using Raffa.Quotes.Application.Outcome;
using Raffa.Quotes.Application.Strategy;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Quotes.Infrastructure;

/// <summary>
/// Composition-root wiring for the Quotes module (ADR-002: "each module exposes an
/// AddXxx(IServiceCollection) extension method"). Task E05/F01/US01/T01 (quote-extraction) gives
/// this module its first <c>DbContext</c> (<see cref="QuotesDbContext"/>, backing
/// <see cref="QuoteUploadService"/> and <see cref="QuoteLineExtractionService"/>) — mirrors
/// <c>Raffa.Savings.Infrastructure.ServiceCollectionExtensions.AddSavingsModule</c>'s own shape.
/// Task E05/F01/US01/T02 (quote-normalization) later adds
/// <see cref="QuoteLineNormalizationService"/> to this same registration, sharing this module's one
/// <c>DbContext</c> too.
///
/// <para>
/// Task E05/F02/US01/T01 (market-assessment) closes the gap this method's own doc comment used to
/// name here ("deliberately does not call <c>AddBenchmarkModule</c>... nothing this task adds
/// resolves <c>IBenchmarkService</c> yet... spec §11's own Quote → Benchmark → Assessment flow
/// treats those as later steps"): <see cref="MarketAssessmentService"/> is that later step's real
/// caller, so this method now also calls
/// <see cref="Raffa.Benchmark.ServiceCollectionExtensions.AddBenchmarkModule"/> — the exact same
/// "a module that depends on another module's interface registers that dependency's own DI wiring
/// transitively" convention
/// <c>Raffa.Savings.Infrastructure.ServiceCollectionExtensions.AddSavingsModule</c>'s own doc
/// comment already established for this exact call (and explicitly anticipated a future
/// <c>Raffa.Quotes</c> caller doing the same). <c>AddBenchmarkModule</c> is itself all
/// <c>TryAdd</c>/<c>TryAddEnumerable</c> (idempotent), so calling it here is safe even though
/// <c>Raffa.Api.Program</c> already calls <c>AddSavingsModule</c>, which calls it too — whichever
/// module's <c>AddXxxModule</c> runs first in a given host wins the single registration, harmlessly,
/// for every module after it.
/// </para>
///
/// Any host that calls this method must also call <c>Raffa.Audit</c>'s <c>AddAuditModule</c> for
/// <see cref="QuoteUploadService"/>'s own <see cref="IAuditWriter"/> dependency to resolve — the
/// same landmine <c>Raffa.Savings.Infrastructure.ServiceCollectionExtensions.AddSavingsModule</c>'s
/// own doc comment flags for <c>SavingsOpportunityService</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddQuotesModule(this IServiceCollection services, string connectionString)
    {
        // TryAdd: any module (or the host) may call this defensively; only the first registration
        // wins, and every module shares the same "now" (IClock) and the same ambient tenant claim
        // (ADR-009) — mirrors every other module's own AddXxxModule.
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<ITenantContext, TenantContext>();

        // Task E05/F02/US01/T01 (market-assessment) — see the type doc comment's own paragraph on
        // why this call, not a Raffa.Api.Program change, is what this module needs to resolve
        // IBenchmarkService for MarketAssessmentService below. Raffa.Quotes.csproj's own
        // ProjectReference to Raffa.Benchmark already exists (Raffa.ArchitectureTests
        // .DependencyDirectionTests' allowed [SharedKernel, Benchmark] set for this module); this is
        // that compile-time dependency's runtime DI registration.
        services.AddBenchmarkModule();

        services.AddDbContext<QuotesDbContext>(
            (sp, options) => QuotesDbContextOptions.Configure(
                options, connectionString, sp.GetRequiredService<ITenantContext>()));

        // Scoped: shares the request's own QuotesDbContext instance (also Scoped, via AddDbContext
        // above) rather than a second, independently-tracked context.
        services.AddScoped<QuoteUploadService>();
        services.AddScoped<QuoteLineExtractionService>();
        // Task E05/F01/US01/T02 (quote-normalization): shares this request's own QuotesDbContext
        // (also Scoped, registered above) with QuoteLineExtractionService — see
        // QuoteLineNormalizationService's own doc comment for why that matters.
        services.AddScoped<QuoteLineNormalizationService>();
        // Task E05/F01/US02/T01 (sku-normalization).
        services.AddScoped<SkuNormalizationService>();
        // Task E05/F02/US01/T01 (market-assessment): shares this request's own QuotesDbContext and
        // resolves the IBenchmarkService registered above.
        services.AddScoped<MarketAssessmentService>();
        // Task E05/F03/US01/T01 (negotiation-strategy): shares this request's own QuotesDbContext
        // and composes on top of the MarketAssessmentService registered immediately above (see
        // NegotiationStrategyService's own doc comment for why it depends on that service rather
        // than re-deriving the assessment itself).
        services.AddScoped<NegotiationStrategyService>();
        // Task E05/F03/US02/T01 (negotiation-outcome): shares this request's own QuotesDbContext;
        // like QuoteUploadService above, also resolves IAuditWriter, so any host calling this method
        // must also call Raffa.Audit's AddAuditModule (already true of every host that reaches this
        // far — see this method's own doc comment).
        services.AddScoped<NegotiationOutcomeService>();
        // Task E05/F01/US02/T02 (sku-recalculate): composes SkuNormalizationService and
        // MarketAssessmentService (both registered above) plus IAuditWriter — same landmine as
        // NegotiationOutcomeService immediately above.
        services.AddScoped<SkuMappingService>();

        return services;
    }
}
