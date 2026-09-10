using Raffa.Documents.Contracts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Documents.Contracts.Infrastructure.Configurations;

public sealed class ContractVersionConfiguration : IEntityTypeConfiguration<ContractVersion>
{
    public void Configure(EntityTypeBuilder<ContractVersion> builder)
    {
        builder.ToTable("contract_version");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(e => e.ContractId)
            .HasConversion(ValueConverters.EntityIdConverter);

        builder.Property(e => e.SnapshotJson).HasColumnType("jsonb");
        builder.Property(e => e.ChangeReason).HasMaxLength(1000);
        builder.Property(e => e.CreatedBy).HasMaxLength(200);

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => new { e.ContractId, e.VersionNumber }).IsUnique();

        // Owned history row: deleting the contract deletes its version history with it.
        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(e => e.ContractId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
