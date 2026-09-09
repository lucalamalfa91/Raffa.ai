using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Contigo.Market.Infrastructure;

/// <summary>
/// Design-time factory so `dotnet ef migrations add` / `dotnet ef migrations script` can build
/// this DbContext without a startup host. Market is a plain class library (ADR-002: domain modules
/// are not hosts); this factory lets `dotnet ef` target `src/Contigo.Market` directly as both
/// `--project` and `--startup-project` -- mirrors
/// <c>Contigo.Documents.Contracts.Infrastructure.DocumentsContractsDbContextFactory</c> /
/// <c>Contigo.Chat.Infrastructure.ChatDbContextFactory</c>.
///
/// Reads the same `ConnectionStrings__Market` environment variable the runtime DI registration
/// (<see cref="ServiceCollectionExtensions.AddMarketModule"/>) expects, so design-time and runtime
/// configuration agree; falls back to <see cref="LocalDevConnectionString"/> so a bare `dotnet ef
/// migrations add` works with no environment set up.
/// </summary>
public sealed class MarketDbContextFactory : IDesignTimeDbContextFactory<MarketDbContext>
{
    internal const string ConnectionStringEnvVar = "ConnectionStrings__Market";

    internal const string LocalDevConnectionString =
        "Host=localhost;Port=5432;Database=contigo_dev;Username=contigo;Password=contigo;Include Error Detail=true";

    public MarketDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringEnvVar)
            ?? LocalDevConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<MarketDbContext>();
        MarketDbContextOptions.Configure(optionsBuilder, connectionString);

        return new MarketDbContext(optionsBuilder.Options);
    }
}
