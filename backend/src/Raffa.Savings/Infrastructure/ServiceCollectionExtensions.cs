using Raffa.Benchmark;
using Raffa.Savings.Application;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Savings.Infrastructure;

/// <summary>
/// Composition-root wiring for the Savings module (ADR-002: "each module exposes an
/// AddXxx(IServiceCollection) extension method"). Task E04/F02/US02/T01 (savings-opportunity) gives
/// this module its first <c>DbContext</c> (<see cref="SavingsDbContext"/>, backing
/// <see cref="SavingsOpportunityService"/>) —
/// <c>Raffa.Savings.Application.PriceNormalizationCalculator</c> (task E04/F02/US01/T01) needed no
/// registration at all (a stateless, dependency-free calculator a caller `new`s up directly), so
/// this is this module's first `AddXxxModule` call of any kind — the same "wiring lands with the
/// first real caller" gap this module's own README section used to name, now closed the same way
/// `Raffa.Renewals`'s own <c>RenewalAction</c> closed it there.
///
/// <see cref="ITenantContext"/>/<see cref="TenantContext"/> registration mirrors
/// <c>Raffa.Renewals.Infrastructure.ServiceCollectionExtensions.AddRenewalsModule</c>'s own
/// `TryAddSingleton` exactly — the same ambient tenant claim every module's DbContext shares
/// (ADR-009), so whichever module's `AddXxxModule` runs first in a given host wins the single
/// registration, harmlessly, for every module after it. Any host that calls this method must also
/// call `Raffa.Audit`'s `AddAuditModule` for <see cref="SavingsOpportunityService"/>'s own
/// <see cref="IAuditWriter"/> dependency to resolve — the same landmine
/// <c>RenewalActionService</c>'s own registration already flags.
///
/// Task E04/F03/US01/T01 (savings-kpis) adds <see cref="SavingsKpiCalculator"/> (stateless,
/// dependency-free — <c>TryAddSingleton</c>, same treatment
/// <c>Raffa.Renewals.Application.RenewalEngine</c>/<c>PriorityScoreCalculator</c> already get) and
/// <see cref="SavingsKpiQueryService"/> (Scoped — shares the request's own
/// <see cref="SavingsDbContext"/> instance, same as <see cref="SavingsOpportunityService"/> above).
///
/// <para>
/// Task E04/F04/US01/T01 (r3-integration) closes the last open wiring gap `backend/README.md`'s own
/// "Benchmark Service" section named ("no host calls <c>AddBenchmarkModule</c> yet ... the same
/// 'wiring lands with the first real caller' gap <c>Raffa.Savings</c> ... will close"): this method
/// now also calls <see cref="Raffa.Benchmark.ServiceCollectionExtensions.AddBenchmarkModule"/> —
/// the exact same "a module that depends on another module's interface registers that dependency's
/// own DI wiring transitively" convention
/// <c>Raffa.Documents.Contracts.Infrastructure.ServiceCollectionExtensions.AddDocumentsContractsModule</c>
/// already established for <c>Raffa.AiGateway.ServiceCollectionExtensions.AddAiGatewayModule</c>
/// (see that method's own doc comment; also <c>Raffa.Api.Program</c>'s own remark on
/// <c>AddChatModule</c>). <see cref="Raffa.Benchmark.ServiceCollectionExtensions.AddBenchmarkModule"/>
/// is itself all <c>TryAdd</c>/<c>TryAddEnumerable</c> (idempotent), so calling it here is safe even if
/// a future task also calls it from <c>Raffa.Renewals</c>'s or <c>Raffa.Quotes</c>'s own
/// <c>AddXxxModule</c> — whichever runs first in a given host wins the single registration, harmlessly,
/// for every module after it (mirrors this method's own <see cref="ITenantContext"/> remark above). No
/// host-side (<c>Raffa.Api.Program</c>/<c>Raffa.Worker.Program</c>) change is required: both
/// already call <c>AddSavingsModule</c>, so <see cref="Raffa.Benchmark.IBenchmarkService"/> becomes
/// resolvable as a side effect of that existing call, not a new call list entry.
/// </para>
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSavingsModule(this IServiceCollection services, string connectionString)
    {
        // TryAdd: any module (or the host) may call this defensively; only the first registration
        // wins, and every module shares the same "now" (IClock) — mirrors
        // Raffa.Renewals.Infrastructure.ServiceCollectionExtensions.AddRenewalsModule /
        // Raffa.Chat.Infrastructure.ServiceCollectionExtensions.AddChatModule.
        services.TryAddSingleton<IClock, SystemClock>();

        // Same ambient tenant claim every module's DbContext shares (ADR-009) — see the type doc
        // comment.
        services.TryAddSingleton<ITenantContext, TenantContext>();

        // Task E04/F04/US01/T01 (r3-integration) — see the type doc comment's own paragraph on why
        // this call, not a Raffa.Api.Program change, is what makes IBenchmarkService resolvable.
        // PriceNormalizationCalculator/SavingsProvenanceClassifier already depend on
        // Raffa.Benchmark.Contracts types at compile time (Raffa.Savings.csproj's own
        // ProjectReference, part of this module's allowed [SharedKernel, Benchmark] reference set —
        // Raffa.ArchitectureTests.DependencyDirectionTests); this is that same dependency's runtime
        // DI registration.
        services.AddBenchmarkModule();

        services.AddDbContext<SavingsDbContext>(
            (sp, options) => SavingsDbContextOptions.Configure(
                options, connectionString, sp.GetRequiredService<ITenantContext>()));

        // SavingsOpportunityService depends on the Scoped SavingsDbContext above plus IAuditWriter —
        // any host that calls this method must also call Raffa.Audit's AddAuditModule for that to
        // resolve (see the type doc comment).
        services.AddScoped<SavingsOpportunityService>();

        // Task E04/F03/US01/T01 (savings-kpis): `GET /api/savings/kpis` — see the type doc comment.
        services.TryAddSingleton<SavingsKpiCalculator>();
        services.AddScoped<SavingsKpiQueryService>();

        return services;
    }
}
