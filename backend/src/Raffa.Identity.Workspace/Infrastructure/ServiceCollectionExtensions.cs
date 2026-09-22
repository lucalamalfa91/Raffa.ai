using Raffa.Identity.Workspace.Application;
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
///
/// Task E14/F03/US01/T01 (wave w14 "workspace is real") adds <see cref="WorkspaceDirectoryService"/>
/// (the <c>GET /api/workspaces</c> discovery half, ADR-026 §D1) and, with it, the first DI
/// registration of <see cref="ICallerIdentityContext"/> anywhere in the solution: phase-1 task
/// E14/F01/US01/T01 added the type itself (<c>Raffa.SharedKernel.Tenancy.CallerIdentityContext</c>)
/// and wired <see cref="TenantRlsConnectionInterceptor"/> to *consume* one when supplied, but wired
/// no DbContext to actually supply it — this module's own <c>workspace_user</c> table is the only
/// one in the product the `identity_self` policy widens, so this is that first real caller.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityWorkspaceModule(
        this IServiceCollection services, string connectionString)
    {
        // TryAdd: any module (or the host) may call this defensively; only the first
        // registration wins, and every module shares the same ambient tenant claim (ADR-009).
        services.TryAddSingleton<ITenantContext, TenantContext>();
        // Same TryAdd convention, same sharing rationale, for the sibling ambient identity claim
        // (ADR-009 w14 footer clause 1 / ADR-025 §F.2) — see this type's own doc comment.
        services.TryAddSingleton<ICallerIdentityContext, CallerIdentityContext>();
        services.TryAddSingleton<IClock>(SystemClock.Instance);

        services.AddDbContext<IdentityWorkspaceDbContext>(
            (sp, options) => IdentityWorkspaceDbContextOptions.Configure(
                options,
                connectionString,
                sp.GetRequiredService<ITenantContext>(),
                sp.GetRequiredService<ICallerIdentityContext>()));

        services.TryAddScoped<WorkspaceMembershipService>();
        services.TryAddScoped<WorkspaceProvisioningService>();
        services.TryAddScoped<WorkspaceDirectoryService>();
        // ADR-030 gate 2: the workspace Admin's web-research opt-in, read by the host's policy
        // adapter and written by the settings endpoint.
        services.TryAddScoped<WorkspaceSettingsService>();

        // Task E15/F01/US01/T01 (wave w14, ADR-025 §C/§D, ADR-026 §D6): the invitation token
        // lifecycle and its mailer seam. Task E17/F01/US01/T01 (wave w15, NW-67/NW-68; ADR-026 w15
        // footer §2/§5): the guest-provisioning seam beside it, and the policy the host binds.
        // Every one of these is TryAdd, and TryAdd means the FIRST registration wins -- so the host
        // registers the real mailer (AcsInvitationMailer), the Graph provisioner and its bound
        // InvitationOptions BEFORE calling this method, and the Null defaults below only apply where
        // the host registered nothing: no transport, no directory write, a site-relative link.
        services.TryAddScoped<WorkspaceInvitationService>();
        services.TryAddScoped<IInvitationMailer, NullInvitationMailer>();
        services.TryAddScoped<IGuestProvisioner, NullGuestProvisioner>();
        services.TryAddSingleton<InvitationOptions>();

        return services;
    }
}
