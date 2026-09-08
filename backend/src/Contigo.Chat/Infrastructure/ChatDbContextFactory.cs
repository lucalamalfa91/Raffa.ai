using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Contigo.Chat.Infrastructure;

/// <summary>
/// Design-time factory so `dotnet ef migrations add` / `dotnet ef database update` can build this
/// DbContext without a startup host. Chat is a plain class library (ADR-002: domain modules are
/// not hosts); this factory lets `dotnet ef` target `src/Contigo.Chat` directly as both
/// `--project` and `--startup-project` — mirrors
/// <c>Contigo.Documents.Contracts.Infrastructure.DocumentsContractsDbContextFactory</c> /
/// <c>Contigo.Audit.Infrastructure.AuditDbContextFactory</c>.
///
/// Reads the same `ConnectionStrings__Chat` environment variable the runtime DI registration
/// (<see cref="ServiceCollectionExtensions"/>) expects — `ConnectionStrings:Chat` is this story's
/// own council-decided key (us-01-conversations "Council decisions carried into this story") —
/// so design-time and runtime configuration agree; falls back to
/// <see cref="LocalDevConnectionString"/> so a bare `dotnet ef migrations add` works with no
/// environment set up.
/// </summary>
public sealed class ChatDbContextFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
    internal const string ConnectionStringEnvVar = "ConnectionStrings__Chat";

    internal const string LocalDevConnectionString =
        "Host=localhost;Port=5432;Database=contigo_dev;Username=contigo;Password=contigo;Include Error Detail=true";

    public ChatDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringEnvVar)
            ?? LocalDevConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<ChatDbContext>();
        ChatDbContextOptions.Configure(optionsBuilder, connectionString);

        return new ChatDbContext(optionsBuilder.Options);
    }
}
