using Raffa.Documents.Contracts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Documents.Contracts.Infrastructure.Configurations;

public sealed class CorrectionHistoryConfiguration : IEntityTypeConfiguration<CorrectionHistory>
{
    public void Configure(EntityTypeBuilder<CorrectionHistory> builder)
    {
        builder.ToTable("correction_history");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);
        // Deliberately no conversion tied to a single parent table — see the type's doc comment.
        builder.Property(e => e.TargetEntityId)
            .HasConversion(ValueConverters.EntityIdConverter);

        builder.Property(e => e.TargetEntityType).HasMaxLength(100);
        builder.Property(e => e.FieldName).HasMaxLength(200);
        builder.Property(e => e.CorrectedBy).HasMaxLength(200);
        builder.Property(e => e.Reason).HasMaxLength(1000);

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => new { e.TargetEntityType, e.TargetEntityId });

        // No HasOne/foreign key here by design: TargetEntityId can point at Contract, Clause,
        // Obligation, Risk, ... — a polymorphic reference no single FK constraint can express.
    }
}
