using Raffa.Documents.Contracts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Documents.Contracts.Infrastructure.Configurations;

public sealed class ContractLineItemConfiguration : IEntityTypeConfiguration<ContractLineItem>
{
    public void Configure(EntityTypeBuilder<ContractLineItem> builder)
    {
        builder.ToTable("contract_line_item");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(e => e.ContractId)
            .HasConversion(ValueConverters.EntityIdConverter);

        // Cross-module reference by id only (Suppliers/Products owns Product) — deliberately no
        // FK; a physical constraint would cross a bounded-context boundary (ADR-002), the same
        // treatment ContractConfiguration gives Contract.SupplierId.
        builder.Property(e => e.ProductId)
            .HasConversion(ValueConverters.NullableEntityIdConverter);
        builder.Property(e => e.SourceDocumentId)
            .HasConversion(ValueConverters.NullableEntityIdConverter);

        builder.Property(e => e.Sku).HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.Unit).HasMaxLength(50);
        builder.Property(e => e.BillingPeriod).HasMaxLength(50);
        builder.Property(e => e.SourceSpan).HasMaxLength(500);

        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.UnitPrice).HasPrecision(18, 2);
        builder.Property(e => e.ListPrice).HasPrecision(18, 2);
        builder.Property(e => e.Discount).HasPrecision(5, 2);
        builder.Property(e => e.AnnualCost).HasPrecision(18, 2);
        builder.Property(e => e.TotalCost).HasPrecision(18, 2);

        // Optimistic-concurrency guard (Appendix C rule 5) — see Contract.Version.
        builder.Property(e => e.Version).HasDefaultValue(1).IsConcurrencyToken();

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => e.ContractId);
        builder.HasIndex(e => e.ProductId);
        builder.HasIndex(e => e.SourceDocumentId);

        // Owned by the contract: a line item has no meaning without it.
        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(e => e.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        // Evidence pointer only; see ClauseConfiguration — deleting the source document must
        // not silently delete the line item fact extracted from it.
        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(e => e.SourceDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
