using Contigo.Suppliers.Products.Domain;
using Contigo.Suppliers.Products.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Contigo.Suppliers.Products.Infrastructure;

/// <summary>
/// EF Core DbContext for the Suppliers/Products bounded context (ADR-003; task E13/F03/US01/T01,
/// ADR-024 "Supplier identity" — this module's first DbContext, turning the bare scaffold task
/// E13/F01/US01/T01 left behind into a real module). Postgres via npgsql is the only access path;
/// schema changes flow through code-first migrations only (no hand-edited DDL). RLS policies and
/// the ambient per-request tenant claim are wired the same way
/// <c>Contigo.Documents.Contracts.Infrastructure.DocumentsContractsDbContext</c> and every other
/// module's own DbContext already wire them — this context only shapes the model and exposes the
/// DbSet.
/// </summary>
public sealed class SuppliersDbContext(DbContextOptions<SuppliersDbContext> options) : DbContext(options)
{
    public DbSet<Supplier> Suppliers => Set<Supplier>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SupplierConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
