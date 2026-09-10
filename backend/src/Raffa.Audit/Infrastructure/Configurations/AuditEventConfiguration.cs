using Raffa.Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Audit.Infrastructure.Configurations;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_event");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);

        builder.Property(e => e.Actor).HasMaxLength(200);
        builder.Property(e => e.Action).HasMaxLength(100);
        builder.Property(e => e.ResourceType).HasMaxLength(100);
        builder.Property(e => e.ResourceId).HasMaxLength(200);
        // Detail: deliberately no HasMaxLength -> Postgres `text` (unbounded free-form context).

        builder.HasIndex(e => e.TenantId);

        // Task E01/F06/US02/T02 (audit-query, GET /api/audit) reads a tenant's events ordered by
        // recency; RLS already restricts the row set to the caller's own tenant, so the leading
        // column here only needs to add the ordering/pagination predicate on top of that.
        builder.HasIndex(e => new { e.TenantId, e.OccurredAt });
    }
}
