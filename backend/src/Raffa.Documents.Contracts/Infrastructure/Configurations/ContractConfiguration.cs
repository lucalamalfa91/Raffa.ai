using Raffa.Documents.Contracts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Documents.Contracts.Infrastructure.Configurations;

public sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> builder)
    {
        builder.ToTable("contract");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);

        // Cross-module reference by id only (Suppliers/Products owns Supplier) — deliberately
        // no FK; a physical constraint would cross a bounded-context boundary (ADR-002).
        builder.Property(e => e.SupplierId)
            .HasConversion(ValueConverters.NullableEntityIdConverter);
        builder.Property(e => e.ParentContractId)
            .HasConversion(ValueConverters.NullableEntityIdConverter);

        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(50);
        builder.Property(e => e.Status).HasMaxLength(50);
        builder.Property(e => e.Currency).HasMaxLength(3);
        builder.Property(e => e.PaymentTerms).HasMaxLength(500);
        builder.Property(e => e.GoverningLaw).HasMaxLength(200);

        builder.Property(e => e.AnnualSpend).HasPrecision(18, 2);
        builder.Property(e => e.TotalContractValue).HasPrecision(18, 2);

        // Optimistic-concurrency guard (Appendix C rule 5) — see Contract.Version.
        builder.Property(e => e.Version).HasDefaultValue(1).IsConcurrencyToken();

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => e.SupplierId);
        builder.HasIndex(e => e.ParentContractId);

        // Self-referencing hierarchy (spec §6.1 amendments/renewals). Restrict, not Cascade:
        // a chain of amendments must not vanish because an earlier link in the chain was
        // deleted, and Postgres/EF Core forbids Cascade on more than one path to the same
        // table anyway.
        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(e => e.ParentContractId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
