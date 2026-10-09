using Raffa.Documents.Contracts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Documents.Contracts.Infrastructure.Configurations;

public sealed class ObligationConfiguration : IEntityTypeConfiguration<Obligation>
{
    public void Configure(EntityTypeBuilder<Obligation> builder)
    {
        builder.ToTable("obligation");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(e => e.ContractId)
            .HasConversion(ValueConverters.EntityIdConverter);
        builder.Property(e => e.SourceDocumentId)
            .HasConversion(ValueConverters.NullableEntityIdConverter);

        builder.Property(e => e.Party).HasMaxLength(300);
        builder.Property(e => e.ObligationType).HasMaxLength(100);
        builder.Property(e => e.Criticality).HasMaxLength(30);
        builder.Property(e => e.Status).HasMaxLength(30);
        builder.Property(e => e.SourceSpan).HasMaxLength(500);

        // Optimistic-concurrency guard (Appendix C rule 5) — see Contract.Version.
        builder.Property(e => e.Version).HasDefaultValue(1).IsConcurrencyToken();

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => e.ContractId);
        builder.HasIndex(e => e.SourceDocumentId);
        builder.HasIndex(e => e.DueDate);

        // Owned by the contract: an obligation has no meaning without it.
        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(e => e.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        // Evidence pointer only; see ClauseConfiguration.
        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(e => e.SourceDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
