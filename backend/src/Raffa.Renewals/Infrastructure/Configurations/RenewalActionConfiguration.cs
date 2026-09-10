using Raffa.Renewals.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Renewals.Infrastructure.Configurations;

public sealed class RenewalActionConfiguration : IEntityTypeConfiguration<RenewalAction>
{
    public void Configure(EntityTypeBuilder<RenewalAction> builder)
    {
        builder.ToTable("renewal_action");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(e => e.ContractId)
            .HasConversion(ValueConverters.EntityIdConverter);

        builder.Property(e => e.Owner).HasMaxLength(200);
        // Same enum-as-string convention as every other module's own closed-set columns
        // (e.g. Raffa.Documents.Contracts.Infrastructure.Configurations.RiskConfiguration's
        // `Severity`) — readable in `psql` without decoding an integer.
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        // Action: deliberately no HasMaxLength -> Postgres `text` (unbounded free-form record of
        // what Procurement actually did), same convention AuditEvent.Detail already uses for its
        // own unbounded-by-nature field.
        builder.HasIndex(e => e.TenantId);

        // At most one action row per contract per tenant (RenewalAction's own doc comment) — POST
        // /api/renewals/{id}/action upserts, it never creates a second row for the same renewal.
        // This is the database-level backstop for RenewalActionService's own check-then-act
        // upsert: two concurrent first-time POSTs for the same (tenant, contract) race the
        // SingleOrDefaultAsync-then-insert in application code, but only one INSERT can win here —
        // the loser's SaveChangesAsync throws DbUpdateException instead of silently duplicating
        // the row. RenewalActionService does not retry that race as an update; an honest 500 for
        // a genuinely concurrent first write is an accepted gap for this task's effort size, not a
        // silent correctness hole (the unique index is what prevents the hole).
        builder.HasIndex(e => new { e.TenantId, e.ContractId }).IsUnique();
    }
}
