using Raffa.Identity.Workspace.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Identity.Workspace.Infrastructure.Configurations;

public sealed class WorkspaceRoleConfiguration : IEntityTypeConfiguration<WorkspaceRole>
{
    public void Configure(EntityTypeBuilder<WorkspaceRole> builder)
    {
        builder.ToTable("workspace_role");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);

        builder.Property(e => e.Name).HasConversion<string>().HasMaxLength(30);

        builder.HasIndex(e => e.TenantId);

        // One row per WorkspaceRoleName per workspace (WorkspaceFactory seeds all five up front).
        builder.HasIndex(e => new { e.TenantId, e.Name }).IsUnique();
    }
}
