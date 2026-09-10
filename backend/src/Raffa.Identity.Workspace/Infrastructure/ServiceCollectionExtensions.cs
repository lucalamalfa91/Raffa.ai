using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Identity.Workspace.Infrastructure;

/// <summary>
/// Composition-root wiring for the Identity/Workspace module. ADR-002: "each module exposes an
/// AddXxx(IServiceCollection) extension method"; domain modules never wire themselves into a host
/// directly. The Api/Worker hosts will call this once they take a dependency on this module — not
/// wired up yet (no endpoint/queue handler exists to attach a tenant claim to). Task E01/F05/US01/T01
/// wired the ambient tenant claim (<see cref="ITenantContext"/>) and the RLS connection interceptor
/// into the DbContext pipeline itself, so whichever future task adds the first endpoint/handler
/// only has to call <see cref="ITenantContext.BeginScope"/> around it — the RLS backstop is
/// already live. Task E01/F05/US01/T02 adds <see cref="WorkspaceMembershipService"/> (the invite
/// + OIDC sign-in linking flow) and the production <see cref="IClock"/> it needs, for the same
/// forward-looking reason. Task E01/F09/US01/T01 (r0-integration) adds
/// <see cref="WorkspaceProvisioningService"/> (the "create workspace" half) and is the first task
/// to actually call this method from a host (<c>Raffa.Api.Program</c>).
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityWorkspaceModule(
        this IServiceCollection services, string connectionString)
    {
        // TryAdd: any module (or the host) may call this defensively; only the first
        // registration wins, and every module shares the same ambient tenant claim (ADR-009).
        services.TryAddSingleton<ITenantContext, TenantContext>();
        services.TryAddSingleton<IClock>(SystemClock.Instance);

        services.AddDbContext<IdentityWorkspaceDbContext>(
            (sp, options) => IdentityWorkspaceDbContextOptions.Configure(
                options, connectionString, sp.GetRequiredService<ITenantContext>()));

        services.TryAddScoped<WorkspaceMembershipService>();
        services.TryAddScoped<WorkspaceProvisioningService>();

        return services;
    }
}
