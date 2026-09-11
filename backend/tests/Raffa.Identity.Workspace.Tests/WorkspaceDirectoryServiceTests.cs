using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Identity.Workspace.Tests;

/// <summary>
/// Proves the Definition of Done for task E14/F03/US01/T01 (wave w14 "workspace is real", NW-01;
/// ADR-026 §D1, ADR-025 §F.1/§F.3/Rule F.1f) at the service level: a person holding two roles in one
/// tenant is collapsed to the highest, a removed membership excludes its tenant from the result
/// (Rule F.1d), discovery is capped at <see cref="WorkspaceDirectoryService.MaxCandidates"/> with the
/// cap audited, and each candidate's own facts are attributed correctly (a proxy for "sequential,
/// never nested" — a nesting bug would surface here as one tenant's data bleeding into another's
/// row). The end-to-end proof that the GUC and the `identity_self` RLS policy actually agree is the
/// HTTP-level <c>Raffa.Api.Tests.WorkspaceDirectoryEndpointTests</c> (T1a) — this class runs against
/// the Testcontainers *superuser* connection, same rationale
/// <c>WorkspaceMembershipServiceTests</c> already gives for skipping the dedicated unprivileged-role
/// rig: it proves this service's own business logic (role precedence, the cap, per-candidate
/// attribution), not RLS cross-tenant enforcement.
/// </summary>
public sealed class WorkspaceDirectoryServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly TenantContext _tenantContext = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private IdentityWorkspaceDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdentityWorkspaceDbContext>();
        IdentityWorkspaceDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString(), _tenantContext);
        return new IdentityWorkspaceDbContext(optionsBuilder.Options);
    }

    private sealed class InstantClock(DateTimeOffset instant) : IClock
    {
        public DateTimeOffset UtcNow => instant;
    }

    /// <summary>Records every <see cref="AuditEntry"/> written, so
    /// <see cref="Caps_discovery_at_the_limit_and_audits_the_truncation"/> can assert on
    /// `workspace.list.truncated` without a real `Raffa.Audit` database.</summary>
    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    /// <summary>Seeds a full workspace (its default role catalog, one <see cref="WorkspaceUser"/>
    /// for <paramref name="identity"/>, and one live <see cref="WorkspaceMembership"/> per entry in
    /// <paramref name="roleNames"/>) with an explicit, caller-controlled <paramref name="createdAt"/>
    /// so ordering assertions do not depend on real-clock resolution.</summary>
    private async Task<TenantId> SeedWorkspaceWithMembershipAsync(
        string name,
        DateTimeOffset createdAt,
        string identity,
        IReadOnlyList<WorkspaceRoleName> roleNames,
        string? country = null,
        string? currency = null)
    {
        var (workspace, roles) = WorkspaceFactory.CreateWorkspaceWithDefaultRoles(name, new InstantClock(createdAt));
        workspace.Country = country;
        workspace.Currency = currency;

        await using var db = CreateContext();
        using var _ = _tenantContext.BeginScope(workspace.TenantId);

        db.Workspaces.Add(workspace);
        db.WorkspaceRoles.AddRange(roles);

        var user = WorkspaceMembershipFactory.CreateInvitedUser(workspace.TenantId, identity, createdAt).Value;
        db.WorkspaceUsers.Add(user);

        foreach (var roleName in roleNames)
        {
            var role = roles.Single(r => r.Name == roleName);
            db.WorkspaceMemberships.Add(WorkspaceMembershipFactory.CreateMembership(user, role, createdAt).Value);
        }

        await db.SaveChangesAsync();
        return workspace.TenantId;
    }

    /// <summary>Seeds a workspace and a <see cref="WorkspaceUser"/> row for <paramref
    /// name="identity"/> but writes <b>no</b> membership — simulates a removed member (Rule F.1d):
    /// the user row outlives the grant by design.</summary>
    private async Task<TenantId> SeedWorkspaceUserWithoutMembershipAsync(
        string name, DateTimeOffset createdAt, string identity)
    {
        var (workspace, roles) = WorkspaceFactory.CreateWorkspaceWithDefaultRoles(name, new InstantClock(createdAt));

        await using var db = CreateContext();
        using var _ = _tenantContext.BeginScope(workspace.TenantId);

        db.Workspaces.Add(workspace);
        db.WorkspaceRoles.AddRange(roles);
        db.WorkspaceUsers.Add(WorkspaceMembershipFactory.CreateInvitedUser(workspace.TenantId, identity, createdAt).Value);

        await db.SaveChangesAsync();
        return workspace.TenantId;
    }

    [Fact]
    public async Task A_person_with_two_roles_in_one_tenant_appears_once_at_the_highest_role()
    {
        const string identity = "multi-role@acme.example";
        var tenantId = await SeedWorkspaceWithMembershipAsync(
            "Acme Procurement",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            identity,
            [WorkspaceRoleName.Legal, WorkspaceRoleName.Admin]);

        await using var db = CreateContext();
        var service = new WorkspaceDirectoryService(db, _tenantContext, new InstantClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var result = await service.ListForIdentityAsync(identity);

        var item = Assert.Single(result);
        Assert.Equal(tenantId, item.TenantId);
        Assert.Equal(WorkspaceRoleName.Admin, item.Role);
    }

    [Fact]
    public async Task A_tenant_with_no_live_membership_is_excluded_even_though_the_user_row_exists()
    {
        const string identity = "removed@acme.example";
        var stillMember = await SeedWorkspaceWithMembershipAsync(
            "Still A Member Of",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            identity,
            [WorkspaceRoleName.Procurement]);
        var removedFrom = await SeedWorkspaceUserWithoutMembershipAsync(
            "Removed From", new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), identity);

        await using var db = CreateContext();
        var service = new WorkspaceDirectoryService(db, _tenantContext, new InstantClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var result = await service.ListForIdentityAsync(identity);

        // Rule F.1d: workspace_user existing in "Removed From" is not the grant — only the live
        // membership in "Still A Member Of" is.
        var item = Assert.Single(result);
        Assert.Equal(stillMember, item.TenantId);
        Assert.NotEqual(removedFrom, item.TenantId);
    }

    [Fact]
    public async Task Caps_discovery_at_the_limit_and_audits_the_truncation()
    {
        const string identity = "prolific@acme.example";
        const int seedCount = WorkspaceDirectoryService.MaxCandidates + 5;

        for (var i = 0; i < seedCount; i++)
        {
            await SeedWorkspaceWithMembershipAsync(
                $"Workspace {i}",
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i),
                identity,
                [WorkspaceRoleName.ReadOnly]);
        }

        await using var db = CreateContext();
        var auditWriter = new RecordingAuditWriter();
        var service = new WorkspaceDirectoryService(db, _tenantContext, new InstantClock(DateTimeOffset.UtcNow), auditWriter);

        var result = await service.ListForIdentityAsync(identity);

        // Rule F.1f: capped, not merely large.
        Assert.Equal(WorkspaceDirectoryService.MaxCandidates, result.Count);

        var truncationEntry = Assert.Single(auditWriter.Entries);
        Assert.Equal("workspace.list.truncated", truncationEntry.Action);
        Assert.Equal("Workspace", truncationEntry.ResourceType);
        Assert.Equal(WorkspaceDirectoryService.MaxCandidates.ToString(), truncationEntry.ResourceId);
        Assert.Equal(identity, truncationEntry.Actor);
    }

    [Fact]
    public async Task Under_the_cap_writes_no_truncation_audit()
    {
        const string identity = "modest@acme.example";
        await SeedWorkspaceWithMembershipAsync(
            "Only Workspace", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), identity, [WorkspaceRoleName.Admin]);

        await using var db = CreateContext();
        var auditWriter = new RecordingAuditWriter();
        var service = new WorkspaceDirectoryService(db, _tenantContext, new InstantClock(DateTimeOffset.UtcNow), auditWriter);

        await service.ListForIdentityAsync(identity);

        Assert.Empty(auditWriter.Entries);
    }

    [Fact]
    public async Task Each_candidates_own_facts_are_attributed_correctly_never_swapped()
    {
        const string identity = "two-tenants@acme.example";
        var first = await SeedWorkspaceWithMembershipAsync(
            "Alpha SA",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            identity,
            [WorkspaceRoleName.Admin],
            country: "CH",
            currency: "CHF");
        var second = await SeedWorkspaceWithMembershipAsync(
            "Beta Srl",
            new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
            identity,
            [WorkspaceRoleName.ReadOnly],
            country: "IT",
            currency: "EUR");

        await using var db = CreateContext();
        var service = new WorkspaceDirectoryService(db, _tenantContext, new InstantClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var result = await service.ListForIdentityAsync(identity);

        // Sequential, never nested (ADR-025 §F.3): a nesting bug would surface here as one
        // tenant's row carrying the other's name/role/country — createdAt-ascending order also
        // pins AC-1/N2.
        Assert.Equal(2, result.Count);
        Assert.Equal(first, result[0].TenantId);
        Assert.Equal("Alpha SA", result[0].Name);
        Assert.Equal(WorkspaceRoleName.Admin, result[0].Role);
        Assert.Equal("CH", result[0].Country);
        Assert.Equal("CHF", result[0].Currency);

        Assert.Equal(second, result[1].TenantId);
        Assert.Equal("Beta Srl", result[1].Name);
        Assert.Equal(WorkspaceRoleName.ReadOnly, result[1].Role);
        Assert.Equal("IT", result[1].Country);
        Assert.Equal("EUR", result[1].Currency);
    }

    [Fact]
    public async Task An_identity_with_no_membership_anywhere_gets_an_empty_list()
    {
        await using var db = CreateContext();
        var service = new WorkspaceDirectoryService(db, _tenantContext, new InstantClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var result = await service.ListForIdentityAsync("nobody-knows-me@acme.example");

        Assert.Empty(result);
    }
}
