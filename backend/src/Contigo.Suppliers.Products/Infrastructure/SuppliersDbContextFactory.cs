using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Contigo.Suppliers.Products.Infrastructure;

/// <summary>
/// Design-time factory so `dotnet ef migrations add` / `dotnet ef database update` can build this
/// DbContext without a startup host. Suppliers/Products is a plain class library (ADR-002: domain
/// modules are not hosts) — mirrors
/// <c>Contigo.Renewals.Infrastructure.RenewalsDbContextFactory</c> exactly.
///
/// Reads the same `ConnectionStrings__SuppliersProducts` environment variable the runtime DI
/// registration (<see cref="ServiceCollectionExtensions"/>) expects, so design-time and runtime
/// configuration agree; falls back to <see cref="LocalDevConnectionString"/> so a bare `dotnet ef
/// migrations add` works with no environment set up.
/// </summary>
public sealed class SuppliersDbContextFactory : IDesignTimeDbContextFactory<SuppliersDbContext>
{
    internal const string ConnectionStringEnvVar = "ConnectionStrings__SuppliersProducts";

    internal const string LocalDevConnectionString =
        "Host=localhost;Port=5432;Database=contigo_dev;Username=contigo;Password=contigo;Include Error Detail=true";

    public SuppliersDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringEnvVar)
            ?? LocalDevConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<SuppliersDbContext>();
        SuppliersDbContextOptions.Configure(optionsBuilder, connectionString);

        return new SuppliersDbContext(optionsBuilder.Options);
    }
}
