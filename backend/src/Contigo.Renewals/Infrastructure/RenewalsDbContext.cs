using Contigo.Renewals.Domain;
using Contigo.Renewals.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Contigo.Renewals.Infrastructure;

/// <summary>
/// EF Core DbContext for the Renewals bounded context (ADR-003; task E03/F03/US01/T02,
/// renewal-action — this module's first <c>DbContext</c>; see
/// <c>Contigo.Renewals.Application.RenewalOpportunity</c>'s own doc comment, which named this gap
/// before this task closed it). Postgres via npgsql is the only access path; schema changes flow
/// through code-first migrations only (no hand-edited DDL). RLS policies and the ambient
/// per-request tenant claim are wired the same way <c>Contigo.Audit.Infrastructure.AuditDbContext</c>
/// and <c>Contigo.Documents.Contracts.Infrastructure.DocumentsContractsDbContext</c> already wire
/// them — this context only shapes the model and exposes the DbSet.
///
/// Task E03/F02/US01/T02 (renewal-alerts) adds this module's second table,
/// <see cref="RenewalAlert"/> — the persisted alert row <c>RenewalThresholdScheduler</c>'s own doc
/// comment named as parent story task-02's job ("de-duplicating which alerts already exist for a
/// threshold").
/// </summary>
public sealed class RenewalsDbContext(DbContextOptions<RenewalsDbContext> options) : DbContext(options)
{
    public DbSet<RenewalAction> RenewalActions => Set<RenewalAction>();

    public DbSet<RenewalAlert> RenewalAlerts => Set<RenewalAlert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new RenewalActionConfiguration());
        modelBuilder.ApplyConfiguration(new RenewalAlertConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
