using Raffa.SharedKernel;

namespace Raffa.Identity.Workspace.Domain;

/// <summary>
/// An offered-but-not-yet-accepted membership (task E14/F01/US01/T02; story
/// us-01-identity-scoped-rls AC-4; ADR-025 §D.1 "an invitation is an offer, not a grant"; ADR-026
/// §D4; ADR-003 w14 amendment footer clause 2). An ordinary <see cref="TenantScopedEntity"/>
/// subclass, unlike <see cref="WorkspaceUser"/>'s <c>identity_self</c> widening: this table gets
/// no additional policy, only the same <c>tenant_isolation</c> shape every other table here
/// carries, shipped in the same migration that creates the table
/// (<c>Migrations.AddWorkspaceInvitation</c>) -- `TenantRlsDeployableScriptCheckTests` discovers
/// every <see cref="TenantScopedEntity"/> subclass from the EF model and fails the build if a
/// policy is missing.
///
/// The token itself (`{tenantId:N}.{secret}`, ADR-025 §C) is never stored here: only
/// <see cref="TokenHash"/>, a SHA-256 hash of the secret half, so a read of this table can never
/// mint an acceptance (ADR-025 Rule C2). Validating a presented token, binding the accepting
/// identity and writing the resulting <see cref="WorkspaceMembership"/> is a later task's job
/// (ADR-025 §D.1/§D.3) -- this task's guarantee is structural: the columns, their nullability and
/// the three indexes (unique `(tenant_id, token_hash)`; a plain `tenant_id` index; and the partial
/// unique `(tenant_id, lower(email)) WHERE accepted_at IS NULL AND revoked_at IS NULL` that keeps
/// at most one live invitation per address per tenant) exist and are RLS-backstopped, not the
/// accept/issue workflow itself.
/// </summary>
public sealed class WorkspaceInvitation : TenantScopedEntity
{
    /// <summary>RFC 5321 mailbox, matching <see cref="WorkspaceUser.Email"/>; stored lower-cased.</summary>
    public required string Email { get; set; }

    /// <summary>FK to <see cref="WorkspaceRole"/> -- the role this invitation offers.</summary>
    public required EntityId WorkspaceRoleId { get; set; }

    /// <summary>SHA-256 hash of the secret half of the token (ADR-025 Rule C2). Never the token itself.</summary>
    public required string TokenHash { get; set; }

    /// <summary>The inviting identity (ADR-025 §A's <c>ICallerIdentity</c> seam).</summary>
    public required string InvitedBy { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>Absolute expiry (ADR-025 Rule C7: 7 days), evaluated server-side against <see cref="Raffa.SharedKernel.IClock"/>.</summary>
    public required DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Null until accepted (ADR-025 §D.3).</summary>
    public DateTimeOffset? AcceptedAt { get; set; }

    /// <summary>Null until revoked -- explicitly (ADR-025 §D.5c) or implicitly by a re-invite.</summary>
    public DateTimeOffset? RevokedAt { get; set; }
}
