using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Identity.Workspace.Infrastructure;

/// <summary>
/// EF Core DbContext for the Identity/Workspace bounded context (ADR-003). Postgres via npgsql is
/// the only access path; schema changes flow through code-first migrations only (no hand-edited
/// DDL). RLS policies and the ambient per-request tenant claim are wired the same way task
/// E01/F04/US03/T01 wired them for Documents/Contracts — this context only shapes the model and
/// exposes the DbSets.
/// </summary>
public sealed class IdentityWorkspaceDbContext(DbContextOptions<IdentityWorkspaceDbContext> options)
    : DbContext(options)
{
    public DbSet<WorkspaceTenant> Workspaces => Set<WorkspaceTenant>();
    public DbSet<WorkspaceUser> WorkspaceUsers => Set<WorkspaceUser>();
    public DbSet<WorkspaceRole> WorkspaceRoles => Set<WorkspaceRole>();
    public DbSet<WorkspaceMembership> WorkspaceMemberships => Set<WorkspaceMembership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new WorkspaceTenantConfiguration());
        modelBuilder.ApplyConfiguration(new WorkspaceUserConfiguration());
        modelBuilder.ApplyConfiguration(new WorkspaceRoleConfiguration());
        modelBuilder.ApplyConfiguration(new WorkspaceMembershipConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
