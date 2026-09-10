using Raffa.Renewals.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Renewals.Infrastructure.Configurations;

/// <summary>
/// EF Core mapping for <see cref="RenewalAlert"/> (task E03/F02/US01/T02, <c>renewal-alerts</c>).
/// Mirrors <see cref="RenewalActionConfiguration"/>'s own shape (enum-as-string columns, module-local
/// <see cref="ValueConverters"/>, a <see cref="TenantScopedEntity"/> id/tenant pair) with one
/// addition: a <em>filtered</em> unique index (see <see cref="Configure"/>'s own comment).
/// </summary>
public sealed class RenewalAlertConfiguration : IEntityTypeConfiguration<RenewalAlert>
{
    public void Configure(EntityTypeBuilder<RenewalAlert> builder)
    {
        builder.ToTable("renewal_alert");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(e => e.ContractId)
            .HasConversion(ValueConverters.EntityIdConverter);

        // Same enum-as-string convention as RenewalActionConfiguration.Status (readable in `psql`
        // without decoding an integer) — RenewalMilestoneKind has exactly two members today
        // (RenewalDate/CancellationDeadline); 30 chars is headroom, not a real bound.
        builder.Property(e => e.Milestone).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => e.TenantId);

        // De-duplication key (RenewalAlert's own doc comment; RenewalThresholdScheduler's own doc
        // comment names this exact job as this task's to do): at most one ACTIVE alert per (tenant,
        // contract, milestone, thresholdDays) — the database-level backstop for
        // RenewalAlertService's own check-then-act de-dup, same "two independent reasons, not one"
        // belt-and-suspenders shape RenewalActionConfiguration's own unique index already documents.
        //
        // Filtered (WHERE status = 'Active'), not a plain unique index: a Resolved alert is closed
        // history (RenewalAlertStatus's own doc comment) — it must never silently block a later,
        // genuinely new Active alert from being raised again for the exact same tuple (for example
        // two corrections that first move a contract off a threshold, then back onto it). Npgsql/EF
        // Core supports a partial index via HasFilter; the raw SQL predicate must match the exact
        // string HasConversion<string>() persists (the enum member name, "Active" — not lowercased).
        builder.HasIndex(e => new { e.TenantId, e.ContractId, e.Milestone, e.ThresholdDays })
            .IsUnique()
            .HasFilter("status = 'Active'")
            .HasDatabaseName("ix_renewal_alert_active_tenant_contract_milestone_threshold");
    }
}
