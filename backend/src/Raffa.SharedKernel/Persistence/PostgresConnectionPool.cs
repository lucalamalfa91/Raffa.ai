using System.Data.Common;
using System.Globalization;

namespace Raffa.SharedKernel.Persistence;

/// <summary>
/// The one per-process ceiling on every module's Npgsql connection pool, and the two idle-connection
/// settings that return pooled connections to the server sooner than Npgsql's own defaults.
///
/// <para>
/// <b>Why one shared bound.</b> <c>psql-raffa-dev</c> and <c>psql-raffa-demo</c> are Burstable
/// <c>Standard_B1ms</c> Flexible Servers: <c>max_connections = 50</c>, server-wide, shared by every
/// module's own <c>DbContext</c> in every replica of both hosts (API <c>max_replicas = 3</c>, Worker
/// 3 on demo / 5 on dev). Npgsql pools per process and per distinct connection string, and its own
/// default is 100 connections per pool. The Documents/Contracts module bounded <em>its</em> pool to 8
/// on 2026-09-15 after a live <c>53300 too_many_connections</c> outage on dev; the other eight
/// modules kept the default, so every screen that fans requests out in parallel (Renewals: one
/// priority read per row; Contract 360: five reads at once, re-polled every 2 s while a contract is
/// still being prepared) could still open dozens of connections per replica, keep them idle for five
/// minutes (Npgsql's <c>Connection Idle Lifetime</c> default), and starve every other process on the
/// same server. Postgres then refuses the next connection outright and the request reads as an
/// unexplained 500 -- on whichever endpoint happened to ask next, which is why demo showed 500s
/// across the whole API and most of all on those two screens.
/// </para>
///
/// <para>
/// <b>What this does.</b> <see cref="Bound"/> clamps a connection string's pool to
/// <see cref="MaxPoolSizeCeiling"/> (keeping a smaller value the host already set) and, unless the
/// string says otherwise, closes connections idle for <see cref="ConnectionIdleLifetimeSeconds"/>,
/// checked every <see cref="ConnectionPruningIntervalSeconds"/>. Every module's <c>*DbContextOptions.Configure</c>
/// routes its connection string through it, so a config edit in any environment can never silently
/// reopen the ceiling. <see cref="WithBudget"/> is the host's own, smaller per-process budget
/// (<c>Postgres:MaxPoolSize</c>, default <see cref="DefaultMaxPoolSize"/>) applied once to every
/// string the host reads from configuration. Once its pool is full a process waits its turn for a
/// connection (Npgsql's <c>Timeout</c>, 15 s by default) instead of being refused by the server --
/// the gradual backpressure a fixed server ceiling needs.
/// </para>
///
/// <para>
/// <b>The arithmetic.</b> A process runs two pools at most: one for the plugin-free modules, which
/// share the same connection string and therefore one pool, and one for the two pgvector modules
/// (Documents/Contracts, Market). With the default budget of <see cref="DefaultMaxPoolSize"/> per pool
/// that is 8 connections per process: demo's 3 API + 3 Worker replicas reach 48 only if every pool
/// in every replica is saturated at the same instant; the two that remain are for an operator's own
/// session and CI's schema apply. Raising the server's own ceiling (a bigger SKU, or the
/// <c>max_connections</c> server parameter) is the durable lever and belongs in Terraform; this is
/// the code-side guarantee that no single screen can take the whole server down in the meantime.
/// </para>
///
/// <para>
/// Provider-agnostic on purpose (SharedKernel carries no Npgsql reference): the keys are the
/// documented Npgsql keywords, edited through <see cref="DbConnectionStringBuilder"/>, and every
/// other key in the string passes through untouched.
/// </para>
/// </summary>
public static class PostgresConnectionPool
{
    /// <summary>The hard per-pool, per-process ceiling no configuration can exceed.</summary>
    public const int MaxPoolSizeCeiling = 8;

    /// <summary>The per-pool budget a host applies when <c>Postgres:MaxPoolSize</c> is not configured.</summary>
    public const int DefaultMaxPoolSize = 4;

