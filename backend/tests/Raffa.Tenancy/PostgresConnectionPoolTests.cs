using Npgsql;
using Raffa.SharedKernel.Persistence;

namespace Raffa.Tenancy.Tests;

/// <summary>
/// <see cref="PostgresConnectionPool"/> is the one bound between nine module pools, two hosts and a
/// 50-connection <c>Standard_B1ms</c> server (its own doc comment). These pin the three things a
/// regression would silently undo: the ceiling holds whatever a connection string says, a host's
/// smaller budget survives the module-side bound, and the result is still a connection string
/// Npgsql itself parses with every other key intact.
/// </summary>
public sealed class PostgresConnectionPoolTests
{
    // The password carries a `;` and a `=` inside ADO.NET quotes: the bound must re-serialise it intact.
    private const string Raw = "Host=psql-raffa-demo.postgres.database.azure.com;Port=5432;Database=raffa_demo;Username=raffa;Password='p;w=d';Ssl Mode=Require";

    [Fact]
    public void Bound_caps_an_unbounded_string_at_the_ceiling_and_fills_the_idle_settings()
    {
        var parsed = new NpgsqlConnectionStringBuilder(PostgresConnectionPool.Bound(Raw));

        Assert.Equal(PostgresConnectionPool.MaxPoolSizeCeiling, parsed.MaxPoolSize);
        Assert.Equal(PostgresConnectionPool.ConnectionIdleLifetimeSeconds, parsed.ConnectionIdleLifetime);
        Assert.Equal(PostgresConnectionPool.ConnectionPruningIntervalSeconds, parsed.ConnectionPruningInterval);
        Assert.Equal("psql-raffa-demo.postgres.database.azure.com", parsed.Host);
        Assert.Equal("raffa_demo", parsed.Database);
        Assert.Equal("p;w=d", parsed.Password);
        Assert.Equal(SslMode.Require, parsed.SslMode);
    }

    [Theory]
    [InlineData("Maximum Pool Size=100")]
    [InlineData("MaxPoolSize=100")]
    [InlineData("Max Pool Size=100")] // the one spelling Npgsql 10 itself refuses to parse: removed, never re-emitted
    [InlineData("Maximum Pool Size=0")]
    public void Bound_clamps_a_larger_or_zero_pool_size_under_any_of_npgsqls_spellings(string poolSetting)
    {
        var bounded = PostgresConnectionPool.Bound($"{Raw};{poolSetting}");

        Assert.Equal(PostgresConnectionPool.MaxPoolSizeCeiling, new NpgsqlConnectionStringBuilder(bounded).MaxPoolSize);
        Assert.Equal(PostgresConnectionPool.MaxPoolSizeCeiling, PostgresConnectionPool.MaxPoolSizeOf(bounded));
    }

    [Fact]
    public void Bound_keeps_a_smaller_pool_size_the_host_already_set()
    {
        var bounded = PostgresConnectionPool.Bound($"{Raw};Maximum Pool Size=3");

        Assert.Equal(3, new NpgsqlConnectionStringBuilder(bounded).MaxPoolSize);
    }

    [Fact]
    public void Bound_keeps_idle_settings_the_string_already_carries()
    {
        var bounded = PostgresConnectionPool.Bound($"{Raw};Connection Idle Lifetime=120;Connection Pruning Interval=20");
        var parsed = new NpgsqlConnectionStringBuilder(bounded);

        Assert.Equal(120, parsed.ConnectionIdleLifetime);
        Assert.Equal(20, parsed.ConnectionPruningInterval);
    }

    [Fact]
    public void Bound_is_idempotent()
    {
        var once = PostgresConnectionPool.Bound(Raw);

        Assert.Equal(once, PostgresConnectionPool.Bound(once));
    }

    [Fact]
    public void WithBudget_sets_the_hosts_budget_and_the_module_bound_keeps_it()
    {
        var budgeted = PostgresConnectionPool.WithBudget($"{Raw};Maximum Pool Size=100", PostgresConnectionPool.DefaultMaxPoolSize);

        Assert.Equal(PostgresConnectionPool.DefaultMaxPoolSize, new NpgsqlConnectionStringBuilder(budgeted).MaxPoolSize);
        Assert.Equal(PostgresConnectionPool.DefaultMaxPoolSize, new NpgsqlConnectionStringBuilder(PostgresConnectionPool.Bound(budgeted)).MaxPoolSize);
    }

    [Fact]
    public void WithBudget_never_exceeds_the_ceiling_and_rejects_a_budget_below_one()
    {
        var budgeted = PostgresConnectionPool.WithBudget(Raw, 1000);

        Assert.Equal(PostgresConnectionPool.MaxPoolSizeCeiling, new NpgsqlConnectionStringBuilder(budgeted).MaxPoolSize);
        Assert.Throws<ArgumentOutOfRangeException>(() => PostgresConnectionPool.WithBudget(Raw, 0));
    }

    [Fact]
    public void MaxPoolSizeOf_reads_nothing_from_a_string_that_sets_none()
    {
        Assert.Null(PostgresConnectionPool.MaxPoolSizeOf(Raw));
    }

    [Fact]
    public void The_default_budget_fits_the_demo_server()
    {
        // 3 API + 3 Worker replicas, two pools each (plugin-free modules; the two pgvector modules),
        // must leave headroom under max_connections = 50 for an operator session and CI's schema apply.
        const int demoReplicas = 3 + 3;
        const int poolsPerProcess = 2;
        const int serverMaxConnections = 50;

        Assert.True(demoReplicas * poolsPerProcess * PostgresConnectionPool.DefaultMaxPoolSize < serverMaxConnections);
        Assert.True(PostgresConnectionPool.DefaultMaxPoolSize <= PostgresConnectionPool.MaxPoolSizeCeiling);
    }
}
