using Microsoft.EntityFrameworkCore;

namespace Raffa.Market.Infrastructure;

/// <summary>
/// Single place that configures <see cref="MarketDbContext"/> provider options, so the runtime DI
/// path (<see cref="ServiceCollectionExtensions"/>), the design-time factory
/// (<see cref="MarketDbContextFactory"/>), and the test project can never drift apart on how the
/// Npgsql provider, pgvector plugin, and naming convention are wired (ADR-003). Public so the test
/// project (a separate assembly) can point it at a disposable Testcontainers connection string
/// instead of duplicating this setup — mirrors
/// <c>Raffa.Documents.Contracts.Infrastructure.DocumentsContractsDbContextOptions</c> /
/// <c>Raffa.Chat.Infrastructure.ChatDbContextOptions</c>.
///
/// Deliberately takes **no** <c>ITenantContext</c> parameter at all — unlike every other module's
/// own <c>Configure</c> overload, which accepts one optionally for tests/design-time tooling to
/// omit. There is no tenant-aware connection interceptor to wire in here under any circumstance
/// (see <see cref="MarketDbContext"/>'s own doc comment) — accepting the parameter "just in case"
/// would invite a future caller to actually pass one, silently reintroducing the exact per-tenant
/// coupling ADR-024 forbids for this shared index.
/// </summary>
public static class MarketDbContextOptions
{
    /// <summary>Configures the Npgsql provider, pgvector plugin, and snake_case naming convention.</summary>
    public static void Configure(DbContextOptionsBuilder builder, string connectionString)
    {
        builder
            // .UseVector() registers the pgvector plugin on the Npgsql provider so the `vector`
            // column type and the Pgvector.Vector CLR type are recognised (ADR-003).
            .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
            // Postgres/ADR-003/ADR-009 convention is snake_case (`market_record`,
            // `market_embedding`, ...); without this, EF Core would emit quoted PascalCase
            // identifiers instead -- applied here even though this module has no `tenant_id` to
            // police, for the same physical-schema consistency every other module's own DbContext
            // already keeps.
            .UseSnakeCaseNamingConvention();
    }
}