    /// <summary>Seconds an idle pooled connection is kept before it is closed (Npgsql default: 300).</summary>
    public const int ConnectionIdleLifetimeSeconds = 60;

    /// <summary>Seconds between two idle-connection sweeps (Npgsql default: 10).</summary>
    public const int ConnectionPruningIntervalSeconds = 5;

    /// <summary>The configuration key (<c>Postgres__MaxPoolSize</c> as an environment variable) a host reads its budget from.</summary>
    public const string MaxPoolSizeConfigurationKey = "Postgres:MaxPoolSize";

    // Npgsql 10.0.3 parses "Maximum Pool Size" and "MaxPoolSize" but rejects the spaced
    // "Max Pool Size" spelling outright ("Couldn't set max pool size"), so that one is only ever
    // removed here, never written.
    private const string MaxPoolSizeKey = "Maximum Pool Size";
    private static readonly string[] MaxPoolSizeKeys = [MaxPoolSizeKey, "MaxPoolSize", "Max Pool Size"];

    private const string IdleLifetimeKey = "Connection Idle Lifetime";
    private static readonly string[] IdleLifetimeKeys = [IdleLifetimeKey, "ConnectionIdleLifetime"];

    private const string PruningIntervalKey = "Connection Pruning Interval";
    private static readonly string[] PruningIntervalKeys = [PruningIntervalKey, "ConnectionPruningInterval"];

    /// <summary>
    /// Module side: the string with its pool clamped to <see cref="MaxPoolSizeCeiling"/> (a smaller,
    /// positive value already in the string is kept; none, zero or larger becomes the ceiling) and
    /// the idle-lifetime / pruning-interval defaults filled in where the string is silent.
    /// </summary>
    public static string Bound(string connectionString)
    {
        ArgumentNullException.ThrowIfNull(connectionString);

        var builder = Parse(connectionString);

        var requested = ReadInt(builder, MaxPoolSizeKeys);
        var bounded = requested is > 0 ? Math.Min(requested.Value, MaxPoolSizeCeiling) : MaxPoolSizeCeiling;

        // Every key is (re)written through the indexer, in its canonical spelling, so the result is
        // the same string whether it is bounded once or again (DbConnectionStringBuilder lower-cases
        // the keys it merely parses back).
        Set(builder, MaxPoolSizeKey, MaxPoolSizeKeys, bounded);
        Set(builder, IdleLifetimeKey, IdleLifetimeKeys, ReadInt(builder, IdleLifetimeKeys) ?? ConnectionIdleLifetimeSeconds);
        Set(builder, PruningIntervalKey, PruningIntervalKeys, ReadInt(builder, PruningIntervalKeys) ?? ConnectionPruningIntervalSeconds);

        return builder.ConnectionString;
    }

    /// <summary>
    /// Host side: the string carrying this process's own per-pool budget, itself clamped to
    /// <see cref="MaxPoolSizeCeiling"/>, plus everything <see cref="Bound"/> adds.
    /// </summary>
    public static string WithBudget(string connectionString, int maxPoolSize)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPoolSize, 1);

        var builder = Parse(connectionString);
        Set(builder, MaxPoolSizeKey, MaxPoolSizeKeys, Math.Min(maxPoolSize, MaxPoolSizeCeiling));
        return Bound(builder.ConnectionString);
    }

    /// <summary>The pool size the string carries under any of Npgsql's spellings, or null when it carries none.</summary>
    public static int? MaxPoolSizeOf(string connectionString)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        return ReadInt(Parse(connectionString), MaxPoolSizeKeys);
    }

    private static DbConnectionStringBuilder Parse(string connectionString) =>
        new() { ConnectionString = connectionString };

    private static int? ReadInt(DbConnectionStringBuilder builder, string[] keys)
    {
        foreach (var key in keys)
        {
            if (builder.TryGetValue(key, out var raw)
                && int.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static void Set(DbConnectionStringBuilder builder, string canonicalKey, string[] keys, int value)
    {
        foreach (var key in keys)
        {
            builder.Remove(key);
        }

        builder[canonicalKey] = value.ToString(CultureInfo.InvariantCulture);
    }
}
