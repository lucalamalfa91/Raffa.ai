using Contigo.Market.Infrastructure.Configurations;
using Contigo.Market.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace Contigo.Market.Infrastructure;

/// <summary>
/// EF Core DbContext for the shared, read-only market index (ADR-003 pgvector; ADR-024 "three
/// sources, one rule" / ADR-011's epic-13 amendment: "its own vector index `market_embedding`: no
/// `tenant_id`, readable by every tenant, written only by the ingestion job, never containing
/// tenant content, never joined with tenant tables"). Task objective, verbatim: "pgvector, **no**
/// tenant interceptor".
///
/// Deliberately does **not** mirror
/// <c>Contigo.Documents.Contracts.Infrastructure.DocumentsContractsDbContext</c>'s own
/// <c>Contigo.SharedKernel.Tenancy.ITenantContext</c>/<c>TenantRlsConnectionInterceptor</c> wiring —
/// this module has no tenant-scoped entity at all (<see cref="MarketRecordEntity"/>/
/// <see cref="MarketEmbeddingEntity"/> carry no <c>tenant_id</c> column), so there is nothing for a
/// per-connection `app.tenant_id` claim to police, and no `tenant_isolation` RLS policy in
/// <c>Migrations/Scripts/market.sql</c> either. A future migration that adds a `tenant_id` column or
/// an RLS policy to either table would be a defect (parent story AC-3: "no `tenant_id`, shared,
/// read-only"), not a hardening.
/// </summary>
public sealed class MarketDbContext(DbContextOptions<MarketDbContext> options) : DbContext(options)
{
    public DbSet<MarketRecordEntity> MarketRecords => Set<MarketRecordEntity>();
    public DbSet<MarketEmbeddingEntity> MarketEmbeddings => Set<MarketEmbeddingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Required so migrations emit `CREATE EXTENSION IF NOT EXISTS "vector"` (ADR-003) -- same
        // call DocumentsContractsDbContext.OnModelCreating already makes for its own `embedding`
        // table; harmless (IF NOT EXISTS) when the extension already exists from that module's own
        // migration having run first in ADR-021's apply order.
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.ApplyConfiguration(new MarketRecordConfiguration());
        modelBuilder.ApplyConfiguration(new MarketEmbeddingConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
