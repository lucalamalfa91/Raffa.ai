using Raffa.Documents.Contracts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Raffa.Api.Tests.TestSupport;

/// <summary>
/// Runs <see cref="DocumentsContractsDbContext.OnModelCreating"/> unchanged
/// (<see cref="ModelCustomizer.Customize"/>'s own base behaviour), then strips
/// <see cref="Embedding.Vector"/> from the model. <c>Pgvector.Vector</c> (the column's CLR type,
/// wired only for the Npgsql `vector` extension — <c>EmbeddingConfiguration</c>) has no InMemory
/// -provider mapping at all; the InMemory provider throws
/// <see cref="InvalidOperationException"/> at model-validation time the moment any
/// <see cref="DocumentsContractsDbContext"/> DbSet is first touched, even one that never queries
/// <see cref="DocumentsContractsDbContext.Embeddings"/> in the same request. Safe here because
/// every scenario <see cref="InMemoryAskEngineFactory"/> serves never reaches clause retrieval —
/// the one thing that ever reads or writes an <see cref="Embedding"/> row (see that factory's own
/// doc comment) — so excluding the column changes nothing these tests assert on.
/// </summary>
internal sealed class InMemoryModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        if (modelBuilder.Model.FindEntityType(typeof(Embedding)) is not null)
        {
            modelBuilder.Entity<Embedding>().Ignore(e => e.Vector);
        }
    }
}
