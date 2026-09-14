using Raffa.Identity.Workspace.Application;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Identity.Workspace.Tests;

/// <summary>
/// Task E17/F01/US01/T01 (wave w15, NW-67/NW-68; ADR-025 §J, ADR-026 w15 footers §1–§9), proven
/// against a real, migrated Postgres instance — the partial unique index that makes re-issue by
/// replacement necessary, and the bound subject the accept matches on, are real database facts:
/// guest first and row second, with the guest's object id bound into
/// <c>workspace_user.external_subject_id</c>; a provisioning failure that leaves nothing behind;
/// <c>NotConfigured</c> behaving exactly like w14; the accept's resolution order (bound <c>oid</c> →
/// <c>email</c> claim → refuse); the cap on distinct live addresses; and <c>deliveryOutcome</c> being
/// nothing but the mailer's own bool.
/// </summary>
public sealed class InviteProvisioningOrderingTests : IAsyncLifetime
{
    private const string AcceptUrlBase = "https://app.dev.raffa.example";
    private const string Admin = "admin@acme.example";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly TenantContext _tenantContext = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Guest_is_provisioned_before_any_row_is_written_and_its_object_id_is_bound_to_the_invited_user()
    {
        var tenantId = await SeedWorkspaceAsync();
        var log = new List<string>();
        var provisioner = new ScriptedProvisioner(GuestProvisioningResult.Provisioned("guest-oid-1"), log);
        var audit = new RecordingAuditWriter(log);
        var service = CreateService(CreateContext(), provisioner, new ScriptedMailer(configured: false, delivered: false, log), audit);

        var result = await service.IssueAsync(tenantId, "Invitee@Acme.example", WorkspaceRoleName.Procurement, Admin);

        Assert.True(result.IsSuccess);
        Assert.True(result.IdentityProvisioned);
        // §J.2a: the directory call precedes the first write -- the audit row the write produces is
        // the first thing after it in the log.
        Assert.Equal("provision:invitee@acme.example", log[0]);
        Assert.Equal("audit:workspace.invitation.issued", log[1]);
        Assert.Contains("audit:" + WorkspaceInvitationService.GuestProvisionedAuditAction, log);

        // §J.3b: the guest's object id -- the oid its token will carry -- is bound at invite time.
        using var _ = _tenantContext.BeginScope(tenantId);
        await using var readDb = CreateContext();
        var user = await readDb.WorkspaceUsers.SingleAsync(u => u.Email == "invitee@acme.example");
        Assert.Equal("guest-oid-1", user.ExternalSubjectId);
        Assert.Single(await readDb.WorkspaceInvitations.Where(i => i.Email == "invitee@acme.example").ToListAsync());

        // ADR-026 w15 footer §4: absolute with a base, the token still after the fragment.
        Assert.StartsWith(AcceptUrlBase + "/invite/accept#", result.AcceptUrl);
        Assert.DoesNotContain('?', result.AcceptUrl!);
        // No transport: the outcome says so, and mailDelivered agrees (§8's biconditional).
        Assert.Equal(InvitationDeliveryOutcome.NoTransport, result.DeliveryOutcome);
        Assert.False(result.MailDelivered);
    }

    [Fact]
    public async Task A_provisioning_failure_aborts_the_invitation_leaving_no_row_no_token_and_no_mail()
    {
        var tenantId = await SeedWorkspaceAsync();
        var log = new List<string>();
        var provisioner = new ScriptedProvisioner(GuestProvisioningResult.Failed(GuestProvisioningFailureReason.ConsentMissing, "req-42"), log);
        var mailer = new ScriptedMailer(configured: true, delivered: true, log);
        var audit = new RecordingAuditWriter(log);
        var service = CreateService(CreateContext(), provisioner, mailer, audit);

        var result = await service.IssueAsync(tenantId, "blocked@acme.example", WorkspaceRoleName.Procurement, Admin);

        Assert.False(result.IsSuccess);
        Assert.Equal(MembershipOperationStatus.ProvisioningFailed, result.Status);
        Assert.Equal(GuestProvisioningFailureReason.ConsentMissing, result.ProvisioningFailureReason);
        Assert.Null(result.AcceptUrl);
        Assert.Null(result.Invitation);
        Assert.DoesNotContain(log, entry => entry.StartsWith("mail:", StringComparison.Ordinal));

        // §J.7: one audit row with the named reason and the request id -- never a raw body.
        var failure = Assert.Single(audit.Entries, e => e.Action == WorkspaceInvitationService.GuestProvisioningFailedAuditAction);
        Assert.Contains("reason=consent_missing", failure.Detail);
        Assert.Contains("request-id=req-42", failure.Detail);

        using var _ = _tenantContext.BeginScope(tenantId);
        await using var readDb = CreateContext();
        Assert.Empty(await readDb.WorkspaceInvitations.ToListAsync());
        Assert.Empty(await readDb.WorkspaceUsers.Where(u => u.Email == "blocked@acme.example").ToListAsync());
    }

