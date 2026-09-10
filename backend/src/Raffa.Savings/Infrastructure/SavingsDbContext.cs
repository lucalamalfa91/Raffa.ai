using Raffa.Savings.Domain;
using Raffa.Savings.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Savings.Infrastructure;

/// <summary>
/// EF Core DbContext for the Savings bounded context (ADR-003; task E04/F02/US02/T01,
/// savings-opportunity — this module's first <c>DbContext</c>). Postgres via npgsql is the only
/// access path; schema changes flow through code-first migrations only (no hand-edited DDL). RLS
/// policies and the ambient per-request tenant claim are wired the same way
/// <c>Raffa.Renewals.Infrastructure.RenewalsDbContext</c> /
/// <c>Raffa.Audit.Infrastructure.AuditDbContext</c> already wire them — this context only shapes
/// the model and exposes the DbSet.
///
/// <see cref="RealizedSavingsRecords"/> (task E04/F02/US02/T02, realized-savings) is this module's
/// second entity, module-map.md's own "Savings | SavingsOpportunity, RealizedSavings" — see that
/// entity's own doc comment.
/// </summary>
public sealed class SavingsDbContext(DbContextOptions<SavingsDbContext> options) : DbContext(options)
{
    public DbSet<SavingsOpportunity> SavingsOpportunities => Set<SavingsOpportunity>();

    /// <summary>Named <c>RealizedSavingsRecords</c>, not <c>RealizedSavings</c> (which would collide
    /// with the entity type name itself and read oddly as a set) — mirrors the plural-noun-plus-role
    /// naming already used elsewhere in this codebase when the bare plural of the type would be
    /// awkward (e.g. <c>Raffa.Audit.Infrastructure.AuditDbContext.AuditEvents</c> for
    /// <c>AuditEvent</c>).</summary>
    public DbSet<RealizedSavings> RealizedSavingsRecords => Set<RealizedSavings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SavingsOpportunityConfiguration());
        modelBuilder.ApplyConfiguration(new RealizedSavingsConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
