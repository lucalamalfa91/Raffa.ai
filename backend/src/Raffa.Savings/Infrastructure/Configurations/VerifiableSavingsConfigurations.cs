using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Raffa.Savings.Domain;
using Raffa.Savings.Domain.Results;

namespace Raffa.Savings.Infrastructure.Configurations;

internal static class TenantKeyConfiguration
{
    public static void Apply<T>(EntityTypeBuilder<T> builder)
        where T : TenantScopedEntity
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasConversion(ValueConverters.EntityIdConverter).ValueGeneratedNever();
        builder.Property(e => e.TenantId).HasConversion(ValueConverters.TenantIdConverter);
        builder.HasIndex(e => e.TenantId);
    }
}

public sealed class SavingResultConfiguration : IEntityTypeConfiguration<SavingResult>
{
    public void Configure(EntityTypeBuilder<SavingResult> builder)
    {
        builder.ToTable("saving_result");
        TenantKeyConfiguration.Apply(builder);
        builder.Property(e => e.SupplierName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.ContractName).IsRequired().HasMaxLength(300);
        builder.Property(e => e.Category).HasMaxLength(100);
        builder.Property(e => e.Kind).IsRequired().HasMaxLength(20);
        builder.Property(e => e.AmountAnnual).HasPrecision(18, 2);
        builder.Property(e => e.Currency).IsRequired().HasMaxLength(3);
        builder.Property(e => e.BaselineValue).HasPrecision(18, 4);
        builder.Property(e => e.NewValue).HasPrecision(18, 4);
        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.EvidenceFileName).IsRequired().HasMaxLength(300);
        builder.Property(e => e.OwnerName).HasMaxLength(200);
        builder.Property(e => e.RecordedBy).HasMaxLength(200);
        builder.HasIndex(e => new { e.TenantId, e.SignedOn });
        builder.HasIndex(e => new { e.TenantId, e.ContractId });
    }
}

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoice");
        TenantKeyConfiguration.Apply(builder);
        builder.Property(e => e.SupplierName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.Currency).IsRequired().HasMaxLength(3);
        builder.HasIndex(e => new { e.TenantId, e.ContractId, e.Period });
    }
}

public sealed class SavingRealizationConfiguration : IEntityTypeConfiguration<SavingRealization>
{
    public void Configure(EntityTypeBuilder<SavingRealization> builder)
    {
        builder.ToTable("saving_realization");
        TenantKeyConfiguration.Apply(builder);
        builder.Property(e => e.SavingResultId).HasConversion(ValueConverters.EntityIdConverter);
        builder.Property(e => e.InvoiceId).HasConversion(ValueConverters.EntityIdConverter);
        builder.Property(e => e.RealizedAmount).HasPrecision(18, 2);
        builder.HasIndex(e => new { e.SavingResultId, e.InvoiceId }).IsUnique();
        builder.HasOne<SavingResult>().WithMany().HasForeignKey(e => e.SavingResultId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Invoice>().WithMany().HasForeignKey(e => e.InvoiceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SavingsGoalConfiguration : IEntityTypeConfiguration<SavingsGoal>
{
    public void Configure(EntityTypeBuilder<SavingsGoal> builder)
    {
        builder.ToTable("savings_goal");
        TenantKeyConfiguration.Apply(builder);
        builder.Property(e => e.UserId).IsRequired().HasMaxLength(200);
        builder.Property(e => e.SourceText).IsRequired().HasMaxLength(1000);
        builder.Property(e => e.Pct).HasPrecision(9, 2);
        builder.Property(e => e.AnnualAmount).HasPrecision(18, 2);
        builder.Property(e => e.ScopeJson).HasColumnType("jsonb");
        builder.Property(e => e.Period).IsRequired().HasMaxLength(40);
        builder.HasIndex(e => new { e.TenantId, e.UserId })
            .IsUnique()
            .HasFilter("active")
            .HasDatabaseName("ux_savings_goal_active_per_user");
    }
}

public sealed class CaseObjectiveConfiguration : IEntityTypeConfiguration<CaseObjective>
{
    public void Configure(EntityTypeBuilder<CaseObjective> builder)
    {
        builder.ToTable("case_objective");
        TenantKeyConfiguration.Apply(builder);
        builder.Property(e => e.OwnerName).HasMaxLength(200);
        builder.Property(e => e.Kind).HasMaxLength(20);
        builder.Property(e => e.Pct).HasPrecision(9, 2);
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.Text).HasMaxLength(1000);
        builder.Property(e => e.Outcome).HasMaxLength(20);
        builder.HasIndex(e => new { e.TenantId, e.CaseId }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.ContractId });
    }
}
