using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Identity.Workspace.Infrastructure;

/// <summary>
/// Reads and writes a workspace's settings row (ADR-030 gate 2: <c>web_research_enabled</c>).
/// Tenant-scoped like every sibling service here (<see cref="ITenantContext.BeginScope"/> around
/// each read/write, RLS underneath); a change writes one audit row
/// (<c>workspace.settings.web_research_enabled</c>) naming the actor and the new value, and a
/// no-op write (same value) writes none. The Admin gate is the endpoint's job, not this
/// service's -- the same split <see cref="WorkspaceMembershipService.RemoveMemberAsync"/> uses.
/// </summary>
public sealed class WorkspaceSettingsService(
    IdentityWorkspaceDbContext db, ITenantContext tenantContext, IClock clock, IAuditWriter auditWriter)
{
    public const string AuditWebResearchEnabledAction = "workspace.settings.web_research_enabled";
    private const string AuditResourceType = "Workspace";

    /// <summary><see langword="null"/> when no workspace row exists for the tenant.</summary>
    public async Task<WorkspaceSettingsResult?> GetAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var enabled = await db.Workspaces
            .AsNoTracking()
            .Where(w => w.TenantId == tenantId)
            .Select(w => (bool?)w.WebResearchEnabled)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return enabled is null ? null : new WorkspaceSettingsResult(enabled.Value);
    }

    /// <summary>Flips the web-research opt-in; <see langword="null"/> when no workspace row exists.</summary>
    public async Task<WorkspaceSettingsResult?> SetWebResearchEnabledAsync(
        TenantId tenantId, bool enabled, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var workspace = await db.Workspaces
            .FirstOrDefaultAsync(w => w.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (workspace is null)
        {
            return null;
        }

        if (workspace.WebResearchEnabled != enabled)
        {
            workspace.WebResearchEnabled = enabled;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await auditWriter.WriteAsync(
                    new AuditEntry(
                        tenantId, actor, AuditWebResearchEnabledAction, AuditResourceType,
                        workspace.Id.Value.ToString(), clock.UtcNow, $"enabled={enabled}"),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return new WorkspaceSettingsResult(workspace.WebResearchEnabled);
    }
}
