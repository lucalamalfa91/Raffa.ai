using Raffa.SharedKernel.Persistence;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Infrastructure;

/// <summary>
/// Single place that configures <see cref="DocumentsContractsDbContext"/> provider options, so
/// the runtime DI path (<see cref="ServiceCollectionExtensions"/>), the design-time factory
/// (<see cref="DocumentsContractsDbContextFactory"/>), and the integration test project can
/// never drift apart on how the Npgsql provider, pgvector plugin, naming convention, and tenant
/// claim interceptor are wired (ADR-003/ADR-009). Public so the test project (a separate
/// assembly) can point it at a disposable Testcontainers connection string instead of
/// duplicating this setup.
/// </summary>
public static class DocumentsContractsDbContextOptions
{
    /// <summary>
    /// Fix 2026-09-15, live symptom on `dev` after PR #118 (Worker concurrency) and PR #120
    /// (priority by claim, one more query per delivery) landed together: `psql-raffa-dev` is a
    /// Burstable <c>Standard_B1ms</c> server, <c>max_connections = 50</c>, server-wide, shared by
    /// every module's own <c>DbContext</c> and by both hosts, and Npgsql's own client-side pool
    /// defaults to 100 connections per process. This module bounded its pool to 8 first; the bound
    /// now lives in <see cref="PostgresConnectionPool"/> (SharedKernel) and every module's
    /// <c>Configure</c> applies it, because the eight unbounded pools that remained were still
    /// enough for one fan-out screen to take the whole server down (demo, 2026-09-22: 500s on
    /// Renewals and Contract 360). The host applies its own smaller per-process budget on top;
    /// see that type's doc comment for the arithmetic.
    /// </summary>

    /// <summary>
    /// Configures the Npgsql provider, pgvector plugin, and snake_case naming convention.
    /// <paramref name="tenantContext"/> is optional so design-time tooling (migrations) and
    /// tests that do not exercise tenancy can omit it — the tenant-aware connection interceptor
    /// (ADR-009: `SET`/`RESET app.tenant_id` per connection) is only wired in when it is
    /// supplied. The runtime DI path (<see cref="ServiceCollectionExtensions"/>) always
    /// supplies one so the RLS backstop is live on every request/job path.
    /// </summary>
    public static void Configure(
        DbContextOptionsBuilder builder,
        string connectionString,
        ITenantContext? tenantContext = null)
    {
        builder
            // .UseVector() registers the pgvector plugin on the Npgsql provider so the `vector`
            // column type and the Pgvector.Vector CLR type are recognised (ADR-003).
            .UseNpgsql(PostgresConnectionPool.Bound(connectionString), npgsql => npgsql.UseVector())
            // Postgres/ADR-009 convention is snake_case (`tenant_id`, `document`, ...); without
            // this, EF Core would emit quoted PascalCase identifiers instead.
            .UseSnakeCaseNamingConvention();

        if (tenantContext is not null)
        {
            builder.AddInterceptors(new TenantRlsConnectionInterceptor(tenantContext));
        }
    }
}
