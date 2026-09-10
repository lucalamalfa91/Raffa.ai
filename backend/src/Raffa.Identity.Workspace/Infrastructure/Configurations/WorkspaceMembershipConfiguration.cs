using Raffa.Identity.Workspace.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Identity.Workspace.Infrastructure.Configurations;

public sealed class WorkspaceMembershipConfiguration : IEntityTypeConfiguration<WorkspaceMembership>
{
    public void Configure(EntityTypeBuilder<WorkspaceMembership> builder)
    {
        builder.ToTable("workspace_membership");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(e => e.WorkspaceUserId)
            .HasConversion(ValueConverters.EntityIdConverter);
        builder.Property(e => e.WorkspaceRoleId)
            .HasConversion(ValueConverters.EntityIdConverter);

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => new { e.WorkspaceUserId, e.WorkspaceRoleId }).IsUnique();

        // Same-module, same-tenant references (Identity/Workspace owns User, Role and Membership
        // alike); Cascade off the user (removing a user removes their assignments), Restrict off
        // the role (the five catalog roles are not expected to be deleted while assigned).
        builder.HasOne<WorkspaceUser>()
            .WithMany()
            .HasForeignKey(e => e.WorkspaceUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<WorkspaceRole>()
            .WithMany()
            .HasForeignKey(e => e.WorkspaceRoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
