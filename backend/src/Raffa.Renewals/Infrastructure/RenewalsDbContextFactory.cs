using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Raffa.Renewals.Infrastructure;

/// <summary>
/// Design-time factory so `dotnet ef migrations add` / `dotnet ef database update` can build this
/// DbContext without a startup host. Renewals is a plain class library (ADR-002: domain modules are
/// not hosts); the Api/Worker composition roots do not need a startup-project detour because of
/// this factory — mirrors <c>Raffa.Audit.Infrastructure.AuditDbContextFactory</c> exactly.
///
/// Reads the same `ConnectionStrings__Renewals` environment variable the runtime DI registration
/// (<see cref="ServiceCollectionExtensions"/>, via <c>Raffa.Api</c>/<c>Raffa.Worker</c>'s own
/// `Program.cs`) expects, so design-time and runtime configuration agree; falls back to
/// <see cref="LocalDevConnectionString"/> so a bare `dotnet ef migrations add` works with no
/// environment set up.
/// </summary>
public sealed class RenewalsDbContextFactory : IDesignTimeDbContextFactory<RenewalsDbContext>
{
    internal const string ConnectionStringEnvVar = "ConnectionStrings__Renewals";

    internal const string LocalDevConnectionString =
        "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true";

    public RenewalsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringEnvVar)
            ?? LocalDevConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<RenewalsDbContext>();
        RenewalsDbContextOptions.Configure(optionsBuilder, connectionString);

        return new RenewalsDbContext(optionsBuilder.Options);
    }
}
