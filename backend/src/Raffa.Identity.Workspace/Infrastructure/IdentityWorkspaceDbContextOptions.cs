using Raffa.SharedKernel.Persistence;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Identity.Workspace.Infrastructure;

/// <summary>
/// Single place that configures <see cref="IdentityWorkspaceDbContext"/> provider options, so the
/// runtime DI path (<see cref="ServiceCollectionExtensions"/>), the design-time factory
/// (<see cref="IdentityWorkspaceDbContextFactory"/>), and the test project can never drift apart
/// on how the Npgsql provider, snake_case naming convention, and tenant claim interceptor are
/// wired (ADR-003/ADR-009). Public so the test project (a separate assembly) can point it at a
/// disposable Testcontainers connection string instead of duplicating this setup.
/// </summary>
public static class IdentityWorkspaceDbContextOptions
{
    /// <summary>
    /// Configures the Npgsql provider and snake_case naming convention.
    /// <paramref name="tenantContext"/> is optional so design-time tooling (migrations) and tests
    /// that do not exercise tenancy can omit it — the tenant-aware connection interceptor
    /// (ADR-009: `SET`/`RESET app.tenant_id` per connection) is only wired in when it is supplied.
    /// The runtime DI path (<see cref="ServiceCollectionExtensions"/>) always supplies one so the
    /// RLS backstop is live on every request/job path.
    ///
    /// <paramref name="callerIdentityContext"/> (task E14/F03/US01/T01, wave w14; ADR-025 §F.2):
    /// this module's <c>workspace_user</c> table is the one table in the whole product with a
    /// second, identity-keyed RLS policy (`identity_self`, migration
    /// `AddWorkspaceUserIdentitySelfReadPolicy`) — <see cref="WorkspaceDirectoryService"/>'s
    /// discovery read is what needs `app.identity_subject` actually set on this DbContext's own
    /// connections, so this is the one <c>*DbContextOptions.Configure</c> call site across every
    /// module that supplies a non-null value — <see cref="TenantRlsConnectionInterceptor"/>'s own
    /// doc comment names this exact clause ("only a caller that actually needs the identity claim
    /// supplies one"). Omitted (the default), a connection through this DbContext never carries an
    /// identity claim, exactly as before this task.
    /// </summary>
    public static void Configure(
        DbContextOptionsBuilder builder,
        string connectionString,
        ITenantContext? tenantContext = null,
        ICallerIdentityContext? callerIdentityContext = null)
    {
        builder
            .UseNpgsql(PostgresConnectionPool.Bound(connectionString))
            // Postgres/ADR-009 convention is snake_case (`tenant_id`, `workspace`, ...); without
            // this, EF Core would emit quoted PascalCase identifiers instead.
            .UseSnakeCaseNamingConvention();

        if (tenantContext is not null)
        {
            builder.AddInterceptors(new TenantRlsConnectionInterceptor(tenantContext, callerIdentityContext));
        }
    }
}
