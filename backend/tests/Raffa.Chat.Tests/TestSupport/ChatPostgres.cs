using Microsoft.EntityFrameworkCore;
using Npgsql;
using Raffa.Chat.Infrastructure;
using Raffa.SharedKernel.Tenancy;
using Testcontainers.PostgreSql;

namespace Raffa.Chat.Tests.TestSupport;

/// <summary>
/// A migrated Chat database reachable through an unprivileged, RLS-subject role (a Postgres
/// superuser bypasses row security, so asserting through one would test the schema without the
/// backstop). Starts a Testcontainers Postgres by default; when the environment variable
/// <see cref="AdminConnectionVariable"/> holds the connection string of an existing Postgres
/// superuser (a CI service container, a local install), a fresh throwaway database is created on
/// that server instead, so the tests run where Docker is not available.
/// </summary>
public sealed class ChatPostgres : IAsyncDisposable
{
    public const string AdminConnectionVariable = "RAFFA_TEST_POSTGRES_ADMIN";

    private const string AppRoleName = "raffa_chat_app";
    private const string AppRolePassword = "raffa_chat_app_test_password";

    private PostgreSqlContainer? _container;
    private string? _adminServerConnection;
    private string? _databaseName;

    /// <summary>Connection string of the unprivileged application role.</summary>
    public string AppConnectionString { get; private set; } = string.Empty;

    public static async Task<ChatPostgres> StartAsync()
    {
        var database = new ChatPostgres();
        await database.InitializeAsync().ConfigureAwait(false);
        return database;
    }

    public ChatDbContext CreateDbContext(ITenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<ChatDbContext>();
        ChatDbContextOptions.Configure(options, AppConnectionString, tenantContext);
        return new ChatDbContext(options.Options);
    }

    private async Task InitializeAsync()
    {
        string adminConnection;
        var external = Environment.GetEnvironmentVariable(AdminConnectionVariable);
        if (string.IsNullOrWhiteSpace(external))
        {
            _container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();
            await _container.StartAsync().ConfigureAwait(false);
            adminConnection = _container.GetConnectionString();
        }
        else
        {
            _adminServerConnection = external;
            _databaseName = "chat_test_" + Guid.NewGuid().ToString("N");
            await using (var server = new NpgsqlConnection(external))
            {
                await server.OpenAsync().ConfigureAwait(false);
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\" TEMPLATE template0", server);
                await create.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            adminConnection = new NpgsqlConnectionStringBuilder(external) { Database = _databaseName }.ConnectionString;
        }

        var adminOptions = new DbContextOptionsBuilder<ChatDbContext>();
        ChatDbContextOptions.Configure(adminOptions, adminConnection);
        await using (var adminDb = new ChatDbContext(adminOptions.Options))
        {
            await adminDb.Database.MigrateAsync().ConfigureAwait(false);

            await adminDb.Database.ExecuteSqlRawAsync(
                $"""
                DO $$ BEGIN
                  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRoleName}') THEN
                    CREATE ROLE {AppRoleName} LOGIN PASSWORD '{AppRolePassword}' NOSUPERUSER NOBYPASSRLS;
                  END IF;
                END $$;
                GRANT USAGE ON SCHEMA public TO {AppRoleName};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRoleName};
                """).ConfigureAwait(false);
        }

        AppConnectionString = new NpgsqlConnectionStringBuilder(adminConnection)
        {
            Username = AppRoleName,
            Password = AppRolePassword,
        }.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }

        if (_adminServerConnection is not null && _databaseName is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await using var server = new NpgsqlConnection(_adminServerConnection);
            await server.OpenAsync().ConfigureAwait(false);
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", server);
            await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }
}
