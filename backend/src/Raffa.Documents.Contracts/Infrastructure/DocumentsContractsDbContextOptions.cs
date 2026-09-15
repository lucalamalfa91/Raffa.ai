using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
    /// every module's own <c>DbContext</c> and by both hosts. Npgsql's own client-side pool
    /// defaults to 100 connections *per process*; with the Worker's <c>max_replicas = 5</c> each
    /// holding a connection for as long as one document's extraction run takes (measured ~100 s),
    /// a batch's own concurrency (<c>ServiceBus__MaxConcurrentCalls = 4</c>) can reach the
    /// server's real ceiling long before any single replica's own pool ever reports itself full —
    /// Postgres then refuses the new connection outright (<c>53300 too_many_connections</c>),
    /// which reads, from this module's own next query, as an ordinary, unexplained failure: every
    /// document in the batch fails identically because every delivery needs the same thing.
    /// Bounding this pool turns "the server refuses a connection" into "this process waits its
    /// turn for one" — Npgsql's own behaviour once its client-side pool is full — the honest,
    /// gradual form of backpressure a fixed server ceiling needs, not a wall every delivery hits
    /// at once. <c>5 (Worker max replicas) × <see cref="MaxPoolSizePerProcess"/> = 40</c>, leaving
    /// 10 of the server's 50 for the API's own three replicas and anything else (a migration
    /// apply, an operator's own session). This is the bounded, code-only stopgap: a real fix
    /// belongs in Terraform (raise <c>max_connections</c>, which needs a server restart, or size
    /// the pool per host instead of one constant shared by both) and is a follow-up, not this
    /// change's own scope.
    /// </summary>
    private const int MaxPoolSizePerProcess = 8;

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
            .UseNpgsql(BoundedPoolConnectionString(connectionString), npgsql => npgsql.UseVector())
            // Postgres/ADR-009 convention is snake_case (`tenant_id`, `document`, ...); without
            // this, EF Core would emit quoted PascalCase identifiers instead.
            .UseSnakeCaseNamingConvention();

        if (tenantContext is not null)
        {
            builder.AddInterceptors(new TenantRlsConnectionInterceptor(tenantContext));
        }
    }

    /// <summary>Overrides whatever pool size (or none) <paramref name="connectionString"/> itself
    /// carries with <see cref="MaxPoolSizePerProcess"/> — one place this module's own connections
    /// are bounded, regardless of what any environment's own <c>ConnectionStrings__*</c> value
    /// says, so a config edit elsewhere cannot silently reopen the ceiling this exists to keep.
    /// </summary>
    private static string BoundedPoolConnectionString(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString) { MaxPoolSize = MaxPoolSizePerProcess }.ConnectionString;
}
