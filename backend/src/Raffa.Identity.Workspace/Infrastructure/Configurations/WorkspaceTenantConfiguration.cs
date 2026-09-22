using Raffa.Identity.Workspace.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Identity.Workspace.Infrastructure.Configurations;

public sealed class WorkspaceTenantConfiguration : IEntityTypeConfiguration<WorkspaceTenant>
{
    public void Configure(EntityTypeBuilder<WorkspaceTenant> builder)
    {
        builder.ToTable("workspace");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);

        builder.Property(e => e.Name).HasMaxLength(200);

        // NW-24 / ADR-003 w14 footer clause 1 (task E14/F01/US01/T02): all three nullable, closed
        // lists at the UI. See WorkspaceTenant's own property doc comments for why nullable is
        // deliberate rather than a gap.
        builder.Property(e => e.Industry).HasMaxLength(120);
        builder.Property(e => e.Country).HasMaxLength(2);
        builder.Property(e => e.Currency).HasMaxLength(3);

        // ADR-030 gate 2: NOT NULL with a false default -- an existing row is "off", never a
        // fabricated business fact (a switch defaults closed).
        builder.Property(e => e.WebResearchEnabled).HasDefaultValue(false);

        // WorkspaceTenant.TenantId always equals WorkspaceTenant.Id (WorkspaceFactory) -- the
        // workspace IS the tenant boundary (ADR-009) -- so this unique index guards that
        // invariant rather than expressing an independent business rule.
        builder.HasIndex(e => e.TenantId).IsUnique();
    }
}
