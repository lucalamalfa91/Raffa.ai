using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;
using Raffa.Suppliers.Products.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Suppliers.Products.Infrastructure;

/// <summary>
/// Composition-root wiring for the Suppliers/Products module (ADR-002: "each module exposes an
/// AddXxx(IServiceCollection) extension method"; domain modules never wire themselves into a host
/// directly). Task E13/F03/US01/T01 (parent story us-01-supplier-identity) gives this module its
/// first real content — <see cref="SuppliersDbContext"/>, <see cref="SupplierResolver"/>,
/// <see cref="SupplierNameLookup"/> — turning the bare scaffold task E13/F01/US01/T01 left behind
/// into a real module. No host calls this yet: task E13/F06/US01/T01 is the first real caller
/// (<c>Raffa.Api.Program</c>), the same "wiring lands with the first real caller" sequencing
/// this codebase already uses for every other module's own <c>AddXxxModule</c> — out of this
/// task's own file scope (this task's own instructions: do not edit <c>Program.cs</c>).
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSuppliersProductsModule(
        this IServiceCollection services, string connectionString)
    {
        // TryAdd: any module (or the host) may call this defensively; only the first registration
        // wins, and every module shares the same ambient tenant claim (ADR-009) and the same "now"
        // (IClock) — mirrors every other module's own AddXxxModule.
        services.TryAddSingleton<ITenantContext, TenantContext>();
        services.TryAddSingleton<IClock, SystemClock>();

        services.AddDbContext<SuppliersDbContext>(
            (sp, options) => SuppliersDbContextOptions.Configure(
                options, connectionString, sp.GetRequiredService<ITenantContext>()));

        // Scoped: shares the request/job's own DbContext instance (also Scoped, via AddDbContext
        // above) rather than a second, independently-tracked context — same convention every other
        // module's own AddXxxModule already uses for its own DbContext-backed services.
        services.AddScoped<ISupplierResolver, SupplierResolver>();
        services.AddScoped<ISupplierNameLookup, SupplierNameLookup>();

        return services;
    }
}
