using Raffa.Documents.Contracts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Documents.Contracts.Infrastructure.Configurations;

public sealed class ClauseConfiguration : IEntityTypeConfiguration<Clause>
{
    public void Configure(EntityTypeBuilder<Clause> builder)
    {
        builder.ToTable("clause");
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

        builder.Property(e => e.ClauseType).HasMaxLength(100);
        builder.Property(e => e.RiskLevel).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.SourceSpan).HasMaxLength(500);

        // Optimistic-concurrency guard (Appendix C rule 5) — see Contract.Version.
        builder.Property(e => e.Version).HasDefaultValue(1).IsConcurrencyToken();

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => e.ContractId);
        builder.HasIndex(e => e.SourceDocumentId);

        // Owned by the contract: a clause has no meaning without it.
        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(e => e.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        // Evidence pointer only; deleting the source document must not silently delete the
        // clause fact extracted from it.
        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(e => e.SourceDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