    [Fact]
    public async Task Not_configured_behaves_exactly_like_w14_link_only_no_subject_bound_relative_link()
    {
        var tenantId = await SeedWorkspaceAsync();
        var log = new List<string>();
        var service = CreateService(
            CreateContext(), new NullGuestProvisioner(), new ScriptedMailer(configured: false, delivered: false, log),
            new RecordingAuditWriter(log), new InvitationOptions());

        var result = await service.IssueAsync(tenantId, "plain@acme.example", WorkspaceRoleName.Legal, Admin);

        Assert.True(result.IsSuccess);
        Assert.False(result.IdentityProvisioned);
        Assert.StartsWith("/invite/accept#", result.AcceptUrl);
        Assert.DoesNotContain("audit:" + WorkspaceInvitationService.GuestProvisionedAuditAction, log);

        using var _ = _tenantContext.BeginScope(tenantId);
        await using var readDb = CreateContext();
        Assert.Null((await readDb.WorkspaceUsers.SingleAsync(u => u.Email == "plain@acme.example")).ExternalSubjectId);
    }

    [Fact]
    public async Task Accept_matches_the_bound_oid_exactly_and_never_the_mangled_upn_or_a_stranger()
    {
        var tenantId = await SeedWorkspaceAsync();
        var log = new List<string>();
        var service = CreateService(
            CreateContext(), new ScriptedProvisioner(GuestProvisioningResult.AlreadyPresent("guest-oid-7"), log),
            new ScriptedMailer(configured: false, delivered: false, log), new RecordingAuditWriter(log));

        var issued = await service.IssueAsync(tenantId, "guest@gmail.example", WorkspaceRoleName.Procurement, Admin);
        Assert.True(issued.IsSuccess);
        Assert.True(issued.IdentityProvisioned); // AlreadyPresent is indistinguishable from Provisioned (§J.4a)
        var token = ExtractToken(issued.AcceptUrl!);

        // A stranger's oid -- and a mangled #EXT# UPN presented as identity -- are refused.
        var stranger = await service.AcceptAsync(token, "other-oid", "guest@gmail.example");
        Assert.Equal(MembershipOperationStatus.Forbidden, stranger.Status);
        var mangled = await service.AcceptAsync(token, "guest_gmail.example#EXT#@contoso.onmicrosoft.com");
        Assert.Equal(MembershipOperationStatus.Forbidden, mangled.Status);

        // The bound oid alone opens the door -- no email claim needed, no UPN parsed.
        var accepted = await service.AcceptAsync(token, "guest-oid-7");
        Assert.True(accepted.IsSuccess);
        Assert.Equal(WorkspaceRoleName.Procurement, accepted.Role);

        // Rule D.3d: the same identity again is the 409 idempotency signal, keyed on the oid.
        var again = await service.AcceptAsync(token, "guest-oid-7");
        Assert.Equal(MembershipOperationStatus.Conflict, again.Status);
    }

    [Fact]
    public async Task Accept_falls_back_to_the_email_claim_when_no_subject_was_bound_and_binds_the_oid_then()
    {
        var tenantId = await SeedWorkspaceAsync();
        var log = new List<string>();
        var service = CreateService(
            CreateContext(), new NullGuestProvisioner(), new ScriptedMailer(configured: false, delivered: false, log),
            new RecordingAuditWriter(log), new InvitationOptions());

        var issued = await service.IssueAsync(tenantId, "claimed@acme.example", WorkspaceRoleName.Finance, Admin);
        var token = ExtractToken(issued.AcceptUrl!);

        // No email claim, an oid nobody bound: refused (ADR-010 w15 §2.3's last step).
        var noClaim = await service.AcceptAsync(token, "oid-xyz");
        Assert.Equal(MembershipOperationStatus.Forbidden, noClaim.Status);

        // The token's own email claim matches the invited address: accepted, and the oid is bound now.
        var accepted = await service.AcceptAsync(token, "oid-xyz", "Claimed@acme.example");
        Assert.True(accepted.IsSuccess);

        using var _ = _tenantContext.BeginScope(tenantId);
        await using var readDb = CreateContext();
        Assert.Equal("oid-xyz", (await readDb.WorkspaceUsers.SingleAsync(u => u.Email == "claimed@acme.example")).ExternalSubjectId);
    }

    [Fact]
    public async Task The_cap_refuses_a_new_address_but_a_replacement_for_a_live_one_still_goes_through()
    {
        var tenantId = await SeedWorkspaceAsync();
        var log = new List<string>();
        var audit = new RecordingAuditWriter(log);
        var service = CreateService(
            CreateContext(), new NullGuestProvisioner(), new ScriptedMailer(configured: false, delivered: false, log), audit,
            new InvitationOptions { AcceptUrlBase = AcceptUrlBase, LiveInvitationCap = 2 });

        Assert.True((await service.IssueAsync(tenantId, "one@acme.example", WorkspaceRoleName.Procurement, Admin)).IsSuccess);
        Assert.True((await service.IssueAsync(tenantId, "two@acme.example", WorkspaceRoleName.Procurement, Admin)).IsSuccess);

        var third = await service.IssueAsync(tenantId, "three@acme.example", WorkspaceRoleName.Procurement, Admin);
        Assert.Equal(MembershipOperationStatus.Conflict, third.Status);
        Assert.Single(audit.Entries, e => e.Action == WorkspaceInvitationService.CapReachedAuditAction);

        // §J.1c.4 bounds DISTINCT addresses: re-issuing "one" replaces its live invitation, adds none.
        var replaced = await service.IssueAsync(tenantId, "one@acme.example", WorkspaceRoleName.Legal, Admin);
        Assert.True(replaced.IsSuccess);

        using var _ = _tenantContext.BeginScope(tenantId);
        await using var readDb = CreateContext();
        Assert.Equal(2, await readDb.WorkspaceInvitations.CountAsync(i => i.AcceptedAt == null && i.RevokedAt == null));
    }

