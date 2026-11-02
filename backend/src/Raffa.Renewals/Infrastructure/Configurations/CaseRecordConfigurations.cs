using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Raffa.Renewals.Domain;
using Raffa.Renewals.Domain.Cases;

namespace Raffa.Renewals.Infrastructure.Configurations;

/// <summary>Shared id/tenant mapping of the AskRaffa 4.0 case tables.</summary>
internal static class CaseTableMapping
{
    public static void Ids<T>(EntityTypeBuilder<T> builder, string table)
        where T : TenantScopedEntity
    {
        builder.ToTable(table);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasConversion(ValueConverters.EntityIdConverter).ValueGeneratedNever();
        builder.Property(e => e.TenantId).HasConversion(ValueConverters.TenantIdConverter);
        builder.HasIndex(e => e.TenantId);
    }
}

public sealed class RenewalCaseRecordConfiguration : IEntityTypeConfiguration<RenewalCaseRecord>
{
    public void Configure(EntityTypeBuilder<RenewalCaseRecord> builder)
    {
        CaseTableMapping.Ids(builder, "renewal_case");
        builder.Property(e => e.ContractId).HasConversion(ValueConverters.EntityIdConverter);
        builder.Property(e => e.Phase).IsRequired().HasMaxLength(40);
        builder.Property(e => e.Step).IsRequired().HasMaxLength(40);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(40);
        builder.Property(e => e.Branch).IsRequired().HasMaxLength(20);
        builder.Property(e => e.State).IsRequired().HasColumnType("jsonb");
        builder.Property(e => e.Version).IsConcurrencyToken();
        builder.Property(e => e.OutcomeKind).HasMaxLength(20);
        builder.Property(e => e.OutcomeJson).HasColumnType("jsonb");

        // BACKEND-SPEC §2.1: one open case per contract, ever.
        builder.HasIndex(e => new { e.TenantId, e.ContractId })
            .IsUnique()
            .HasFilter("closed_at IS NULL")
            .HasDatabaseName("ux_renewal_case_tenant_contract_open");
    }
}

public sealed class CaseEmailRecordConfiguration : IEntityTypeConfiguration<CaseEmailRecord>
{
    public void Configure(EntityTypeBuilder<CaseEmailRecord> builder)
    {
        CaseTableMapping.Ids(builder, "case_email");
        builder.Property(e => e.CaseId).HasConversion(ValueConverters.EntityIdConverter);
        builder.Property(e => e.Kind).IsRequired().HasMaxLength(40);
        builder.Property(e => e.State).IsRequired().HasMaxLength(20);
        builder.Property(e => e.ToAddress).HasMaxLength(320);
        builder.Property(e => e.Subject).IsRequired();
        builder.Property(e => e.Body).IsRequired();
        builder.Property(e => e.AttachmentsJson).HasColumnType("jsonb");
        builder.Property(e => e.ApprovedBy).HasMaxLength(200);
        builder.Property(e => e.SentBy).HasMaxLength(200);
        builder.HasIndex(e => new { e.TenantId, e.CaseId, e.EmailIndex }).IsUnique();
    }
}

public sealed class CaseDelegationRecordConfiguration : IEntityTypeConfiguration<CaseDelegationRecord>
{
    public void Configure(EntityTypeBuilder<CaseDelegationRecord> builder)
    {
        CaseTableMapping.Ids(builder, "case_delegation");
        builder.Property(e => e.CaseId).HasConversion(ValueConverters.EntityIdConverter);
        builder.Property(e => e.UserName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.GrantedBy).HasMaxLength(200);
        builder.HasIndex(e => new { e.TenantId, e.CaseId });
    }
}

public sealed class CaseEventRecordConfiguration : IEntityTypeConfiguration<CaseEventRecord>
{
    public void Configure(EntityTypeBuilder<CaseEventRecord> builder)
    {
        CaseTableMapping.Ids(builder, "case_event");
        builder.Property(e => e.CaseId).HasConversion(ValueConverters.EntityIdConverter);
        builder.Property(e => e.Actor).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Type).IsRequired().HasMaxLength(60);
        builder.Property(e => e.PayloadJson).HasColumnType("jsonb");
        builder.HasIndex(e => new { e.TenantId, e.CaseId, e.At });
    }
}

public sealed class CaseVendorMessageRecordConfiguration : IEntityTypeConfiguration<CaseVendorMessageRecord>
{
    public void Configure(EntityTypeBuilder<CaseVendorMessageRecord> builder)
    {
        CaseTableMapping.Ids(builder, "case_vendor_message");
        builder.Property(e => e.CaseId).HasConversion(ValueConverters.EntityIdConverter);
        builder.Property(e => e.Direction).IsRequired().HasMaxLength(10);
        builder.Property(e => e.PastedText).IsRequired();
        builder.Property(e => e.ContentHash).IsRequired().HasMaxLength(64);
        builder.Property(e => e.ThreadKey).HasMaxLength(200);
        builder.Property(e => e.ParsedJson).HasColumnType("jsonb");
        builder.Property(e => e.AnalysedByModel).HasMaxLength(200);
        builder.Property(e => e.PromptVersion).HasMaxLength(60);
        builder.Property(e => e.InputHash).HasMaxLength(64);

        // §2.6: the same reply pasted twice is detected, never stored twice.
        builder.HasIndex(e => new { e.TenantId, e.CaseId, e.ContentHash }).IsUnique();
    }
}

public sealed class UserReminderRecordConfiguration : IEntityTypeConfiguration<UserReminderRecord>
{
    public void Configure(EntityTypeBuilder<UserReminderRecord> builder)
    {
        CaseTableMapping.Ids(builder, "user_reminder");
        builder.Property(e => e.UserName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Kind).IsRequired().HasMaxLength(20);
        builder.Property(e => e.State).IsRequired().HasMaxLength(20);
        builder.HasIndex(e => new { e.TenantId, e.UserId, e.DueDate });
    }
}

public sealed class TodaySnoozeRecordConfiguration : IEntityTypeConfiguration<TodaySnoozeRecord>
{
    public void Configure(EntityTypeBuilder<TodaySnoozeRecord> builder)
    {
        CaseTableMapping.Ids(builder, "today_snooze");
        builder.Property(e => e.ItemKey).IsRequired().HasMaxLength(200);
        builder.HasIndex(e => new { e.TenantId, e.UserId, e.ItemKey }).IsUnique();
    }
}

public sealed class CompanyRulesRecordConfiguration : IEntityTypeConfiguration<CompanyRulesRecord>
{
    public void Configure(EntityTypeBuilder<CompanyRulesRecord> builder)
    {
        CaseTableMapping.Ids(builder, "company_rules");
        builder.Property(e => e.Text).IsRequired();
        builder.Property(e => e.SourceFile).HasMaxLength(300);
        builder.Property(e => e.UpdatedBy).IsRequired().HasMaxLength(200);
        builder.HasIndex(e => e.TenantId).IsUnique();
    }
}
