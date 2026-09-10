using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Raffa.Documents.Contracts.Infrastructure;

/// <summary>
/// Design-time factory so `dotnet ef migrations add` / `dotnet ef database update` can build
/// this DbContext without a startup host. Documents/Contracts is a plain class library
/// (ADR-002: domain modules are not hosts); the Api/Worker composition roots do not reference
/// this module yet (that wiring is out of this task's scope — see us-03 / us-04), so this
/// factory lets `dotnet ef` target `src/Raffa.Documents.Contracts` directly as both
/// `--project` and `--startup-project`.
///
/// Reads the same `ConnectionStrings__DocumentsContracts` environment variable the runtime DI
/// registration (<see cref="ServiceCollectionExtensions"/>) expects, so design-time and runtime
/// configuration agree; falls back to <see cref="LocalDevConnectionString"/> so a bare
/// `dotnet ef migrations add` works with no environment set up.
/// </summary>
public sealed class DocumentsContractsDbContextFactory : IDesignTimeDbContextFactory<DocumentsContractsDbContext>
{
    internal const string ConnectionStringEnvVar = "ConnectionStrings__DocumentsContracts";

    internal const string LocalDevConnectionString =
        "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true";

    public DocumentsContractsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringEnvVar)
            ?? LocalDevConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(optionsBuilder, connectionString);

        return new DocumentsContractsDbContext(optionsBuilder.Options);
    }
}
