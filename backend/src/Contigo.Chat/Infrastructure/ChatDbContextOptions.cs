using Contigo.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Contigo.Chat.Infrastructure;

/// <summary>
/// Single place that configures <see cref="ChatDbContext"/> provider options, so the runtime DI
/// path (<see cref="ServiceCollectionExtensions"/>), the design-time factory
/// (<see cref="ChatDbContextFactory"/>), and the test project can never drift apart on how the
/// Npgsql provider, snake_case naming convention, and tenant claim interceptor are wired
/// (ADR-003/ADR-009). Public so the test project (a separate assembly) can point it at a
/// disposable Testcontainers connection string instead of duplicating this setup. Mirrors
/// <c>Contigo.Documents.Contracts.Infrastructure.DocumentsContractsDbContextOptions</c> /
/// <c>Contigo.Audit.Infrastructure.AuditDbContextOptions</c> exactly, minus the pgvector plugin
/// neither this DbContext nor its two tables need.
/// </summary>
public static class ChatDbContextOptions
{
    /// <summary>
    /// Configures the Npgsql provider and snake_case naming convention.
    /// <paramref name="tenantContext"/> is optional so design-time tooling (migrations) and tests
    /// that do not exercise tenancy can omit it — the tenant-aware connection interceptor
    /// (ADR-009: `SET`/`RESET app.tenant_id` per connection) is only wired in when it is
    /// supplied. The runtime DI path (<see cref="ServiceCollectionExtensions"/>) always supplies
    /// one so the RLS backstop is live on every request/job path.
    /// </summary>
    public static void Configure(
        DbContextOptionsBuilder builder,
        string connectionString,
        ITenantContext? tenantContext = null)
    {
        builder
            .UseNpgsql(connectionString)
            // Postgres/ADR-009 convention is snake_case (`tenant_id`, `conversation`, ...);
            // without this, EF Core would emit quoted PascalCase identifiers instead.
            .UseSnakeCaseNamingConvention();

        if (tenantContext is not null)
        {
            builder.AddInterceptors(new TenantRlsConnectionInterceptor(tenantContext));
        }
    }
}
