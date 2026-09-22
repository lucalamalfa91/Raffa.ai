namespace Raffa.Identity.Workspace.Domain;

/// <summary>
/// The tenant boundary itself (product spec §3.2 "Every business object must carry tenant_id";
/// ADR-009; story us-01-workspace-roles AC-1's "Workspace"). Named <c>WorkspaceTenant</c> rather
/// than the bare "Workspace" because this project's own root namespace is
/// <c>Raffa.Identity.Workspace</c> — a type named exactly <c>Workspace</c> inside it collides
/// with that namespace segment (CS0118 "is a namespace but is used like a type") the moment any
/// other file in this assembly references it unqualified, so every sibling type here
/// (<see cref="WorkspaceUser"/>, <see cref="WorkspaceRole"/>, <see cref="WorkspaceMembership"/>)
/// already uses a compound name for the same structural reason; this one just spells out the
/// concept it stands for instead of leaving it implicit.
///
/// In V1 a workspace *is* a tenant — there is no separate "Tenant" table — so a
/// <see cref="WorkspaceTenant"/> row's own <see cref="TenantScopedEntity.TenantId"/> is always
/// equal to that same row's <see cref="TenantScopedEntity.Id"/>: this table carries `tenant_id`
/// (and is RLS-guarded, AC-1) exactly like every other table here, and every one of those other
/// tables' `tenant_id` values is a logical reference back to one specific workspace row's
/// <see cref="TenantScopedEntity.Id"/> (ADR-009: "FK to workspace/tenant").
///
/// Always construct via <see cref="WorkspaceFactory.CreateWorkspaceWithDefaultRoles"/> rather
/// than the object initializer directly, so the `Id == TenantId` invariant cannot drift.
/// </summary>
public sealed class WorkspaceTenant : TenantScopedEntity
{
    public required string Name { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Closed-list free text entered on the create form (NW-24; ADR-003 w14 amendment footer
    /// clause 1; story us-01-identity-scoped-rls AC-5). Nullable, deliberately: existing rows
    /// predate this column, and a NOT NULL column on a populated table would need a default that
    /// is a fabricated business fact -- the trap this avoids is documented at
    /// `QuoteLineConfiguration.cs:45-49` (EF backfilling a new NOT NULL enum-as-string with `""`
    /// the converter then cannot parse), with the nullable precedent at `quotes.sql:163,170`. A
    /// missing value renders as absent, never as an invented one.
    /// </summary>
    public string? Industry { get; set; }

    /// <summary>
    /// ISO 3166-1 alpha-2, closed list at the UI (NW-24; ADR-003 w14 footer clause 1). Nullable for
    /// the same reason as <see cref="Industry"/> (`quotes.sql:195` precedent). Drives
    /// <see cref="Currency"/>'s derivation -- a later task's job, not this one's.
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// ISO 4217 alpha-3. Derived from <see cref="Country"/> and stored, never typed by a user
    /// (ADR-001 w14 footer; ADR-003 w14 footer clause 1) -- a display default that never overrides
    /// a contract's own extracted currency (converting a validated fact would breach product spec
    /// §2's "AI is not the database"). Nullable for the same reason as <see cref="Industry"/>
    /// (`quotes.sql:202` precedent); the derivation itself is a later task's job (ADR-003 w14
    /// footer names E14/F03/US01/T01).
    /// </summary>
    public string? Currency { get; set; }

    /// <summary>
    /// ADR-030 gate 2: this workspace's Admin opted into Ask Raffa's web research. Off by default
    /// and never inferred: with it off, no web option is ever offered, no consent is ever asked, and
    /// an explicit "search the web" gets a redirect naming this switch. Only
    /// <c>PATCH /api/workspaces/{tenantId}/settings</c> (Admin) flips it, with an audit row.
    /// </summary>
    public bool WebResearchEnabled { get; set; }
}
