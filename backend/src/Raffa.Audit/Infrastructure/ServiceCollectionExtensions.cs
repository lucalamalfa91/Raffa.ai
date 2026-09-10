using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Audit.Infrastructure;

/// <summary>
/// Composition-root wiring for the Audit module. ADR-002: "each module exposes an
/// AddXxx(IServiceCollection) extension method"; domain modules never wire themselves into a host
/// directly. Registers the DbContext (with the RLS backstop already live, same as every other
/// module), this module's <see cref="IAuditWriter"/> implementation, and its
/// <see cref="IAuditQueryService"/> read side (task E01/F06/US02/T02), so any future host wiring —
/// or any other module's own `AddXxx` — only needs to call <see cref="AddAuditModule"/> once to get
/// a working, tenant-scoped, append-only <see cref="IAuditWriter"/> and a tenant-scoped
/// <see cref="IAuditQueryService"/> from DI. Write-side callers still arrive as later tasks
/// retrofit their own modules to call <see cref="IAuditWriter"/>; <c>Raffa.Api</c>'s
/// `GET /api/audit` endpoint is this module's first <see cref="IAuditQueryService"/> caller.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAuditModule(
        this IServiceCollection services, string connectionString)
    {
        // TryAdd: any module (or the host) may call this defensively; only the first
        // registration wins, and every module shares the same ambient tenant claim (ADR-009).
        services.TryAddSingleton<ITenantContext, TenantContext>();

        services.AddDbContext<AuditDbContext>(
            (sp, options) => AuditDbContextOptions.Configure(
                options, connectionString, sp.GetRequiredService<ITenantContext>()));

        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();

        return services;
    }
}
