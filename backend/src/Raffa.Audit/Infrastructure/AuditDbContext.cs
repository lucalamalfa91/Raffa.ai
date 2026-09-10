using Raffa.Audit.Domain;
using Raffa.Audit.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Audit.Infrastructure;

/// <summary>
/// EF Core DbContext for the Audit bounded context (ADR-003). Postgres via npgsql is the only
/// access path; schema changes flow through code-first migrations only (no hand-edited DDL). RLS
/// policies and the ambient per-request tenant claim are wired the same way task E01/F04/US03/T01
/// wired them for Documents/Contracts and task E01/F05/US01/T01 wired them for Identity/Workspace
/// — this context only shapes the model and exposes the DbSet.
/// </summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AuditEventConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
