using Raffa.Documents.Contracts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Documents.Contracts.Infrastructure.Configurations;

public sealed class ContractFactsRecordConfiguration : IEntityTypeConfiguration<ContractFactsRecord>
{
    public void Configure(EntityTypeBuilder<ContractFactsRecord> builder)
    {
        builder.ToTable("contract_facts");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(e => e.ContractId)
            .HasConversion(ValueConverters.EntityIdConverter);

        builder.Property(e => e.Facts).HasColumnType("jsonb");

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => new { e.TenantId, e.ContractId }).IsUnique();

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(e => e.ContractId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
