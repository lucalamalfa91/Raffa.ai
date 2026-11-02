using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Raffa.Market.Infrastructure;
using Raffa.Market.Network;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Market;
using Testcontainers.PostgreSql;

namespace Raffa.Market.Tests.Network;

/// <summary>The invisible flow against a real Postgres: a reply is recorded once, anonymously, and audited in the contributing workspace.</summary>
public sealed class MarketObservationRecorderDbTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private PostgreSqlContainer? _container;
    private string _connection = string.Empty;
    private string? _database;
    private string? _server;

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("RAFFA_TEST_POSTGRES_ADMIN");
        if (string.IsNullOrWhiteSpace(external))
        {
            _container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();
            await _container.StartAsync();
            _connection = _container.GetConnectionString();
        }
        else
        {
            _server = external;
            _database = "market_obs_" + Guid.NewGuid().ToString("N");
            await using var server = new NpgsqlConnection(external);
            await server.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\" TEMPLATE template0", server);
            await create.ExecuteNonQueryAsync();
            _connection = new NpgsqlConnectionStringBuilder(external) { Database = _database }.ConnectionString;
        }

        await using var db = Create();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
        else if (_database is not null && _server is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await using var server = new NpgsqlConnection(_server);
            await server.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", server);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private MarketDbContext Create(string? connection = null)
    {
        var options = new DbContextOptionsBuilder<MarketDbContext>();
        MarketDbContextOptions.Configure(options, connection ?? _connection);
        return new MarketDbContext(options.Options);
    }

    private sealed class Factory(MarketDbContextFactoryBridge bridge) : IDbContextFactory<MarketDbContext>
    {
        public MarketDbContext CreateDbContext() => bridge.Create();
    }

    private sealed class MarketDbContextFactoryBridge(Func<MarketDbContext> create)
    {
        public MarketDbContext Create() => create();
    }

    private sealed class RecordingAudit : IAuditWriter
    {
        public List<AuditEntry> Written { get; } = [];

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Written.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private static SupplierReplyObservation Reply(decimal price = 47) =>
        new("DocuSign", "e-signature", "CH", "CHF", "eSignature Business Pro", "ESIG-1", "seat / month", price, 24, 10, 3, 60, "quarterly in advance", Now);

    [Fact]
    public async Task A_reply_is_recorded_once_anonymously_and_audited_in_the_contributing_workspace()
    {
        var audit = new RecordingAudit();
        var recorder = new MarketObservationRecorder(new Factory(new MarketDbContextFactoryBridge(() => Create())), audit, new FixedClock(), new MarketObservationOptions());
        var tenant = TenantId.New();

        await recorder.RecordAsync(tenant, "marta", "case-1:1", Reply());
        await recorder.RecordAsync(tenant, "marta", "case-1:1", Reply()); // a replay: the same reply

        await using var db = Create();
        var row = Assert.Single(await db.MarketObservations.ToListAsync());
        Assert.Equal("docusign", row.SupplierKey);
        Assert.Equal(47m, row.UnitPrice);
        Assert.DoesNotContain(tenant.Value.ToString(), row.WorkspaceKey + row.ObservationKey, StringComparison.OrdinalIgnoreCase);
        var entry = Assert.Single(audit.Written);
        Assert.Equal(tenant, entry.TenantId);
        Assert.Equal(MarketObservationRecorder.RecordedAction, entry.Action);
        Assert.Equal(row.Id.ToString(), entry.ResourceId);
        Assert.Contains("anonymised", entry.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_api_role_that_can_only_insert_still_records_once_and_cannot_read_the_table()
    {
        // The production grant: raffa_app may INSERT into market_observation and nothing else on it.
        await using (var admin = Create())
        {
            await admin.Database.ExecuteSqlRawAsync(
                """
                DO $$ BEGIN
                  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'raffa_obs_api') THEN
                    CREATE ROLE raffa_obs_api LOGIN PASSWORD 'raffa_obs_api_pw' NOSUPERUSER;
                  END IF;
                END $$;
                GRANT USAGE ON SCHEMA public TO raffa_obs_api;
                GRANT INSERT ON market_observation TO raffa_obs_api;
                """);
        }

        var apiConnection = new NpgsqlConnectionStringBuilder(_connection) { Username = "raffa_obs_api", Password = "raffa_obs_api_pw" }.ConnectionString;
        var audit = new RecordingAudit();
        var recorder = new MarketObservationRecorder(new Factory(new MarketDbContextFactoryBridge(() => Create(apiConnection))), audit, new FixedClock(), new MarketObservationOptions());
        var tenant = TenantId.New();

        await recorder.RecordAsync(tenant, "marta", "case-9:1", Reply());
        await recorder.RecordAsync(tenant, "marta", "case-9:1", Reply());

        await using var db = Create();
        Assert.Single(await db.MarketObservations.ToListAsync());
        Assert.Single(audit.Written);
        await using var asApi = Create(apiConnection);
        await Assert.ThrowsAnyAsync<Exception>(() => asApi.MarketObservations.ToListAsync());
    }

    [Fact]
    public async Task Disabled_or_empty_replies_record_nothing_and_never_throw()
    {
        var audit = new RecordingAudit();
        var factory = new Factory(new MarketDbContextFactoryBridge(() => Create()));
        var off = new MarketObservationRecorder(factory, audit, new FixedClock(), new MarketObservationOptions { Enabled = false });
        var on = new MarketObservationRecorder(factory, audit, new FixedClock(), new MarketObservationOptions());

        await off.RecordAsync(TenantId.New(), "marta", "c:1", Reply());
        await on.RecordAsync(TenantId.New(), "marta", "c:2", new SupplierReplyObservation("Acme", null, null, null, null, null, null, null, null, null, null, null, null, Now));

        await using var db = Create();
        Assert.Empty(await db.MarketObservations.ToListAsync());
        Assert.Empty(audit.Written);
    }

    [Fact]
    public async Task Three_workspaces_make_the_network_publish_a_deal_and_two_do_not()
    {
        var recorder = new MarketObservationRecorder(new Factory(new MarketDbContextFactoryBridge(() => Create())), new RecordingAudit(), new FixedClock(), new MarketObservationOptions());
        var network = new MarketNetworkService(new Factory(new MarketDbContextFactoryBridge(() => Create())), new MarketObservationOptions(), new FixedClock());

        await recorder.RecordAsync(TenantId.New(), "a", "c:1", Reply(40));
        await recorder.RecordAsync(TenantId.New(), "b", "c:1", Reply(44));
        var two = await network.BuildAsync();
        await recorder.RecordAsync(TenantId.New(), "c", "c:1", Reply(48));
        var three = await network.BuildAsync();

        Assert.Empty(two.Deals);
        Assert.Equal(2, two.Workspaces);
        var deal = Assert.Single(three.Deals);
        Assert.Equal(3, deal.SampleSize);
        Assert.Equal(44m, deal.UnitPriceP50);
    }
}
