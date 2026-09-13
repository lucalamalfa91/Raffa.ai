using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Tenancy.Tests;

/// <summary>
/// Proves the Definition of Done for task E14/F01/US01/T01 (us-01-identity-scoped-rls, AC-2/AC-3):
/// <see cref="TenantRlsConnectionInterceptor"/>'s new `app.identity_subject` GUC (ADR-009 w14
/// footer clauses 1-5, ADR-025 §F.2, ADR-026 implication 2) is bound through a real
/// <see cref="System.Data.Common.DbParameter"/> -- never interpolated -- is independent of the
/// `app.tenant_id` claim, is genuinely unset (not empty) when no identity scope is active, and is
/// session-scoped (`set_config`'s third argument `false`) rather than transaction-local.
///
/// This task does not touch `identity-workspace.sql` or the `identity_self` policy it defines
/// (that is the sibling task E14/F01/US01/T02) -- these tests are entirely about the GUC layer,
/// independent of any RLS policy, so they run against a bare Postgres container with no
/// migrations applied. The end-to-end proof that the policy and the GUC agree is
/// E14/F03/US01/T01's.
///
/// Runs as a Postgres Testcontainers suite: CI-only, per this task's own notes -- Docker does not
/// start on the operator's machine.
/// </summary>
public sealed class TenantRlsIdentitySubjectTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Identity_containing_sql_metacharacters_is_bound_not_executed()
    {
        // T13/AC-3: '`, `;` and `--` are exactly the shape that would let a naive
        // string-interpolated SET repoint app.tenant_id if this path ever copied
        // BuildSetCommandText. All-lowercase already, so CallerIdentityContext's own
        // normalisation is a no-op here and does not interfere with the assertion below.
        const string maliciousIdentity =
            "x'; set app.tenant_id = '11111111-1111-1111-1111-111111111111'; --";

        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var callerIdentityContext = new CallerIdentityContext();

        using var tenantScope = tenantContext.BeginScope(tenantId);
        using var identityScope = callerIdentityContext.BeginIdentityScope(maliciousIdentity);

        await using var db = CreateProbe(tenantContext, callerIdentityContext);
        await db.Database.OpenConnectionAsync();
        try
        {
            // The would-be injection target: app.tenant_id must still read as the tenant this
            // connection was actually scoped to, not the attacker's GUID.
            var tenantSetting = await ReadSettingAsync(db, "app.tenant_id");
            Assert.Equal(tenantId.Value.ToString(), tenantSetting);

            // And the identity itself must have landed verbatim as data -- proving the statement
            // was never parsed as more than one command.
            var identitySetting = await ReadSettingAsync(db, "app.identity_subject");
            Assert.Equal(maliciousIdentity, identitySetting);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task Absent_identity_leaves_the_guc_unset_but_the_tenant_claim_is_unaffected()
    {
        // AC-2 / DoD(b): no BeginIdentityScope entered at all.
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var callerIdentityContext = new CallerIdentityContext();

        using var tenantScope = tenantContext.BeginScope(tenantId);

        await using var db = CreateProbe(tenantContext, callerIdentityContext);
        await db.Database.OpenConnectionAsync();
        try
        {
            var identitySetting = await ReadSettingAsync(db, "app.identity_subject");
            Assert.True(
                string.IsNullOrEmpty(identitySetting),
                $"Expected app.identity_subject to be null or empty with no identity scope active, got '{identitySetting}'.");

            // The two GUCs are independent: an absent identity must not disturb the tenant claim.
            var tenantSetting = await ReadSettingAsync(db, "app.tenant_id");
            Assert.Equal(tenantId.Value.ToString(), tenantSetting);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task No_identity_context_supplied_at_all_is_equally_fail_closed()
    {
        // Every existing *DbContextOptions.Configure call site constructs
        // TenantRlsConnectionInterceptor with only a tenant context (callerIdentityContext
        // defaults to null) until a later task wires the identity seam through DI. That default
        // path must be exactly as fail-closed as an unentered identity scope.
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        using var tenantScope = tenantContext.BeginScope(tenantId);

        await using var db = CreateProbe(tenantContext, callerIdentityContext: null);
        await db.Database.OpenConnectionAsync();
        try
        {
            var identitySetting = await ReadSettingAsync(db, "app.identity_subject");
            Assert.True(string.IsNullOrEmpty(identitySetting));
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task Identity_scope_needs_no_tenant_scope_at_all()
    {
        // AC-1 / ADR-009 w14 footer clause 1 (NW-01 discovery): the identity claim must work with
        // no tenant scope entered whatsoever -- this is the exact shape GET /api/workspaces needs.
        // The identity_self policy itself is proven by the sibling task/phase 2; this proves only
        // that the GUC layer supports the combination.
        const string identity = "discovery-caller@example.com";
        var tenantContext = new TenantContext(); // never entered
        var callerIdentityContext = new CallerIdentityContext();

        using var identityScope = callerIdentityContext.BeginIdentityScope(identity);

        await using var db = CreateProbe(tenantContext, callerIdentityContext);
        await db.Database.OpenConnectionAsync();
        try
        {
            var identitySetting = await ReadSettingAsync(db, "app.identity_subject");
            Assert.Equal(identity, identitySetting);

            var tenantSetting = await ReadSettingAsync(db, "app.tenant_id");
            Assert.True(
                string.IsNullOrEmpty(tenantSetting),
                $"Expected app.tenant_id to stay unset when only an identity scope is active, got '{tenantSetting}'.");
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task Identity_claim_is_session_scoped_and_survives_a_completed_transaction()
    {
        // DoD(c): the third set_config argument must be `false` (session-scoped), not `true`
        // (transaction-local). Proof: begin an explicit transaction after the connection is
        // already open, do something inside it, roll it back, and confirm the claim set at
        // ConnectionOpened is still there. A transaction-local value is undone at the end of
        // *any* transaction, committed or rolled back -- including the implicit one-statement
        // transaction the interceptor's own SELECT set_config(...) would have run in, which is
        // exactly how it would "silently vanish before the discovery read" (ADR-009 w14 footer
        // clause 3) if this regressed to `true`.
        const string identity = "session-scope@example.com";
        var tenantContext = new TenantContext();
        var callerIdentityContext = new CallerIdentityContext();
        using var identityScope = callerIdentityContext.BeginIdentityScope(identity);

        await using var db = CreateProbe(tenantContext, callerIdentityContext);
        await db.Database.OpenConnectionAsync();
        try
        {
            Assert.Equal(identity, await ReadSettingAsync(db, "app.identity_subject"));

            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await db.Database.ExecuteSqlRawAsync("SELECT 1");
                await transaction.RollbackAsync();
            }

            Assert.Equal(identity, await ReadSettingAsync(db, "app.identity_subject"));
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Wires <see cref="TenantRlsConnectionInterceptor"/> onto a bare, entity-less
    /// <see cref="DbContext"/> so these tests exercise only the connection-level GUC behaviour --
    /// no table, no migration, no dependency on any module's model (deliberately not
    /// `DocumentsContractsDbContextOptions`/`IdentityWorkspaceDbContextOptions`, neither of which
    /// this task may touch, and neither of which yet knows how to accept an
    /// <see cref="ICallerIdentityContext"/>).
    /// </summary>
    private ProbeDbContext CreateProbe(ITenantContext tenantContext, ICallerIdentityContext? callerIdentityContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProbeDbContext>();
        optionsBuilder.UseNpgsql(_postgres.GetConnectionString());
        optionsBuilder.AddInterceptors(new TenantRlsConnectionInterceptor(tenantContext, callerIdentityContext));
        return new ProbeDbContext(optionsBuilder.Options);
    }

    private static async Task<string?> ReadSettingAsync(ProbeDbContext db, string settingName)
    {
        var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT current_setting(@name, true)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "name";
        parameter.Value = settingName;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync();
        return result as string;
    }

    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options)
    {
    }
}

/// <summary>
/// Unit-level proof that <see cref="CallerIdentityContext"/> normalises before any value could
/// reach the interceptor's bind parameter -- independent of any database, the same way
/// <see cref="TenantContextTests"/> proves <see cref="TenantContext"/>'s mechanics in isolation.
/// </summary>
public sealed class CallerIdentityContextNormalizationTests
{
    [Fact]
    public void No_active_scope_yields_null_current()
    {
        ICallerIdentityContext context = new CallerIdentityContext();

        Assert.Null(context.Current);
    }

    [Fact]
    public void Identity_is_trimmed_and_lower_cased_before_it_reaches_the_context()
    {
        ICallerIdentityContext context = new CallerIdentityContext();

        // Matches the identity_self policy's own lower(email) comparison (ADR-025 §F.1) and
        // WorkspaceMembershipFactory.CreateInvitedUser's existing trim -- this is the single
        // point that normalises before the value ever reaches the set_config bind parameter.
        using (context.BeginIdentityScope("  MixedCase@Example.COM  "))
        {
            Assert.Equal("mixedcase@example.com", context.Current);
        }

        Assert.Null(context.Current);
    }

    [Fact]
    public void Disposing_a_scope_restores_the_previous_value()
    {
        ICallerIdentityContext context = new CallerIdentityContext();

        using (context.BeginIdentityScope("outer@example.com"))
        {
            using (context.BeginIdentityScope("inner@example.com"))
            {
                Assert.Equal("inner@example.com", context.Current);
            }

            Assert.Equal("outer@example.com", context.Current);
        }

        Assert.Null(context.Current);
    }
}
