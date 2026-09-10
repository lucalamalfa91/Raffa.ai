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

        // WorkspaceTenant.TenantId always equals WorkspaceTenant.Id (WorkspaceFactory) -- the
        // workspace IS the tenant boundary (ADR-009) -- so this unique index guards that
        // invariant rather than expressing an independent business rule.
        builder.HasIndex(e => e.TenantId).IsUnique();
    }
}
