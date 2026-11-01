using Raffa.Documents.Contracts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Documents.Contracts.Infrastructure.Configurations;

public sealed class ContractPointDecisionConfiguration : IEntityTypeConfiguration<ContractPointDecision>
{
    public void Configure(EntityTypeBuilder<ContractPointDecision> builder)
    {
        builder.ToTable("contract_point_decision");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasConversion(ValueConverters.EntityIdConverter).ValueGeneratedNever();
        builder.Property(e => e.TenantId).HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(e => e.ContractId).HasConversion(ValueConverters.EntityIdConverter);
        builder.Property(e => e.PointKey).HasMaxLength(120);
        builder.Property(e => e.Decision).HasMaxLength(20);
        builder.Property(e => e.DecidedBy).HasMaxLength(200);

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => new { e.TenantId, e.ContractId, e.PointKey }).IsUnique();

        builder.HasOne<Contract>().WithMany().HasForeignKey(e => e.ContractId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ContractTermVersionConfiguration : IEntityTypeConfiguration<ContractTermVersion>
{
    public void Configure(EntityTypeBuilder<ContractTermVersion> builder)
    {
        builder.ToTable("contract_term_version");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasConversion(ValueConverters.EntityIdConverter).ValueGeneratedNever();
        builder.Property(e => e.TenantId).HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(e => e.ContractId).HasConversion(ValueConverters.EntityIdConverter);
        builder.Property(e => e.SourceDocumentId).HasConversion(ValueConverters.NullableEntityIdConverter);
        builder.Property(e => e.Currency).HasMaxLength(3);
        builder.Property(e => e.AnnualValue).HasPrecision(18, 2);
        builder.Property(e => e.UpliftPct).HasPrecision(9, 4);

        builder.HasIndex(e => e.TenantId);
        // At most one current version per contract.
        builder.HasIndex(e => new { e.TenantId, e.ContractId }, "ux_contract_term_version_current")
            .IsUnique()
            .HasFilter("is_current");

        builder.HasOne<Contract>().WithMany().HasForeignKey(e => e.ContractId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DocumentFieldDecisionConfiguration : IEntityTypeConfiguration<DocumentFieldDecision>
{
    public void Configure(EntityTypeBuilder<DocumentFieldDecision> builder)
    {
        builder.ToTable("document_field_decision");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasConversion(ValueConverters.EntityIdConverter).ValueGeneratedNever();
        builder.Property(e => e.TenantId).HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(e => e.DocumentId).HasConversion(ValueConverters.EntityIdConverter);
        builder.Property(e => e.FieldKey).HasMaxLength(120);
        builder.Property(e => e.Decision).HasMaxLength(20);
        builder.Property(e => e.Value).HasMaxLength(1000);
        builder.Property(e => e.DecidedBy).HasMaxLength(200);

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => new { e.TenantId, e.DocumentId, e.FieldKey }).IsUnique();

        builder.HasOne<Document>().WithMany().HasForeignKey(e => e.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}
