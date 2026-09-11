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
/// AC-4; T5): migration <c>AddWorkspaceInvitation</c>'s `tenant_isolation` policy on
/// `workspace_invitation` behaves exactly like every other tenant table's -- outside any scope
/// reads return zero rows and a write for a tenant other than the active scope is rejected by
/// `WITH CHECK`, not silently accepted (ADR-025 §F.4, ADR-009). This table gets **no**
/// `identity_self` policy (that widening is confined to `workspace_user`), so unlike
/// <see cref="WorkspaceUserIdentitySelfPolicyTests"/> there is nothing to prove beyond ordinary
/// tenant isolation -- the same shape <see cref="TenantRlsCrossTenantIsolationTests"/> and
/// <see cref="Raffa.Identity.Workspace.Tests.WorkspaceRlsCrossTenantIsolationTests"/> already
/// proved for the four pre-existing tables, applied here to the new one.
///
/// Runs every assertion through a dedicated, deliberately unprivileged Postgres role -- see
/// <see cref="TenantRlsCrossTenantIsolationTests"/>'s own doc comment for why a superuser
/// connection would make this pass vacuously.
/// </summary>
public sealed class WorkspaceInvitationRlsTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_invitation_app";
    private const string AppRolePassword = "raffa_invitation_app_test_password";

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
    public async Task Outside_any_scope_reads_return_zero_rows()
    {
        await SeedInvitationAsync(TenantId.New());

        // No ITenantContext supplied: the interceptor is not attached, app.tenant_id stays unset,
        // and tenant_id = NULL is never true -- fail closed (T5), exactly like every other tenant
        // table with no active scope.
        await using var db = CreateAppContext();

        Assert.Empty(await db.WorkspaceInvitations.ToListAsync());
    }

    [Fact]
    public async Task Insert_outside_any_scope_is_rejected_by_with_check()
    {
        // A real, already-committed role -- seeded in its own tenant scope first -- so the
        // rejection below can only be the RLS WITH CHECK, never an incidental foreign-key failure
        // on a fabricated WorkspaceRoleId.
        var tenantId = TenantId.New();
        var roleId = await SeedRoleAsync(tenantId);

        await using var db = CreateAppContext();

        db.WorkspaceInvitations.Add(new WorkspaceInvitation
        {
            TenantId = tenantId,
            Email = "invitee@example.com",
            WorkspaceRoleId = roleId,
            TokenHash = RandomTokenHash(),
            InvitedBy = "admin@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        });

        // T5 / ADR-025 §F.4: WITH CHECK requires tenant_id = the active claim; with no scope the
        // claim is NULL and no tenant_id value can satisfy `= NULL`, so the insert is rejected
        // outright rather than silently landing unscoped.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Tenant_cannot_read_another_tenants_invitations()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();

        await SeedInvitationAsync(tenantA);
        await SeedInvitationAsync(tenantB);

        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(tenantA);
        await using var db = CreateAppContext(tenantContext);

        // The tenant_isolation policy shipped in the same migration as the table (ADR-009): tenant
        // B's row exists (seeded above, over the same table) but is invisible on a connection
        // scoped to tenant A.
        var visible = await db.WorkspaceInvitations.ToListAsync();
        var row = Assert.Single(visible);
        Assert.Equal(tenantA, row.TenantId);
    }

    private IdentityWorkspaceDbContext CreateAppContext(ITenantContext? tenantContext = null)
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdentityWorkspaceDbContext>();
        IdentityWorkspaceDbContextOptions.Configure(optionsBuilder, _appConnectionString, tenantContext);
        return new IdentityWorkspaceDbContext(optionsBuilder.Options);
    }

    private async Task SeedInvitationAsync(TenantId tenantId)
    {
        var roleId = await SeedRoleAsync(tenantId);

        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(tenantId);
        await using var db = CreateAppContext(tenantContext);

        db.WorkspaceInvitations.Add(new WorkspaceInvitation
        {
            TenantId = tenantId,
            Email = "invitee@example.com",
            WorkspaceRoleId = roleId,
            TokenHash = RandomTokenHash(),
            InvitedBy = "admin@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A real, committed <see cref="WorkspaceRole"/> row for <paramref name="tenantId"/>.
    /// <see cref="WorkspaceInvitation.WorkspaceRoleId"/> is a real FK
    /// (<c>WorkspaceInvitationConfiguration</c>), so every test here seeds one first -- a
    /// fabricated id would fail on the foreign key rather than on RLS, and prove nothing about
    /// tenant isolation.
    /// </summary>
    private async Task<EntityId> SeedRoleAsync(TenantId tenantId)
    {
        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(tenantId);
        await using var db = CreateAppContext(tenantContext);

        var role = new WorkspaceRole
        {
            TenantId = tenantId,
            Name = WorkspaceRoleName.Procurement,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.WorkspaceRoles.Add(role);
        await db.SaveChangesAsync();

        return role.Id;
    }

    /// <summary>Stands in for a real SHA-256 digest (ADR-025 Rule C2) -- only its uniqueness matters here.</summary>
    private static string RandomTokenHash() =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()));
}
