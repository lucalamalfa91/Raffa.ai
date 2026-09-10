using Raffa.Identity.Workspace.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Identity.Workspace.Infrastructure.Configurations;

public sealed class WorkspaceUserConfiguration : IEntityTypeConfiguration<WorkspaceUser>
{
    public void Configure(EntityTypeBuilder<WorkspaceUser> builder)
    {
        builder.ToTable("workspace_user");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);

        builder.Property(e => e.Email).HasMaxLength(320); // RFC 5321 max mailbox length.
        builder.Property(e => e.DisplayName).HasMaxLength(200);
        builder.Property(e => e.ExternalSubjectId).HasMaxLength(200);

        builder.HasIndex(e => e.TenantId);

        // One profile per email per workspace. Postgres unique indexes treat every NULL as
        // distinct, so multiple not-yet-signed-in rows (ExternalSubjectId is null until task
        // E01/F05/US01/T02's OIDC linkage) never collide with each other.
        builder.HasIndex(e => new { e.TenantId, e.Email }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.ExternalSubjectId }).IsUnique();
    }
}
