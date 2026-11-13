using Raffa.Renewals.Domain;
using Raffa.Renewals.Domain.Cases;
using Raffa.Renewals.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Renewals.Infrastructure;

/// <summary>
/// EF Core DbContext for the Renewals bounded context (ADR-003; task E03/F03/US01/T02,
/// renewal-action — this module's first <c>DbContext</c>; see
/// <c>Raffa.Renewals.Application.RenewalOpportunity</c>'s own doc comment, which named this gap
/// before this task closed it). Postgres via npgsql is the only access path; schema changes flow
/// through code-first migrations only (no hand-edited DDL). RLS policies and the ambient
/// per-request tenant claim are wired the same way <c>Raffa.Audit.Infrastructure.AuditDbContext</c>
/// and <c>Raffa.Documents.Contracts.Infrastructure.DocumentsContractsDbContext</c> already wire
/// them — this context only shapes the model and exposes the DbSet.
///
/// Task E03/F02/US01/T02 (renewal-alerts) adds this module's second table,
/// <see cref="RenewalAlert"/> — the persisted alert row <c>RenewalThresholdScheduler</c>'s own doc
/// comment named as parent story task-02's job ("de-duplicating which alerts already exist for a
/// threshold").
///
/// Task E29/F01/US01/T01 (todo-entity-api, wave w19 NW-85) adds this module's third table,
/// <see cref="RenewalNegotiationTodo"/> — the AI-ranked negotiation TODO list Ask upserts
/// in-process, keyed by (tenant, contract, point_key); see that entity's own doc comment.
/// </summary>
public sealed class RenewalsDbContext(DbContextOptions<RenewalsDbContext> options) : DbContext(options)
{
    public DbSet<RenewalAction> RenewalActions => Set<RenewalAction>();

    public DbSet<RenewalAlert> RenewalAlerts => Set<RenewalAlert>();

    public DbSet<RenewalNegotiationTodo> RenewalNegotiationTodos => Set<RenewalNegotiationTodo>();

    // AskRaffa 4.0 renewal case (BACKEND-SPEC §2.1-2.7, 2.12).
    public DbSet<RenewalCaseRecord> RenewalCases => Set<RenewalCaseRecord>();

    public DbSet<CaseEmailRecord> CaseEmails => Set<CaseEmailRecord>();

    public DbSet<CaseDelegationRecord> CaseDelegations => Set<CaseDelegationRecord>();

    public DbSet<CaseEventRecord> CaseEvents => Set<CaseEventRecord>();

    public DbSet<CaseVendorMessageRecord> CaseVendorMessages => Set<CaseVendorMessageRecord>();

    public DbSet<CaseFileRecord> CaseFiles => Set<CaseFileRecord>();

    public DbSet<UserReminderRecord> UserReminders => Set<UserReminderRecord>();

    public DbSet<TodaySnoozeRecord> TodaySnoozes => Set<TodaySnoozeRecord>();

    public DbSet<CompanyRulesRecord> CompanyRules => Set<CompanyRulesRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new RenewalActionConfiguration());
        modelBuilder.ApplyConfiguration(new RenewalAlertConfiguration());
        modelBuilder.ApplyConfiguration(new RenewalNegotiationTodoConfiguration());
        modelBuilder.ApplyConfiguration(new RenewalCaseRecordConfiguration());
        modelBuilder.ApplyConfiguration(new CaseEmailRecordConfiguration());
        modelBuilder.ApplyConfiguration(new CaseDelegationRecordConfiguration());
        modelBuilder.ApplyConfiguration(new CaseEventRecordConfiguration());
        modelBuilder.ApplyConfiguration(new CaseFileRecordConfiguration());
        modelBuilder.ApplyConfiguration(new CaseVendorMessageRecordConfiguration());
        modelBuilder.ApplyConfiguration(new UserReminderRecordConfiguration());
        modelBuilder.ApplyConfiguration(new TodaySnoozeRecordConfiguration());
        modelBuilder.ApplyConfiguration(new CompanyRulesRecordConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
