using System.Security.Cryptography;
using System.Text;
using Raffa.Identity.Workspace.Application;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace Raffa.Identity.Workspace.Tests;

/// <summary>
/// Task E15/F01/US01/T01 (wave w14; ADR-025 §C, §D.1/§D.3, ADR-026 §D6): the token lifecycle's own
/// facts — 256-bit CSPRNG secret, SHA-256 at rest, 7-day absolute expiry, single use, a
/// site-relative accept link, and <see cref="NullInvitationMailer"/>'s honest
/// <c>mailDelivered: false</c> — proven against a real, migrated Postgres instance (the unique
/// index and the expiry comparison are real database/IClock facts, not stand-ins), the same shape
/// <c>WorkspaceMembershipServiceTests</c> already uses for the sibling invite/sign-in flow.
/// </summary>
public sealed class WorkspaceInvitationServiceTests : IAsyncLifetime
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

    private WorkspaceInvitationService CreateInvitationService(
        IdentityWorkspaceDbContext db, IClock clock, IInvitationMailer mailer, IAuditWriter? auditWriter = null)
    {
        var membershipService = new WorkspaceMembershipService(db, _tenantContext, clock, auditWriter ?? new NoOpAuditWriter());
        // The module's own defaults (task E17/F01/US01/T01): no directory write, a site-relative
        // link -- exactly the w14 shape every test below was written against.
        return new WorkspaceInvitationService(
            db, _tenantContext, clock, auditWriter ?? new NoOpAuditWriter(), mailer, membershipService,
            new NullGuestProvisioner(), new InvitationOptions());
    }

    private async Task<TenantId> SeedWorkspaceAsync(IClock clock)
    {
        var (workspace, roles) = WorkspaceFactory.CreateWorkspaceWithDefaultRoles("Acme Procurement", clock);

        await using var db = CreateContext();
        using var _ = _tenantContext.BeginScope(workspace.TenantId);
        db.Workspaces.Add(workspace);
        db.WorkspaceRoles.AddRange(roles);
        await db.SaveChangesAsync();

        return workspace.TenantId;
    }

    [Fact]
    public async Task Issue_mints_a_256_bit_base64url_secret_and_stores_only_its_sha256_hash()
    {
        var clock = FixedClock.Instance;
        var tenantId = await SeedWorkspaceAsync(clock);
        var service = CreateInvitationService(CreateContext(), clock, new NullInvitationMailer(NullLogger<NullInvitationMailer>.Instance));

        var result = await service.IssueAsync(tenantId, "invitee@acme.example", WorkspaceRoleName.Procurement, "admin@acme.example");

        Assert.True(result.IsSuccess);
        var token = ExtractToken(result.AcceptUrl!);
        var parts = token.Split('.', 2);
        Assert.Equal(2, parts.Length);
        Assert.Equal($"{tenantId.Value:N}", parts[0]);

        // 256 bits = 32 bytes; base64url has no padding, so length is ceil(32 * 4 / 3) = 43.
        var secret = parts[1];
        Assert.Equal(43, secret.Length);
        Assert.DoesNotContain('+', secret);
        Assert.DoesNotContain('/', secret);
        Assert.DoesNotContain('=', secret);

        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

        using var _ = _tenantContext.BeginScope(tenantId);
        await using var readDb = CreateContext();
        var invitation = await readDb.WorkspaceInvitations.SingleAsync(i => i.Id == result.Invitation!.Id);
        Assert.Equal(expectedHash, invitation.TokenHash);

        // Rule C2 / task DoD line: the raw token never appears anywhere the row could leak it from.
        Assert.DoesNotContain(token, invitation.TokenHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Issue_sets_a_seven_day_absolute_expiry_evaluated_against_the_clock()
    {
        var now = new DateTimeOffset(2026, 9, 11, 9, 0, 0, TimeSpan.Zero);
        var clock = new FixedClock(now);
        var tenantId = await SeedWorkspaceAsync(clock);
        var service = CreateInvitationService(CreateContext(), clock, new NullInvitationMailer(NullLogger<NullInvitationMailer>.Instance));

        var result = await service.IssueAsync(tenantId, "expiry@acme.example", WorkspaceRoleName.Legal, "admin@acme.example");

        Assert.True(result.IsSuccess);
        Assert.Equal(now.AddDays(7), result.Invitation!.ExpiresAt);
    }

    [Fact]
    public async Task Issue_returns_a_site_relative_accept_url_with_the_token_as_a_fragment()
    {
        var clock = FixedClock.Instance;
        var tenantId = await SeedWorkspaceAsync(clock);
        var service = CreateInvitationService(CreateContext(), clock, new NullInvitationMailer(NullLogger<NullInvitationMailer>.Instance));

        var result = await service.IssueAsync(tenantId, "frag@acme.example", WorkspaceRoleName.Admin, "admin@acme.example");

        Assert.True(result.IsSuccess);
        // ADR-025 Rule C9: site-relative, and a fragment -- never an absolute URL, never a query
        // string (a '?' here would mean the token rides Referer headers and access logs).
        Assert.StartsWith("/invite/accept#", result.AcceptUrl);
        Assert.DoesNotContain('?', result.AcceptUrl!);
        Assert.False(Uri.IsWellFormedUriString(result.AcceptUrl, UriKind.Absolute));
    }

    [Fact]
    public async Task Null_invitation_mailer_returns_false_and_the_issue_response_carries_that_fact()
    {
        var clock = FixedClock.Instance;
        var tenantId = await SeedWorkspaceAsync(clock);
        var service = CreateInvitationService(CreateContext(), clock, new NullInvitationMailer(NullLogger<NullInvitationMailer>.Instance));

        var result = await service.IssueAsync(tenantId, "nomail@acme.example", WorkspaceRoleName.Finance, "admin@acme.example");

        Assert.True(result.IsSuccess);
        Assert.False(result.MailDelivered);
    }

    [Fact]
    public async Task A_second_accept_is_refused_single_use()
    {
        var clock = FixedClock.Instance;
        var tenantId = await SeedWorkspaceAsync(clock);
        var service = CreateInvitationService(CreateContext(), clock, new NullInvitationMailer(NullLogger<NullInvitationMailer>.Instance));

        var issued = await service.IssueAsync(tenantId, "single-use@acme.example", WorkspaceRoleName.Procurement, "admin@acme.example");
        Assert.True(issued.IsSuccess);
        var token = ExtractToken(issued.AcceptUrl!);

        var firstAccept = await service.AcceptAsync(token, "single-use@acme.example");
        Assert.True(firstAccept.IsSuccess);

        var secondAccept = await service.AcceptAsync(token, "single-use@acme.example");
        Assert.False(secondAccept.IsSuccess);
        Assert.Equal(MembershipOperationStatus.Conflict, secondAccept.Status);

        using var _ = _tenantContext.BeginScope(tenantId);
        await using var readDb = CreateContext();
        var memberships = await readDb.WorkspaceMemberships.ToListAsync();
        Assert.Single(memberships);
    }

    // ----- AcceptForIdentityAsync (fix 2026-09-14): the identity-keyed counterpart, no token -----

    [Fact]
    public async Task AcceptForIdentityAsync_grants_membership_for_a_live_invitation_matching_the_identity()
    {
        var clock = FixedClock.Instance;
        var tenantId = await SeedWorkspaceAsync(clock);
        var service = CreateInvitationService(CreateContext(), clock, new NullInvitationMailer(NullLogger<NullInvitationMailer>.Instance));

        var issued = await service.IssueAsync(tenantId, "no-token-needed@acme.example", WorkspaceRoleName.Procurement, "admin@acme.example");
        Assert.True(issued.IsSuccess);

        var result = await service.AcceptForIdentityAsync(tenantId, "no-token-needed@acme.example");

        Assert.True(result.IsSuccess);
        Assert.Equal(tenantId, result.WorkspaceId);
        Assert.Equal(WorkspaceRoleName.Procurement, result.Role);

        using var _ = _tenantContext.BeginScope(tenantId);
        await using var readDb = CreateContext();
        var invitation = await readDb.WorkspaceInvitations.SingleAsync(i => i.TenantId == tenantId && i.Email == "no-token-needed@acme.example");
        Assert.NotNull(invitation.AcceptedAt);
        var user = await readDb.WorkspaceUsers.SingleAsync(u => u.TenantId == tenantId && u.Email == "no-token-needed@acme.example");
        Assert.True(await readDb.WorkspaceMemberships.AnyAsync(m => m.WorkspaceUserId == user.Id));
    }

    [Fact]
    public async Task AcceptForIdentityAsync_with_no_live_invitation_for_this_identity_is_not_found()
    {
        var clock = FixedClock.Instance;
        var tenantId = await SeedWorkspaceAsync(clock);
        var service = CreateInvitationService(CreateContext(), clock, new NullInvitationMailer(NullLogger<NullInvitationMailer>.Instance));

        var result = await service.AcceptForIdentityAsync(tenantId, "nobody-invited-me@acme.example");

        Assert.False(result.IsSuccess);
        Assert.Equal(MembershipOperationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task AcceptForIdentityAsync_a_second_call_after_success_is_not_found_not_conflict()
    {
        // Deliberate difference from AcceptAsync's own 409 (A_second_accept_is_refused_single_use):
        // there is no token to re-present here, so a second call simply finds no live invitation left
        // -- see this method's own doc comment, "Deliberately no 409".
        var clock = FixedClock.Instance;
        var tenantId = await SeedWorkspaceAsync(clock);
        var service = CreateInvitationService(CreateContext(), clock, new NullInvitationMailer(NullLogger<NullInvitationMailer>.Instance));

        var issued = await service.IssueAsync(tenantId, "repeat-no-token@acme.example", WorkspaceRoleName.Legal, "admin@acme.example");
        Assert.True(issued.IsSuccess);

        var first = await service.AcceptForIdentityAsync(tenantId, "repeat-no-token@acme.example");
        Assert.True(first.IsSuccess);

        var second = await service.AcceptForIdentityAsync(tenantId, "repeat-no-token@acme.example");
        Assert.False(second.IsSuccess);
        Assert.Equal(MembershipOperationStatus.NotFound, second.Status);

        using var _ = _tenantContext.BeginScope(tenantId);
        await using var readDb = CreateContext();
        var user = await readDb.WorkspaceUsers.SingleAsync(u => u.TenantId == tenantId && u.Email == "repeat-no-token@acme.example");
        Assert.Single(await readDb.WorkspaceMemberships.Where(m => m.WorkspaceUserId == user.Id).ToListAsync());
    }

    [Fact]
    public async Task AcceptForIdentityAsync_an_expired_invitation_is_expired_not_not_found()
    {
        var now = new DateTimeOffset(2026, 9, 11, 9, 0, 0, TimeSpan.Zero);
        var clock = new FixedClock(now);
        var tenantId = await SeedWorkspaceAsync(clock);
        var service = CreateInvitationService(CreateContext(), clock, new NullInvitationMailer(NullLogger<NullInvitationMailer>.Instance));

        var issued = await service.IssueAsync(tenantId, "expired-no-token@acme.example", WorkspaceRoleName.Legal, "admin@acme.example");
        Assert.True(issued.IsSuccess);

        var laterService = CreateInvitationService(
            CreateContext(), new FixedClock(now.AddDays(8)), new NullInvitationMailer(NullLogger<NullInvitationMailer>.Instance));

        var result = await laterService.AcceptForIdentityAsync(tenantId, "expired-no-token@acme.example");

        Assert.False(result.IsSuccess);
        Assert.Equal(MembershipOperationStatus.Expired, result.Status);
    }

    [Fact]
    public async Task AcceptForIdentityAsync_never_grants_in_a_tenant_where_this_identity_holds_no_invitation()
    {
        var clock = FixedClock.Instance;
        var tenantWithInvitation = await SeedWorkspaceAsync(clock);
        var tenantWithout = await SeedWorkspaceAsync(clock);
        var service = CreateInvitationService(CreateContext(), clock, new NullInvitationMailer(NullLogger<NullInvitationMailer>.Instance));

        var issued = await service.IssueAsync(tenantWithInvitation, "elsewhere@acme.example", WorkspaceRoleName.Admin, "admin@acme.example");
        Assert.True(issued.IsSuccess);

        // Same identity, but the ROUTE tenant (tenantWithout) is not where their invitation lives --
        // this is the "caller-supplied tenantId is never trusted, always re-verified" guarantee: a
        // real invitation existing SOMEWHERE for this identity must not leak a grant into a tenant
        // that never invited them.
        var result = await service.AcceptForIdentityAsync(tenantWithout, "elsewhere@acme.example");

        Assert.False(result.IsSuccess);
        Assert.Equal(MembershipOperationStatus.NotFound, result.Status);

        using var _ = _tenantContext.BeginScope(tenantWithout);
        await using var readDb = CreateContext();
        Assert.False(await readDb.WorkspaceMemberships.AnyAsync(m => m.TenantId == tenantWithout));
    }

    [Fact]
    public async Task A_token_whose_prefix_is_not_a_guid_is_refused_with_no_scope_entered()
    {
        var clock = FixedClock.Instance;
        var service = CreateInvitationService(CreateContext(), clock, new NullInvitationMailer(NullLogger<NullInvitationMailer>.Instance));

        var result = await service.PreAcceptAsync("not-a-guid.some-secret");

        Assert.False(result.IsSuccess);
        Assert.Equal(MembershipOperationStatus.NotFound, result.Status);
        // C4's own point: TryParseToken never touches ITenantContext at all on a parse failure --
        // proven here by the ambient scope staying exactly what it was before the call (null).
        Assert.Null(_tenantContext.Current);
    }

    [Fact]
    public async Task An_expired_invitation_returns_expired_not_not_found()
    {
        var now = new DateTimeOffset(2026, 9, 11, 9, 0, 0, TimeSpan.Zero);
        var clock = new FixedClock(now);
        var tenantId = await SeedWorkspaceAsync(clock);
        var service = CreateInvitationService(CreateContext(), clock, new NullInvitationMailer(NullLogger<NullInvitationMailer>.Instance));

        var issued = await service.IssueAsync(tenantId, "expired@acme.example", WorkspaceRoleName.Legal, "admin@acme.example");
        var token = ExtractToken(issued.AcceptUrl!);

        var laterService = CreateInvitationService(
            CreateContext(), new FixedClock(now.AddDays(8)), new NullInvitationMailer(NullLogger<NullInvitationMailer>.Instance));

        var preview = await laterService.PreAcceptAsync(token);
        Assert.Equal(MembershipOperationStatus.Expired, preview.Status);

        var accept = await laterService.AcceptAsync(token, "expired@acme.example");
        Assert.Equal(MembershipOperationStatus.Expired, accept.Status);
    }

    [Fact]
    public async Task An_email_mismatch_on_accept_is_forbidden_and_never_echoes_the_invited_address()
    {
        var clock = FixedClock.Instance;
        var tenantId = await SeedWorkspaceAsync(clock);
        var service = CreateInvitationService(CreateContext(), clock, new NullInvitationMailer(NullLogger<NullInvitationMailer>.Instance));

        var issued = await service.IssueAsync(tenantId, "invited@acme.example", WorkspaceRoleName.Procurement, "admin@acme.example");
        var token = ExtractToken(issued.AcceptUrl!);

        var result = await service.AcceptAsync(token, "someone-else@acme.example");

        Assert.False(result.IsSuccess);
        Assert.Equal(MembershipOperationStatus.Forbidden, result.Status);
        Assert.DoesNotContain("invited@acme.example", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractToken(string acceptUrl) => acceptUrl["/invite/accept#".Length..];

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public static readonly FixedClock Instance = new(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));

        public DateTimeOffset UtcNow => now;
    }

    private sealed class NoOpAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
