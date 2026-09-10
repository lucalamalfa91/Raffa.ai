using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Raffa.Quotes.Infrastructure;

/// <summary>
/// Design-time factory so `dotnet ef migrations add` / `dotnet ef database update` can build this
/// DbContext without a startup host; Quotes is a plain class library (ADR-002: domain modules are
/// not hosts) — mirrors <c>Raffa.Savings.Infrastructure.SavingsDbContextFactory</c> exactly.
///
/// Reads the same `ConnectionStrings__Quotes` environment variable the runtime DI registration
/// (<see cref="ServiceCollectionExtensions"/>, via <c>Raffa.Api</c>'s own `Program.cs`) expects,
/// so design-time and runtime configuration agree; falls back to
/// <see cref="LocalDevConnectionString"/> so a bare `dotnet ef migrations add` works with no
/// environment set up.
/// </summary>
public sealed class QuotesDbContextFactory : IDesignTimeDbContextFactory<QuotesDbContext>
{
    internal const string ConnectionStringEnvVar = "ConnectionStrings__Quotes";

    internal const string LocalDevConnectionString =
        "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true";

    public QuotesDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringEnvVar)
            ?? LocalDevConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<QuotesDbContext>();
        QuotesDbContextOptions.Configure(optionsBuilder, connectionString);

        return new QuotesDbContext(optionsBuilder.Options);
    }
}
