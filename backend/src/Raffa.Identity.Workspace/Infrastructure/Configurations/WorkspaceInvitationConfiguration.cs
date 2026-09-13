using Raffa.Identity.Workspace.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raffa.Identity.Workspace.Infrastructure.Configurations;

public sealed class WorkspaceInvitationConfiguration : IEntityTypeConfiguration<WorkspaceInvitation>
{
    public void Configure(EntityTypeBuilder<WorkspaceInvitation> builder)
    {
        builder.ToTable("workspace_invitation");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(ValueConverters.EntityIdConverter)
            .ValueGeneratedNever();
        builder.Property(e => e.TenantId)
            .HasConversion(ValueConverters.TenantIdConverter);
        builder.Property(e => e.WorkspaceRoleId)
            .HasConversion(ValueConverters.EntityIdConverter);

        builder.Property(e => e.Email).HasMaxLength(320); // RFC 5321 max mailbox length; stored lower-cased.
        builder.Property(e => e.TokenHash).HasMaxLength(128); // SHA-256 digest of the secret half (ADR-025 Rule C2) -- never the token itself.
        builder.Property(e => e.InvitedBy).HasMaxLength(320); // the inviting identity (ADR-025 §A).

        // Tenant-first convention (WorkspaceUserConfiguration.cs:24-25, suppliers.sql:37,
        // quotes.sql:233): a plain tenant_id index, then the unique composite that also starts
        // with it.
        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => new { e.TenantId, e.TokenHash }).IsUnique();

        // The third index this table needs -- unique (tenant_id, lower(email)) WHERE accepted_at
        // IS NULL AND revoked_at IS NULL, so at most one live invitation per address per tenant
        // survives (ADR-025 §F.4, ADR-026 §D4) -- is a Postgres partial *expression* index.
        // EF's fluent HasIndex binds only to real CLR properties (HasFilter supplies a WHERE
        // clause, not a `lower(...)` column transform), so it cannot be declared here. It is
        // hand-authored as raw SQL directly inside migration `AddWorkspaceInvitation`, the same
        // way this module's `tenant_isolation` / `identity_self` RLS policies live as raw SQL
        // rather than fluent model configuration -- see that migration's own doc comment.

        // Same-tenant reference to the offered role (WorkspaceMembershipConfiguration.cs:35-38
        // precedent): Restrict, since the five catalog roles are not expected to be deleted while
        // an invitation still offers one.
        builder.HasOne<WorkspaceRole>()
            .WithMany()
            .HasForeignKey(e => e.WorkspaceRoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
