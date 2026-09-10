using Raffa.Documents.Contracts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Documents.Contracts.Infrastructure.Configurations;

public sealed class RiskConfiguration : IEntityTypeConfiguration<Risk>
{
    public void Configure(EntityTypeBuilder<Risk> builder)
    {
        builder.ToTable("risk");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(e => e.ContractId)
            .HasConversion(ValueConverters.EntityIdConverter);
        builder.Property(e => e.ClauseId)
            .HasConversion(ValueConverters.NullableEntityIdConverter);
        builder.Property(e => e.SourceDocumentId)
            .HasConversion(ValueConverters.NullableEntityIdConverter);

        builder.Property(e => e.RiskType).HasMaxLength(100);
        builder.Property(e => e.Severity).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Status).HasMaxLength(30);
        builder.Property(e => e.SourceSpan).HasMaxLength(500);

        // Optimistic-concurrency guard (Appendix C rule 5) — see Contract.Version.
        builder.Property(e => e.Version).HasDefaultValue(1).IsConcurrencyToken();

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => e.ContractId);
        builder.HasIndex(e => e.ClauseId);
        builder.HasIndex(e => e.SourceDocumentId);

        // Owned by the contract: a risk has no meaning without it.
        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(e => e.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        // Optional trace back to the clause it was derived from; deleting the clause must not
        // silently delete the risk record.
        builder.HasOne<Clause>()
            .WithMany()
            .HasForeignKey(e => e.ClauseId)
            .OnDelete(DeleteBehavior.Restrict);

        // Direct evidence pointer (Appendix C rule 2), independent of ClauseId — see
        // ClauseConfiguration; deleting the source document must not silently delete the risk.
        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(e => e.SourceDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
