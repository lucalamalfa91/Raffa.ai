using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Infrastructure;

/// <summary>
/// EF Core DbContext for the Documents/Contracts bounded context (ADR-003). Postgres +
/// pgvector via npgsql is the only access path; schema changes flow through code-first
/// migrations only (no hand-edited DDL). RLS policies and the ambient per-request tenant claim
/// are wired by us-03 — this context only shapes the model and exposes the DbSets.
/// </summary>
public sealed class DocumentsContractsDbContext(DbContextOptions<DocumentsContractsDbContext> options)
    : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<ExtractionJob> ExtractionJobs => Set<ExtractionJob>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ContractVersion> ContractVersions => Set<ContractVersion>();
    public DbSet<ContractLineItem> ContractLineItems => Set<ContractLineItem>();
    public DbSet<Clause> Clauses => Set<Clause>();
    public DbSet<Obligation> Obligations => Set<Obligation>();
    public DbSet<Risk> Risks => Set<Risk>();
    public DbSet<CorrectionHistory> CorrectionHistories => Set<CorrectionHistory>();
    public DbSet<Embedding> Embeddings => Set<Embedding>();
    public DbSet<ExtractionEvidence> ExtractionEvidences => Set<ExtractionEvidence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Required so migrations emit `CREATE EXTENSION IF NOT EXISTS "vector"` (ADR-003).
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.ApplyConfiguration(new DocumentConfiguration());
        modelBuilder.ApplyConfiguration(new DocumentVersionConfiguration());
        modelBuilder.ApplyConfiguration(new ExtractionJobConfiguration());
        modelBuilder.ApplyConfiguration(new ContractConfiguration());
        modelBuilder.ApplyConfiguration(new ContractVersionConfiguration());
        modelBuilder.ApplyConfiguration(new ContractLineItemConfiguration());
        modelBuilder.ApplyConfiguration(new ClauseConfiguration());
        modelBuilder.ApplyConfiguration(new ObligationConfiguration());
        modelBuilder.ApplyConfiguration(new RiskConfiguration());
        modelBuilder.ApplyConfiguration(new CorrectionHistoryConfiguration());
        modelBuilder.ApplyConfiguration(new EmbeddingConfiguration());
        modelBuilder.ApplyConfiguration(new ExtractionEvidenceConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
