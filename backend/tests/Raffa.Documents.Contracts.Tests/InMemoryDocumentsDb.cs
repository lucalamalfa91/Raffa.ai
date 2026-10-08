using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// A <see cref="DocumentsContractsDbContext"/> over the EF InMemory provider, for tests that prove
/// application logic without Docker. The one thing InMemory cannot map is the pgvector
/// <see cref="Embedding.Vector"/> column, so that single property is left out of the model here; no
/// test built on this helper reads or writes embeddings. Everything else -- every entity, key, index
/// and value converter -- is the production model.
/// </summary>
internal static class InMemoryDocumentsDb
{
    public static DocumentsContractsDbContext Create(string databaseName) =>
        new(new DbContextOptionsBuilder<DocumentsContractsDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ReplaceService<IModelCustomizer, IgnoreVectorModelCustomizer>()
            .Options);

    private sealed class IgnoreVectorModelCustomizer(ModelCustomizerDependencies dependencies)
        : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.Entity<Embedding>().Ignore(e => e.Vector);
        }
    }
}
