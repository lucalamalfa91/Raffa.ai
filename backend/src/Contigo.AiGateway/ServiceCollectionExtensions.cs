using Azure.Core;
using Azure.Identity;
using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Fixtures;
using Contigo.AiGateway.Foundry;
using Contigo.AiGateway.Logging;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Contigo.AiGateway;

/// <summary>
/// Composition-root wiring for the AI Gateway module (ADR-002: "each module exposes an
/// AddXxx(IServiceCollection) extension method"). Called from
/// <see cref="Contigo.Documents.Contracts.Infrastructure.ServiceCollectionExtensions
/// .AddDocumentsContractsModule"/> (that module is the first, and today only, consumer — its own
/// allow-listed reference to <c>Contigo.AiGateway</c> is exactly for this), so every host that
/// already calls <c>AddDocumentsContractsModule</c> (Api, Worker) gets a working
/// <see cref="IAiGateway"/> automatically, with no call-site signature change.
///
/// Task E13/F01/US01/T02 (foundry-gateway) changed what <see cref="IAiGateway"/> resolves to:
/// <see cref="Foundry.FoundryAiGateway"/> when <see cref="AiGatewayFoundryOptions.Endpoint"/> is
/// set (Container Apps inject it, <c>infra/modules/containerapps/main.tf</c>), else
/// <see cref="FixtureAiGateway"/> — either way now always wrapped by
/// <see cref="LoggingAiGateway"/> (ADR-004/ADR-011: "always log-wrapped"), which this method did
/// not do before this task (nothing here constructed <see cref="LoggingAiGateway"/> at all — see
/// git history / that type's own long-standing "a composition root wraps whichever inner
/// IAiGateway it constructs with this type" doc comment, which was aspirational until now).
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAiGatewayModule(this IServiceCollection services)
    {
        // TryAdd: any module (or the host) may call this defensively; only the first
        // registration wins (mirrors Contigo.Documents.Contracts.Infrastructure.ServiceCollectionExtensions).
        services.TryAddSingleton<IClock, SystemClock>();

        // Same defensive TryAdd every module that ends up needing the ambient tenant claim already
        // uses (AddDocumentsContractsModule, AddAuditModule) — LoggingAiGateway (wired at the
        // bottom of this method) is this module's own first consumer of it.
        services.TryAddSingleton<ITenantContext, TenantContext>();

        // Bound lazily inside the factory (not via the Options pattern — FixtureAiGateway's
        // constructor takes the plain AiGatewayModelOptions type, so there is nothing an
        // IOptions<T> indirection would buy here) by resolving IConfiguration from the
        // container: it is always already registered by WebApplicationBuilder /
        // Host.CreateApplicationBuilder, so this method does not need its own IConfiguration
        // parameter threaded through every caller (Contigo.Api/Program.cs,
        // Contigo.Worker/WorkerServiceCollectionExtensions, AddDocumentsContractsModule).
        // AiGatewayModelOptions's own property initializers already supply ADR-004's candidate
        // defaults, so a deployment with no "AiGateway:Models" section configured still gets a
        // working (fixture-backed) gateway — Bind only overlays keys that are actually present.
        services.TryAddSingleton(sp =>
        {
            var options = new AiGatewayModelOptions();
            sp.GetRequiredService<IConfiguration>()
                .GetSection(AiGatewayModelOptions.SectionName)
                .Bind(options);
            return options;
        });

        // Task E02/F01/US02/T02 (hybrid-ocr): same bind-with-defaults pattern as
        // AiGatewayModelOptions immediately above — a deployment with no "AiGateway:Ocr" section
        // configured still gets FixtureAiGateway's registered constructor parameter satisfied with
        // ADR-017's own starting default (300 pages).
        services.TryAddSingleton(sp =>
        {
            var options = new AiGatewayOcrOptions();
            sp.GetRequiredService<IConfiguration>()
                .GetSection(AiGatewayOcrOptions.SectionName)
                .Bind(options);
            return options;
        });

        // Task E13/F01/US01/T02 (foundry-gateway): AiGatewayFoundryOptions binds the *root*
        // "AiGateway" section (Endpoint/ProjectName/DocumentIntelligenceConnection/
        // AnswerTemperature) — see that type's own doc comment for why it is a sibling of, not
        // nested under, AiGatewayModelOptions/AiGatewayOcrOptions. AiGatewayComplianceOptions was
        // already a type this module shipped (LoggingAiGateway's own no-training guard) but was
        // never actually bound/registered here — LoggingAiGateway was never constructed via DI
        // before this task, so nothing needed it resolvable yet.
        services.TryAddSingleton(sp =>
        {
            var options = new AiGatewayFoundryOptions();
            sp.GetRequiredService<IConfiguration>()
                .GetSection(AiGatewayFoundryOptions.SectionName)
                .Bind(options);
            return options;
        });

        services.TryAddSingleton(sp =>
        {
            var options = new AiGatewayComplianceOptions();
            sp.GetRequiredService<IConfiguration>()
                .GetSection(AiGatewayComplianceOptions.SectionName)
                .Bind(options);
            return options;
        });

        // Fixture path (unchanged): still registered so a FixtureAiGateway singleton exists to
        // wrap whenever AiGateway:Endpoint is unset (local dev, CI, every existing fixture test).
        services.TryAddSingleton<FixtureAiGateway>();

        // Foundry path. Constructing DefaultAzureCredential does no I/O by itself (only a later
        // GetTokenAsync call does — see FoundryTokenProvider), and nothing below ever resolves
        // FoundryAiGateway or any of its dependencies (including this HttpClient) unless
        // AiGateway:Endpoint is actually set (see the IAiGateway factory at the bottom of this
        // method) — so a fixture-only deployment or unit test never touches Azure, satisfying this
        // task's own "no live Azure in unit tests" rule even though these are registered
        // unconditionally.
        services.TryAddSingleton<TokenCredential, DefaultAzureCredential>();
        services.TryAddSingleton<FoundryTokenProvider>();

        services.TryAddSingleton(sp =>
        {
            var foundryOptions = sp.GetRequiredService<AiGatewayFoundryOptions>();
            if (string.IsNullOrWhiteSpace(foundryOptions.Endpoint))
            {
                // Unreachable in practice: every real caller below only resolves FoundryAiGateway
                // (and therefore this HttpClient, transitively) once AiGateway:Endpoint is already
                // confirmed set. Fails loudly rather than constructing an HttpClient with no
                // BaseAddress that would then fail confusingly on its first request instead.
                throw new InvalidOperationException(
                    "AiGateway:Endpoint is not configured; the Foundry-backed HttpClient cannot " +
                    "be constructed. FoundryAiGateway must only be resolved when AiGateway:Endpoint " +
                    "is set.");
            }

            var baseAddress = foundryOptions.Endpoint.EndsWith('/')
                ? foundryOptions.Endpoint
                : foundryOptions.Endpoint + "/";

            return new HttpClient { BaseAddress = new Uri(baseAddress) };
        });

        services.TryAddSingleton<FoundryHttpJsonClient>();
        services.TryAddSingleton<FoundryChatCompletionsClient>();
        services.TryAddSingleton<FoundryClassifyClient>();
        services.TryAddSingleton<FoundryExtractClient>();
        services.TryAddSingleton<FoundryEmbedClient>();
        services.TryAddSingleton<FoundryAnswerClient>();
        services.TryAddSingleton<FoundryOcrClient>();
        services.TryAddSingleton<FoundryAiGateway>();

        // Task E13/F01/US01/T02's own coding objective: "when AiGateway:Endpoint is set register
        // FoundryAiGateway, else FixtureAiGateway; always wrap the inner gateway with the existing
        // LoggingAiGateway (decorator)". Scoped, not Singleton: LoggingAiGateway depends on
        // IAuditWriter, which Contigo.Audit.Infrastructure.ServiceCollectionExtensions
        // .AddAuditModule registers Scoped (it wraps a Scoped AuditDbContext) — a Singleton
        // IAiGateway would capture that Scoped dependency for the lifetime of the host, which
        // ServiceProviderOptions.ValidateOnBuild (enabled by default for the Development
        // environment WebApplicationFactory-based tests this solution already runs under) rejects
        // at startup — the same captive-dependency reasoning
        // Contigo.Chat.Infrastructure.ServiceCollectionExtensions's own doc comment already states
        // for RagAnswerService. Every current IAiGateway consumer is already registered Scoped
        // (DocumentProcessingPipeline, StagedExtractionService, EmbeddingRetrievalService,
        // HybridDocumentParsingService, QuoteExtractionPipeline, RagAnswerService) or resolved from
        // a fresh DI scope, so Scoped-consuming-Scoped is safe. The concrete Foundry/Fixture
        // gateways and their per-role clients stay Singleton above (no per-request state of their
        // own — HttpClient/TokenCredential/options are all safely shared), so only this thin
        // decorator instance is allocated per scope, not the expensive parts.
        services.TryAddScoped<IAiGateway>(sp =>
        {
            var foundryOptions = sp.GetRequiredService<AiGatewayFoundryOptions>();

            IAiGateway inner = string.IsNullOrWhiteSpace(foundryOptions.Endpoint)
                ? sp.GetRequiredService<FixtureAiGateway>()
                : sp.GetRequiredService<FoundryAiGateway>();

            return new LoggingAiGateway(
                inner,
                sp.GetRequiredService<IAuditWriter>(),
                sp.GetRequiredService<ITenantContext>(),
                sp.GetRequiredService<AiGatewayComplianceOptions>());
        });

        return services;
    }
}
