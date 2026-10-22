using System.Linq.Expressions;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Raffa.Identity.Workspace.Tests;

/// <summary>
/// Docker-free proof that <see cref="WorkspaceDirectoryService.DiscoverForIdentityAsync"/> reads a
/// live workspace's row in the same query as the caller's roles (it used to be a second round trip
/// per workspace, on the endpoint the SPA shell calls on every navigation) without changing what it
/// returns: which workspaces are listed, which role each shows, what is skipped, and what is
/// reported as a pending invitation. The InMemory provider has no row-level security, so tenant
/// separation here rests on the explicit tenant predicates the query keeps; the RLS-enforced
/// behaviour of the same method stays with <see cref="WorkspaceDirectoryServiceTests"/> and
/// <c>Raffa.Api.Tests.WorkspaceDirectoryEndpointTests</c> (Docker). The query count is the number of
/// distinct query shapes EF compiles for the call on a fresh internal service provider, each of which
/// runs once per candidate tenant.
/// </summary>
public sealed class WorkspaceDirectoryInMemoryTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_live_workspace_is_listed_with_its_own_facts_and_costs_two_queries()
    {
        var env = new Env();
        var tenant = await env.SeedAsync("Acme", Epoch, "member@acme.example", [WorkspaceRoleName.Procurement], country: "IT", currency: "EUR");

        var result = await env.DiscoverAsync("member@acme.example");

        var item = Assert.Single(result.Workspaces);
        Assert.Equal(tenant, item.TenantId);
        Assert.Equal("Acme", item.Name);
        Assert.Equal(Epoch, item.CreatedAt);
        Assert.Equal(WorkspaceRoleName.Procurement, item.Role);
        Assert.Equal("IT", item.Country);
        Assert.Equal("EUR", item.Currency);
        Assert.Empty(result.PendingInvitations);
        // Discovery + one combined roles-and-workspace read (was discovery + roles + workspace).
        Assert.Equal(2, env.Counter.Compiled);
    }

    [Fact]
    public async Task Absent_profile_fields_stay_absent()
    {
        var env = new Env();
        await env.SeedAsync("Bare Co", Epoch, "member@bare.example", [WorkspaceRoleName.Admin]);

        var item = Assert.Single((await env.DiscoverAsync("member@bare.example")).Workspaces);

        Assert.Null(item.Country);
        Assert.Null(item.Currency);
    }

    [Fact]
    public async Task Two_roles_in_one_tenant_list_it_once_at_the_highest()
    {
        var env = new Env();
        await env.SeedAsync("Duo", Epoch, "both@duo.example", [WorkspaceRoleName.Procurement, WorkspaceRoleName.Admin]);

        var item = Assert.Single((await env.DiscoverAsync("both@duo.example")).Workspaces);

        Assert.Equal(WorkspaceRoleName.Admin, item.Role);
    }

    [Fact]
    public async Task Each_workspace_carries_its_own_row_and_the_list_is_ordered_by_creation()
    {
        var env = new Env();
        const string identity = "shared@everywhere.example";
        var second = await env.SeedAsync("Second", Epoch.AddDays(1), identity, [WorkspaceRoleName.Legal], country: "DE", currency: "EUR");
        var first = await env.SeedAsync("First", Epoch, identity, [WorkspaceRoleName.Admin], country: "FR", currency: "EUR");
        // A tenant the identity does not belong to must not bleed into either row.
        await env.SeedAsync("Stranger", Epoch.AddDays(2), "someone@else.example", [WorkspaceRoleName.Admin], country: "US", currency: "USD");

        var result = await env.DiscoverAsync(identity);

        Assert.Equal(
            [("First", first, WorkspaceRoleName.Admin, "FR"), ("Second", second, WorkspaceRoleName.Legal, "DE")],
            result.Workspaces.Select(w => (w.Name, w.TenantId, w.Role, w.Country)).ToArray());
    }

    [Fact]
    public async Task A_membership_whose_workspace_row_is_missing_is_skipped_quietly()
    {
        var env = new Env();
        const string identity = "orphan@ghost.example";
        await env.SeedAsync("Ghost", Epoch, identity, [WorkspaceRoleName.Admin], includeWorkspaceRow: false);
        await env.SeedAsync("Real", Epoch.AddDays(1), identity, [WorkspaceRoleName.Procurement]);

        var result = await env.DiscoverAsync(identity);

        var item = Assert.Single(result.Workspaces);
        Assert.Equal("Real", item.Name);
        // A live membership with no workspace is "defensive only" -- not a pending invitation either.
        Assert.Empty(result.PendingInvitations);
    }

    [Fact]
    public async Task A_removed_member_is_not_listed_and_a_live_invitation_is_reported_as_pending()
    {
        var env = new Env();
        await env.SeedAsync("Removed Co", Epoch, "gone@removed.example", roles: []);
        var invited = await env.SeedAsync("Invite Co", Epoch.AddDays(1), "new@invite.example", roles: [], invitedRole: WorkspaceRoleName.Legal);

        Assert.Empty((await env.DiscoverAsync("gone@removed.example")).Workspaces);
        Assert.Empty((await env.DiscoverAsync("gone@removed.example")).PendingInvitations);

        var result = await env.DiscoverAsync("new@invite.example");
        Assert.Empty(result.Workspaces);
        var pending = Assert.Single(result.PendingInvitations);
        Assert.Equal(invited, pending.TenantId);
        Assert.Equal("Invite Co", pending.WorkspaceName);
        Assert.Equal(WorkspaceRoleName.Legal, pending.Role);
    }

    [Fact]
    public async Task An_unknown_identity_discovers_nothing_with_one_query()
    {
        var env = new Env();
        await env.SeedAsync("Acme", Epoch, "member@acme.example", [WorkspaceRoleName.Admin]);

        var result = await env.DiscoverAsync("nobody@nowhere.example");

        Assert.Empty(result.Workspaces);
        Assert.Empty(result.PendingInvitations);
        Assert.Equal(1, env.Counter.Compiled);
    }

    private sealed class InstantClock(DateTimeOffset instant) : IClock
    {
        public DateTimeOffset UtcNow => instant;
    }

    private sealed class NoAudit : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class QueryCounter : IQueryExpressionInterceptor
    {
        private int _compiled;

        public int Compiled => _compiled;

        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        {
            Interlocked.Increment(ref _compiled);
            return queryExpression;
        }
    }

    /// <summary>One InMemory database shared by the seeding contexts and the service's context.</summary>
    private sealed class Env
    {
        private readonly InMemoryDatabaseRoot _root = new();
        private readonly string _name = $"workspace-directory-{Guid.NewGuid()}";
        private readonly TenantContext _tenantContext = new();

        public QueryCounter Counter { get; } = new();

        public async Task<WorkspaceDirectoryResult> DiscoverAsync(string identity)
        {
            await using var db = Open(Counter);
            var service = new WorkspaceDirectoryService(db, _tenantContext, new InstantClock(Epoch.AddYears(1)), new NoAudit());
            return await service.DiscoverForIdentityAsync(identity);
        }

        public async Task<TenantId> SeedAsync(
            string name,
            DateTimeOffset createdAt,
            string identity,
            IReadOnlyList<WorkspaceRoleName> roles,
            string? country = null,
            string? currency = null,
            bool includeWorkspaceRow = true,
            WorkspaceRoleName? invitedRole = null)
        {
            var (workspace, defaultRoles) = WorkspaceFactory.CreateWorkspaceWithDefaultRoles(name, new InstantClock(createdAt));
            workspace.Country = country;
            workspace.Currency = currency;

            await using var db = Open(counter: null);
            if (includeWorkspaceRow)
            {
                db.Workspaces.Add(workspace);
            }

            db.WorkspaceRoles.AddRange(defaultRoles);

            var user = WorkspaceMembershipFactory.CreateInvitedUser(workspace.TenantId, identity, createdAt).Value;
            db.WorkspaceUsers.Add(user);

            foreach (var roleName in roles)
            {
                var role = defaultRoles.Single(r => r.Name == roleName);
                db.WorkspaceMemberships.Add(WorkspaceMembershipFactory.CreateMembership(user, role, createdAt).Value);
            }

            if (invitedRole is { } invited)
            {
                db.WorkspaceInvitations.Add(new WorkspaceInvitation
                {
                    TenantId = workspace.TenantId,
                    Email = user.Email,
                    WorkspaceRoleId = defaultRoles.Single(r => r.Name == invited).Id,
                    TokenHash = "test-hash-" + Guid.NewGuid().ToString("N"),
                    InvitedBy = "admin@acme.example",
                    CreatedAt = createdAt,
                    ExpiresAt = Epoch.AddYears(2),
                });
            }

            await db.SaveChangesAsync();
            return workspace.TenantId;
        }

        private IdentityWorkspaceDbContext Open(QueryCounter? counter)
        {
            var builder = new DbContextOptionsBuilder<IdentityWorkspaceDbContext>()
                .UseInMemoryDatabase(_name, _root)
                // A fresh internal service provider per context, so no query compiled by an earlier
                // call is cached and every distinct query shape of a call is counted.
                .EnableServiceProviderCaching(false)
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
            if (counter is not null)
            {
                builder.AddInterceptors(counter);
            }

            return new IdentityWorkspaceDbContext(builder.Options);
        }
    }
}
