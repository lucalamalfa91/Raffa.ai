using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Testcontainers.PostgreSql;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// Proves the Definition of Done for task E01/F04/US02/T01 (us-02-relational-store, AC-1/AC-2):
/// `dotnet ef migrations add` + `database update` succeed against a real Postgres instance, and
/// the pgvector `vector` column is genuinely usable — not just declared in the model.
///
/// Spins up its own disposable Postgres+pgvector container per test run (Testcontainers), so
/// this test needs nothing but a running Docker daemon; no shared/external database to stand up
/// or configure by hand.
/// </summary>
public sealed class DocumentsContractsMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private DocumentsContractsDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString());
        return new DocumentsContractsDbContext(optionsBuilder.Options);
    }

    [Fact]
    public async Task Migrate_applies_the_code_first_schema_against_a_real_postgres()
    {
        await using var db = CreateContext();

        // AC-2: code-first migrations are the only schema path. This must succeed with no
        // hand-edited DDL involved.
        await db.Database.MigrateAsync();

        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
        Assert.Contains(appliedMigrations, id => id.EndsWith("_Initial", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Embedding_vector_column_round_trips_a_real_vector_through_ef_core()
    {
        await using (var db = CreateContext())
        {
            await db.Database.MigrateAsync();
        }

        var original = new float[Embedding.VectorDimensions];
        for (var i = 0; i < original.Length; i++)
        {
            original[i] = i * 0.001f;
        }

        var embedding = new Embedding
        {
            TenantId = TenantId.New(),
            SourceType = nameof(Document),
            SourceId = EntityId.New(),
            ChunkText = "sample chunk text for embedding round-trip",
            Vector = new Vector(original),
            Model = "text-embedding-3-small",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await using (var writeDb = CreateContext())
        {
            writeDb.Embeddings.Add(embedding);
            await writeDb.SaveChangesAsync();
        }

        // Fresh context/connection: this reads back from Postgres, not the change tracker.
        await using var readDb = CreateContext();
        var stored = await readDb.Embeddings.SingleAsync(e => e.Id == embedding.Id);

        Assert.Equal(Embedding.VectorDimensions, stored.Vector.ToArray().Length);
        Assert.Equal(original, stored.Vector.ToArray());

        // AC-1/ADR-003: the `vector` column is usable, i.e. pgvector's own distance operator
        // works against the stored value, not just plain storage/retrieval of the raw bytes.
        // Executed as a parameterised raw SQL command over EF Core's own connection, so this
        // exercises the exact same Npgsql + UseVector() wiring the application uses
        // (DocumentsContractsDbContextOptions), not an out-of-band client.
        await readDb.Database.OpenConnectionAsync();
        try
        {
            var command = readDb.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT (vector <-> vector) FROM embedding WHERE id = @id";
            var idParameter = command.CreateParameter();
            idParameter.ParameterName = "id";
            idParameter.Value = embedding.Id.Value;
            command.Parameters.Add(idParameter);

            var selfDistance = Convert.ToDouble(await command.ExecuteScalarAsync());
            Assert.Equal(0.0, selfDistance, precision: 6);
        }
        finally
        {
            await readDb.Database.CloseConnectionAsync();
        }
    }
}
