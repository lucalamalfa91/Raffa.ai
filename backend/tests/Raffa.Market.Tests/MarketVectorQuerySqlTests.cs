using Raffa.Market.Infrastructure;
using Raffa.Market.Infrastructure.Entities;
using Raffa.Market.Retrieval;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Raffa.Market.Tests;

/// <summary>
/// F7-T01 acceptance, checked without a database: the SQL the vector search generates is a bounded
/// <c>ORDER BY distance LIMIT n</c> over <c>market_embedding</c> -- not a materialization of the whole
/// table -- and the model declares the HNSW cosine index that serves it. EF Core builds the SQL
/// (<c>ToQueryString</c>) without opening a connection, so this runs anywhere; the behaviour against a
/// real Postgres+pgvector is in <c>PgVectorMarketKnowledgeRetrievalTests</c> /
/// <c>MarketMigrationScriptTests</c> (Testcontainers, need Docker).
/// </summary>
public sealed class MarketVectorQuerySqlTests
{
    private static MarketDbContext CreateOfflineContext()
    {
        var options = new DbContextOptionsBuilder<MarketDbContext>();
        MarketDbContextOptions.Configure(options, "Host=localhost;Database=offline;Username=u;Password=p");
        return new MarketDbContext(options.Options);
    }

    private static Vector QueryVector()
    {
        var values = new float[MarketEmbeddingEntity.VectorDimensions];
        values[0] = 1f;
        return new Vector(values);
    }

    [Fact]
    public void The_vector_query_orders_by_cosine_distance_and_is_bounded_by_a_limit()
    {
        using var db = CreateOfflineContext();

        var sql = PgVectorMarketKnowledgeRetrieval.NearestNeighbours(db, QueryVector(), limit: 25).ToQueryString();

        Assert.Contains("<=>", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void The_limit_sits_on_market_embedding_before_the_join_so_the_hnsw_index_can_serve_it()
    {
        using var db = CreateOfflineContext();

        var sql = PgVectorMarketKnowledgeRetrieval.NearestNeighbours(db, QueryVector(), limit: 25).ToQueryString();

        // Subquery shape: (SELECT ... FROM market_embedding ORDER BY vector <=> q LIMIT n) JOIN market_record.
        var limitAt = sql.IndexOf("LIMIT", StringComparison.Ordinal);
        var joinAt = sql.IndexOf("JOIN", StringComparison.Ordinal);
        var recordAt = sql.IndexOf("market_record", StringComparison.Ordinal);
        Assert.True(limitAt >= 0 && joinAt >= 0 && recordAt >= 0, sql);
        Assert.True(limitAt < joinAt, "LIMIT must be applied to market_embedding before joining market_record:\n" + sql);
        Assert.True(joinAt < recordAt, sql);
    }

    [Fact]
    public void Restricting_to_one_embedding_identity_filters_on_model_and_the_fixture_flag()
    {
        using var db = CreateOfflineContext();

        var sql = PgVectorMarketKnowledgeRetrieval
            .NearestNeighbours(db, QueryVector(), limit: 10, new MarketEmbeddingIdentity("text-embedding-3-small", IsFixture: false))
            .ToQueryString();

        Assert.Contains("model", sql, StringComparison.Ordinal);
        Assert.Contains("is_fixture", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void The_model_declares_an_hnsw_cosine_index_on_the_vector_column()
    {
        using var db = CreateOfflineContext();

        // The design-time model: the index method / operator class are migration annotations, which the
        // slimmed runtime model does not keep.
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(MarketEmbeddingEntity))!;
        var index = Assert.Single(
            entity.GetIndexes(),
            i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(MarketEmbeddingEntity.Vector));

        Assert.Equal("hnsw", index.GetMethod());
        Assert.Equal(["vector_cosine_ops"], index.GetOperators());
    }

    [Fact]
    public void The_fixture_flag_is_a_nullable_column_so_old_rows_stay_unmarked()
    {
        using var db = CreateOfflineContext();

        var property = db.Model.FindEntityType(typeof(MarketEmbeddingEntity))!
            .FindProperty(nameof(MarketEmbeddingEntity.IsFixture))!;

        Assert.True(property.IsNullable);
        Assert.Equal("is_fixture", property.GetColumnName());
    }

    [Fact]
    public void The_checked_in_sql_script_creates_the_hnsw_index_and_the_flag_idempotently()
    {
        var script = File.ReadAllText(ScriptPath());

        Assert.Contains("USING hnsw (vector vector_cosine_ops)", script, StringComparison.Ordinal);
        Assert.Contains("ADD is_fixture boolean", script, StringComparison.Ordinal);

        // Both statements sit inside the migration-history guard that makes the script re-runnable.
        const string guard = "IF NOT EXISTS(SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"migration_id\" = '20261008152734_AddEmbeddingFixtureFlagAndHnswIndex')";
        Assert.True(script.Split(guard).Length - 1 >= 3, "index, column and history row must all be guarded");
    }

    private static string ScriptPath([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(here)!, "..", "..", "src", "Raffa.Market", "Migrations", "Scripts", "market.sql"));
}
