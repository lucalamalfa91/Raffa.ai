using System.Globalization;
using Raffa.Identity.Workspace.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Identity.Workspace.Infrastructure;

/// <summary>
/// One workspace fact row for <see cref="WorkspaceDirectoryService.ListForIdentityAsync"/> (ADR-026
/// §D1). Deliberately carries no contract count: this service "does not know what a contract is"
/// (ADR-026 §D2) — <c>Raffa.Api</c>'s handler joins
/// <c>Raffa.Documents.Contracts.Application.PortfolioQueryService.CountValidatedContractsAsync</c>
/// onto each item in the host, the one project allowed to see both modules.
/// </summary>
public sealed record WorkspaceListItem(
    TenantId TenantId,
    string Name,
    DateTimeOffset CreatedAt,
    WorkspaceRoleName Role,
    string? Country,
    string? Currency);

/// <summary>
/// Implements task E14/F03/US01/T01 (wave w14 "workspace is real", NW-01; ADR-026 §D1, ADR-025
/// §F.1/§F.3): "which workspaces does this identity belong to". Registered in
/// <see cref="ServiceCollectionExtensions"/>; <c>Raffa.Api.WorkspaceEndpointExtensions</c>'
/// <c>GET /api/workspaces</c> handler is the only caller and is also the one call site in the
/// product that opens <see cref="ICallerIdentityContext.BeginIdentityScope"/> — this service never
/// does, and never injects <see cref="ICallerIdentityContext"/> at all: it trusts that scope is
/// already open around its own call, the same "call-site scope" shape
/// <c>PortfolioEndpointExtensions.cs</c>/<c>WorkspaceRoleResolver.cs</c>/<c>AskCopilotService.cs</c>
/// already use for <see cref="ITenantContext"/>.
/// </summary>
public sealed class WorkspaceDirectoryService(
    IdentityWorkspaceDbContext db, ITenantContext tenantContext, IClock clock, IAuditWriter auditWriter)
{
    /// <summary>
    /// Discovery cap (ADR-025 Rule F.1f, ADR-009 w14 footer clause 4): memberships per identity are
    /// 1–2 at pilot scale, so a request needing more than this is either a data anomaly or a
    /// caller-controlled-identity DoS shape — either way, bounded, not unbounded. Public so
    /// <c>WorkspaceDirectoryServiceTests</c> asserts against this one definition rather than a
    /// duplicated magic number that could silently drift from it.
    /// </summary>
    public const int MaxCandidates = 50;

    /// <summary>
    /// Two-phase workspace discovery for <paramref name="identity"/> (ADR-026 §D1):
    /// <list type="number">
    /// <item><b>Discovery.</b> With <c>app.identity_subject</c> already set by the caller's own
    /// identity scope and <b>no</b> tenant scope, select candidate <see cref="TenantId"/>s from
    /// <c>workspace_user</c> for the matching identity — the `identity_self` policy
    /// (migration <c>AddWorkspaceUserIdentitySelfReadPolicy</c>) is the only reason this returns
    /// anything; the <c>Where</c> below is belt-and-suspenders, the same convention
    /// <c>PortfolioQueryService</c> already applies on top of its own tenant RLS. Capped at
    /// <see cref="MaxCandidates"/> + 1 rows fetched (never more, regardless of how many rows a
    /// pathological identity owns) so the cap can be detected without ever materialising an
    /// unbounded result.</item>
    /// <item><b>Projection.</b> For each (at most <see cref="MaxCandidates"/>) candidate, enter that
    /// tenant's own scope and read the <c>workspace</c> row plus the caller's role through the
    /// existing membership join (<c>WorkspaceRoleResolver.cs:101-103,116-118</c> precedence — a
    /// person holding two roles in one tenant appears once, at the highest). <b>Inclusion is gated
    /// on a live <c>workspace_membership</c> row, never on the <c>workspace_user</c> row</b>
    /// (ADR-025 Rule F.1d): a removed member keeps their user row by design, and a
    /// user-row-driven list would show them the tenant they were removed from.</item>
    /// </list>
    ///
    /// <para>
    /// This is the wave's one sanctioned multi-tenant-scope method (ADR-025 §F.3 Exception 1,
    /// ADR-009 w14 footer clause 4), bounded by four rules — all enforced by the loop below, and
    /// named here so a reviewer finds every one of them in this one place:
    /// <list type="number">
    /// <item><b>Sequential, never nested.</b> The <c>foreach</c> loop fully awaits and disposes one
    /// candidate's <see cref="ITenantContext.BeginScope"/> handle before the next candidate's call
    /// to <see cref="ITenantContext.BeginScope"/> begins — never two active scopes at once.</item>
    /// <item><b>Its own connection each.</b> No query below holds the connection open across a scope
    /// change (no explicit <c>OpenConnectionAsync</c>, no ambient transaction spanning candidates),
    /// so EF Core opens and closes the connection per operation and
    /// <see cref="TenantRlsConnectionInterceptor"/> re-fires <c>ConnectionOpened</c> — and re-reads
    /// <see cref="ITenantContext.Current"/> — under each candidate's own claim (ADR-009 w14 footer
    /// clause 5).</item>
    /// <item><b>One named method.</b> Every scope entry for a tenant other than one the caller has
    /// already been verified into happens here, in this one method, and nowhere else in the
    /// product.</item>
    /// <item><b>No precedent.</b> Any other endpoint entering a second tenant scope in one request is
    /// a defect, not an extension of this one (ADR-025 §F.3: "any other endpoint doing it is a
    /// defect, not a precedent").</item>
    /// </list>
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<WorkspaceListItem>> ListForIdentityAsync(
        string identity, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);

        // Normalised the same way CallerIdentityContext.BeginIdentityScope normalises the GUC value
        // itself (trim + lower), so this method's own belt-and-suspenders comparisons below match
        // the identity_self policy's `lower(email)` comparison regardless of what a future caller
        // passes in.
        var normalizedIdentity = identity.Trim().ToLowerInvariant();

        var discovered = await db.WorkspaceUsers
            .AsNoTracking()
            .Where(u => u.Email.ToLower() == normalizedIdentity || u.ExternalSubjectId == normalizedIdentity)
            .Select(u => u.TenantId)
            .Take(MaxCandidates + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var truncated = discovered.Count > MaxCandidates;
        var candidates = truncated ? discovered.Take(MaxCandidates).ToList() : discovered;

        var items = new List<WorkspaceListItem>(candidates.Count);
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidateTenantId = candidates[index];
            using var tenantScope = tenantContext.BeginScope(candidateTenantId);

            // Truncation is audited once, inside the first candidate's own already-open scope,
            // rather than opening a 51st scope purely for the write (still "one named method",
            // still sequential — see the four bounds above). AuditEvent requires a tenant_id; which
            // of the identity's own tenants carries the row is a technical necessity of that
            // schema, not a claim about which tenant "caused" the truncation.
            if (truncated && index == 0)
            {
                await auditWriter.WriteAsync(
                    new AuditEntry(
                        candidateTenantId,
                        normalizedIdentity,
                        "workspace.list.truncated",
                        "Workspace",
                        MaxCandidates.ToString(CultureInfo.InvariantCulture),
                        clock.UtcNow),
                    cancellationToken).ConfigureAwait(false);
            }

            var roleNames = await (
                from user in db.WorkspaceUsers
                join membership in db.WorkspaceMemberships on user.Id equals membership.WorkspaceUserId
                join workspaceRole in db.WorkspaceRoles on membership.WorkspaceRoleId equals workspaceRole.Id
                where user.TenantId == candidateTenantId
                    && membership.TenantId == candidateTenantId
                    && workspaceRole.TenantId == candidateTenantId
                    && (user.Email.ToLower() == normalizedIdentity || user.ExternalSubjectId == normalizedIdentity)
                select workspaceRole.Name)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // Rule F.1d: a live workspace_membership row is the grant, never workspace_user's own
            // existence. No recognized role among this identity's memberships in this tenant (most
            // commonly: none at all, because the membership was removed) excludes the tenant
            // entirely — WorkspaceRoleClaimResolver.TryResolve's own highest-role-wins precedence
            // also collapses a person holding two roles in this one tenant to a single row.
            if (!WorkspaceRoleClaimResolver.TryResolve(roleNames.Select(name => name.ToString()), out var role))
            {
                continue;
            }

            var workspace = await db.Workspaces
                .AsNoTracking()
                .SingleOrDefaultAsync(w => w.TenantId == candidateTenantId, cancellationToken)
                .ConfigureAwait(false);

            if (workspace is null)
            {
                // Defensive only: WorkspaceFactory's own Id == TenantId invariant means a live
                // membership cannot exist without a workspace row. Skip rather than throw, so one
                // corrupt tenant cannot 500 every other workspace this identity legitimately holds.
                continue;
            }

            items.Add(new WorkspaceListItem(
                candidateTenantId, workspace.Name, workspace.CreatedAt, role, workspace.Country, workspace.Currency));
        }

        // AC-1/N2: ordered by createdAt ascending, stable — OrderBy is a stable sort, so two
        // workspaces created in the same instant keep their discovery order instead of reshuffling
        // between loads.
        return items.OrderBy(item => item.CreatedAt).ToList();
    }
}