    [Fact]
    public async Task Delivery_outcome_is_the_mailers_own_bool_and_the_mailer_receives_the_absolute_link()
    {
        var tenantId = await SeedWorkspaceAsync();
        var log = new List<string>();
        var failing = new ScriptedMailer(configured: true, delivered: false, log);
        var audit = new RecordingAuditWriter(log);
        var failingService = CreateService(CreateContext(), new NullGuestProvisioner(), failing, audit);

        var failed = await failingService.IssueAsync(tenantId, "mailfail@acme.example", WorkspaceRoleName.Procurement, Admin);
        Assert.True(failed.IsSuccess);
        Assert.Equal(InvitationDeliveryOutcome.MailFailed, failed.DeliveryOutcome);
        Assert.False(failed.MailDelivered);
        Assert.Single(audit.Entries, e => e.Action == WorkspaceInvitationService.MailFailedAuditAction);
        Assert.Equal(failed.AcceptUrl, failing.LastAcceptUrl);
        Assert.StartsWith(AcceptUrlBase + "/invite/accept#", failing.LastAcceptUrl);

        var sending = new ScriptedMailer(configured: true, delivered: true, log);
        var sendingService = CreateService(CreateContext(), new NullGuestProvisioner(), sending, audit);
        var sent = await sendingService.IssueAsync(tenantId, "mailok@acme.example", WorkspaceRoleName.Procurement, Admin);
        Assert.True(sent.IsSuccess);
        Assert.Equal(InvitationDeliveryOutcome.Sent, sent.DeliveryOutcome);
        Assert.True(sent.MailDelivered);
        Assert.Single(audit.Entries, e => e.Action == WorkspaceInvitationService.MailSentAuditAction);
    }

    // ----- harness -----

    private IdentityWorkspaceDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdentityWorkspaceDbContext>();
        IdentityWorkspaceDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString(), _tenantContext);
        return new IdentityWorkspaceDbContext(optionsBuilder.Options);
    }

    private WorkspaceInvitationService CreateService(
        IdentityWorkspaceDbContext db,
        IGuestProvisioner provisioner,
        IInvitationMailer mailer,
        IAuditWriter audit,
        InvitationOptions? options = null)
    {
        var clock = FixedClock.Instance;
        var membershipService = new WorkspaceMembershipService(db, _tenantContext, clock, audit);
        return new WorkspaceInvitationService(
            db, _tenantContext, clock, audit, mailer, membershipService, provisioner,
            options ?? new InvitationOptions { AcceptUrlBase = AcceptUrlBase });
    }

    private async Task<TenantId> SeedWorkspaceAsync()
    {
        var (workspace, roles) = WorkspaceFactory.CreateWorkspaceWithDefaultRoles("Acme Procurement", FixedClock.Instance);

        await using var db = CreateContext();
        using var _ = _tenantContext.BeginScope(workspace.TenantId);
        db.Workspaces.Add(workspace);
        db.WorkspaceRoles.AddRange(roles);
        await db.SaveChangesAsync();

        return workspace.TenantId;
    }

    private static string ExtractToken(string acceptUrl) => acceptUrl[(acceptUrl.IndexOf('#') + 1)..];

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public static readonly FixedClock Instance = new(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));

        public DateTimeOffset UtcNow => now;
    }

    /// <summary>Answers a scripted result and records the call in the shared log, so the order
    /// "provision, then write" is a fact of the log rather than an inference.</summary>
    private sealed class ScriptedProvisioner(GuestProvisioningResult result, List<string> log) : IGuestProvisioner
    {
        public Task<GuestProvisioningResult> EnsureGuestAsync(string email, string workspaceName, CancellationToken cancellationToken = default)
        {
            log.Add($"provision:{email}");
            return Task.FromResult(result);
        }
    }

    private sealed class ScriptedMailer(bool configured, bool delivered, List<string> log) : IInvitationMailer
    {
        public bool IsConfigured => configured;

        public string? LastAcceptUrl { get; private set; }

        public Task<bool> TrySendAsync(
            string email, string workspaceName, WorkspaceRoleName role, string acceptUrl, DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default)
        {
            log.Add($"mail:{email}");
            LastAcceptUrl = acceptUrl;
            return Task.FromResult(delivered);
        }
    }

    private sealed class RecordingAuditWriter(List<string> log) : IAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            log.Add($"audit:{entry.Action}");
            return Task.CompletedTask;
        }
    }
}
