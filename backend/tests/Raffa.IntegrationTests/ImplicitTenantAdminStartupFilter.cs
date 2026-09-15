using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.IntegrationTests;

/// <summary>
/// Fix 2026-09-14, NW-05 applied to the data plane. Every tenant-scoped route now resolves the
/// caller through <c>Raffa.Api.Infrastructure.ICallerContext</c>: a validated identity (401),
/// then <c>X-Tenant-Id</c> as an authorized selector (400), then a live membership in that tenant
/// (404). The endpoint tests in this project predate that: they name a tenant with
/// <c>X-Tenant-Id</c> and never present a caller, because until w15 the header alone was the
/// whole story. Rewriting every one of those requests to also seed a workspace member is churn
/// that proves nothing about the endpoints they test, so this test-host-only filter does the one
/// thing they all implicitly assumed: a request that presents no caller runs as an implicit Admin,
/// whose membership row in the tenant the request names is created on first use.
///
/// <para>
/// <b>It never touches a request that presents a caller.</b> A test that sends <c>X-User-Id</c>
/// gets exactly the real behaviour -- 404 without a membership, 403 for the wrong role -- so the
/// authorization tests keep their teeth, and the gate itself is proven by hosts built without
/// this filter at all (the token-gate tests), where no caller means 401 on every guarded route.
/// Registered only by the test fixtures; <c>Raffa.Api.Program</c> never sees this type.
/// </para>
/// </summary>
public sealed class ImplicitTenantAdminStartupFilter : IStartupFilter
{
    public const string Email = "implicit-admin@raffa.test";

    private const string TenantHeaderName = "X-Tenant-Id";
    private const string UserHeaderName = "X-User-Id";

    private static readonly SemaphoreSlim Serializer = new(1, 1);

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            // A request that presents no caller at all runs as the implicit Admin (so a request with a
            // missing or malformed tenant header reaches ICallerContext's 400, as it did before NW-05);
            // the membership is only granted when the request actually names a tenant. A request that
            // presents a caller -- even a blank one -- is never touched.
            // Integration hosts: a request that names a tenant but presents no caller runs as that
            // tenant's implicit Admin. A request with no tenant header is left alone -- R0's
            // "unauthenticated caller cannot read the audit trail" must stay a genuinely anonymous
            // request (401), and no integration test relies on the 400 for a missing tenant header.
            var headers = context.Request.Headers;
            if (!headers.ContainsKey(UserHeaderName)
                && headers.TryGetValue(TenantHeaderName, out var tenantValues)
                && Guid.TryParse(tenantValues.ToString(), out var tenantGuid))
            {
                headers[UserHeaderName] = Email;
                await EnsureMembershipAsync(context.RequestServices, new TenantId(tenantGuid), Email, WorkspaceRoleName.Admin, context.RequestAborted)
                    .ConfigureAwait(false);
            }

            await nextMiddleware().ConfigureAwait(false);
        });

        next(app);
    };

    /// <summary>Idempotently makes <paramref name="email"/> a member of <paramref name="tenant"/> with
    /// <paramref name="role"/> -- the same seed the middleware uses for the implicit Admin, exposed so a
    /// test that presents its own caller can give that caller the membership NW-05 now requires.</summary>
    public static async Task EnsureMembershipAsync(
        IServiceProvider services, TenantId tenant, string email, WorkspaceRoleName role, CancellationToken cancellationToken = default)
    {
        await Serializer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Own scope, own DbContext: the request's DbContext must not inherit tracked rows or a
            // half-open connection from this seed. The tenant scope is opened for the RLS policy on
            // the Postgres-backed hosts and disposed before the request continues.
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var tenantScope = tenantContext.BeginScope(tenant);

            var alreadyMember = await db.WorkspaceUsers
                .AnyAsync(u => u.TenantId == tenant && u.Email == email, cancellationToken)
                .ConfigureAwait(false);
            if (alreadyMember)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var roleRow = await db.WorkspaceRoles
                .SingleOrDefaultAsync(r => r.TenantId == tenant && r.Name == role, cancellationToken)
                .ConfigureAwait(false);
            if (roleRow is null)
            {
                roleRow = new WorkspaceRole { TenantId = tenant, Name = role, CreatedAt = now };
                db.WorkspaceRoles.Add(roleRow);
            }

            // ADR-010 w16 footer S16-1: CallerContext/WorkspaceRoleResolver match ExternalSubjectId
            // only now (Email dropped as an authorization leg), so this shim's simulated member must
            // carry the same value there too -- it is what the X-User-Id -> `oid` bridge presents.
            var user = new WorkspaceUser { TenantId = tenant, Email = email, ExternalSubjectId = email, CreatedAt = now };
            db.WorkspaceUsers.Add(user);
            db.WorkspaceMemberships.Add(new WorkspaceMembership
            {
                TenantId = tenant,
                WorkspaceUserId = user.Id,
                WorkspaceRoleId = roleRow.Id,
                CreatedAt = now,
            });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Serializer.Release();
        }
    }
}
