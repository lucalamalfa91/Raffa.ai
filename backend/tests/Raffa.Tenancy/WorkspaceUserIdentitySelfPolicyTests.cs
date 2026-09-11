using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Tenancy.Tests;

/// <summary>
/// Proves the Definition of Done for task E14/F01/US01/T02 (story us-01-identity-scoped-rls,
/// AC-1/AC-2): migration <c>AddWorkspaceUserIdentitySelfReadPolicy</c>'s `identity_self` policy
/// lets a connection with no active tenant scope read *only* the caller's own `workspace_user`
/// rows -- across every tenant that caller has a row in, which is exactly what makes
/// `GET /api/workspaces` discovery possible (ADR-026 §D1 phase 1) -- while the widening stays
/// confined to that one table (T1c/T1d, ADR-025 §H) and an absent or empty
/// `app.identity_subject` claim sees nothing at all (AC-2, ADR-025 Rule F.1b) -- the same
/// fail-closed shape <see cref="TenantRlsCrossTenantIsolationTests"/> already proves for
/// `app.tenant_id`.
///
/// `app.identity_subject` is set directly on the connection with a bind parameter
/// (`SELECT set_config('app.identity_subject', @identity, false)`, ADR-025 Rule F.2b -- never the
/// interpolating `SET` form) rather than through
/// <see cref="TenantRlsConnectionInterceptor"/>: wiring that GUC into the interceptor is task
/// E14/F01/US01/T01's sole ownership (this task's own "do not touch" list), so this test proves
/// the migration's SQL policy directly, independent of that interceptor and its own tests.
///
/// Runs every assertion through a dedicated, deliberately unprivileged Postgres role -- see
/// <see cref="TenantRlsCrossTenantIsolationTests"/>'s own doc comment for why a superuser
/// connection (Testcontainers' bootstrap role) would make this pass vacuously.
/// </summary>
public sealed class WorkspaceUserIdentitySelfPolicyTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_identity_self_app";
    private const string AppRolePassword = "raffa_identity_self_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<IdentityWorkspaceDbContext>();
        IdentityWorkspaceDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());

        await using (var adminDb = new IdentityWorkspaceDbContext(adminOptions.Options))
        {
            // Applies Initial + AddTenantRowLevelSecurity + this task's own three migrations
            // (AddWorkspaceUserIdentitySelfReadPolicy, AddWorkspaceProfileColumns,
            // AddWorkspaceInvitation).
            await adminDb.Database.MigrateAsync();

            // A non-owner, non-superuser, NOBYPASSRLS role: see the type doc comment for why this
            // is required for the test to mean anything.
            await adminDb.Database.ExecuteSqlRawAsync(
                $"""
                CREATE ROLE {AppRoleName} LOGIN PASSWORD '{AppRolePassword}' NOSUPERUSER NOBYPASSRLS;
                GRANT USAGE ON SCHEMA public TO {AppRoleName};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRoleName};
                """);
        }

        _appConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = AppRoleName,
            Password = AppRolePassword,
        }.ConnectionString;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Identity_scope_sees_only_its_own_workspace_user_rows_across_tenants()
    {
        var tenantA1 = TenantId.New();
        var tenantA2 = TenantId.New();
        var tenantB = TenantId.New();
        const string identityA = "a@example.com";

        await SeedUserAsync(tenantA1, identityA);
        await SeedUserAsync(tenantA2, identityA);
        await SeedUserAsync(tenantB, "b@example.com");

        await using var db = await OpenIdentityScopedContextAsync(identityA);

        // T1c (ADR-025 §H): with no app.tenant_id and app.identity_subject = A, a direct SELECT on
        // workspace_user returns only A's own rows -- both of them, because identity_self carries
        // no tenant_id condition at all; that is exactly what makes cross-tenant discovery
        // possible (ADR-026 D1 phase 1: "select tenant_id from workspace_user for the matching
        // identity").
        var visible = await db.WorkspaceUsers.ToListAsync();

        Assert.Equal(2, visible.Count);
        Assert.All(visible, u => Assert.Equal(identityA, u.Email));
        Assert.Contains(visible, u => u.TenantId == tenantA1);
        Assert.Contains(visible, u => u.TenantId == tenantA2);
    }

    [Fact]
    public async Task Identity_scope_sees_zero_rows_on_every_other_tenant_table()
    {
        const string identity = "a@example.com";
        var tenant = await SeedWorkspaceGraphAsync();
        await SeedUserAsync(tenant, identity);

        await using var db = await OpenIdentityScopedContextAsync(identity);

        // T1d: the widening is confined to workspace_user -- workspace, workspace_role and
        // workspace_membership keep exactly the one tenant_isolation policy
        // AddTenantRowLevelSecurity already gave them, which evaluates false with no
        // app.tenant_id, so they stay fail-closed even on a connection that can see its own
        // workspace_user rows. Proven against real data (SeedWorkspaceGraphAsync), not an empty
        // table.
        Assert.Empty(await db.Workspaces.ToListAsync());
        Assert.Empty(await db.WorkspaceRoles.ToListAsync());
        Assert.Empty(await db.WorkspaceMemberships.ToListAsync());
    }

    [Fact]
    public async Task No_identity_claim_sees_zero_workspace_user_rows()
    {
        await SeedUserAsync(TenantId.New(), "someone-else@example.com");

        // app.identity_subject never set on this connection: current_setting(..., true) returns
        // NULL, nullif(...) IS NOT NULL is false, so identity_self denies every row -- fail closed
        // (AC-2).
        await using var db = await OpenIdentityScopedContextAsync(identity: null);

        Assert.Empty(await db.WorkspaceUsers.ToListAsync());
    }

    [Fact]
    public async Task Empty_identity_claim_sees_zero_workspace_user_rows()
    {
        await SeedUserAsync(TenantId.New(), "someone-else@example.com");

        // app.identity_subject explicitly set to '': nullif(..., '') folds it to SQL NULL, the
        // same fail-closed path as an entirely absent claim (AC-2).
        await using var db = await OpenIdentityScopedContextAsync(identity: "");

        Assert.Empty(await db.WorkspaceUsers.ToListAsync());
    }

    /// <summary>
    /// Opens a connection with <b>no</b> <see cref="ITenantContext"/> supplied -- so
    /// <see cref="TenantRlsConnectionInterceptor"/> is not attached and `app.tenant_id` stays
    /// unset, matching a caller with no active tenant scope (ADR-025 §F.1's own discovery
    /// precondition) -- then sets `app.identity_subject` directly with a bind parameter, the
    /// production shape ADR-025 Rule F.2b mandates and task E14/F01/US01/T01 wires into the
    /// interceptor. <paramref name="identity"/> of <see langword="null"/> leaves the GUC entirely
    /// unset (the "absent claim" case); an empty string sets it to `''` (the "empty claim" case).
    /// </summary>
    private async Task<IdentityWorkspaceDbContext> OpenIdentityScopedContextAsync(string? identity)
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdentityWorkspaceDbContext>();
        IdentityWorkspaceDbContextOptions.Configure(optionsBuilder, _appConnectionString);
        var db = new IdentityWorkspaceDbContext(optionsBuilder.Options);

        await db.Database.OpenConnectionAsync();

        if (identity is not null)
        {
            var connection = db.Database.GetDbConnection();
            var command = connection.CreateCommand();
            await using var _ = command.ConfigureAwait(false);
            command.CommandText = "SELECT set_config('app.identity_subject', @identity, false)";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "identity";
            parameter.Value = identity;
            command.Parameters.Add(parameter);
            await command.ExecuteNonQueryAsync();
        }

        return db;
    }

    private IdentityWorkspaceDbContext CreateAppContext(ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdentityWorkspaceDbContext>();
        IdentityWorkspaceDbContextOptions.Configure(optionsBuilder, _appConnectionString, tenantContext);
        return new IdentityWorkspaceDbContext(optionsBuilder.Options);
    }

    private async Task SeedUserAsync(TenantId tenantId, string email)
    {
        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(tenantId);
        await using var db = CreateAppContext(tenantContext);

        db.WorkspaceUsers.Add(new WorkspaceUser
        {
            TenantId = tenantId,
            Email = email,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// One minimal, fully-formed row in each of the three tables `identity_self` must never widen
    /// (T1d) -- <c>workspace</c>, <c>workspace_role</c> and <c>workspace_membership</c> -- so
    /// <see cref="Identity_scope_sees_zero_rows_on_every_other_tenant_table"/>'s zero-rows
    /// assertion proves fail-closed against real data. Constructs via
    /// <see cref="WorkspaceFactory"/> (not an object initializer) so the workspace's own
    /// `Id == TenantId` invariant holds, matching <c>IdentityWorkspaceMigrationTests</c>'s own
    /// precedent.
    /// </summary>
    private async Task<TenantId> SeedWorkspaceGraphAsync()
    {
        var (workspace, roles) = WorkspaceFactory.CreateWorkspaceWithDefaultRoles(
            "Other Tenant's Workspace", SystemClock.Instance);
        var adminRole = roles.Single(r => r.Name == WorkspaceRoleName.Admin);

        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(workspace.TenantId);
        await using var db = CreateAppContext(tenantContext);

        var member = new WorkspaceUser
        {
            TenantId = workspace.TenantId,
            Email = "member@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Workspaces.Add(workspace);
        db.WorkspaceRoles.AddRange(roles);
        db.WorkspaceUsers.Add(member);
        db.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            TenantId = workspace.TenantId,
            WorkspaceUserId = member.Id,
            WorkspaceRoleId = adminRole.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return workspace.TenantId;
    }
}
