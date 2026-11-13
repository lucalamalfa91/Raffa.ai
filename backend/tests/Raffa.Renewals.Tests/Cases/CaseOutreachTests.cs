using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Raffa.Renewals.Application.CaseWork;
using Raffa.Renewals.Application.Cases;
using Raffa.Renewals.Application.Notifications;
using Raffa.Renewals.Application.Outreach;
using Raffa.Renewals.Infrastructure;
using Raffa.Renewals.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Cases;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Renewals;
using Raffa.SharedKernel.Savings;
using Raffa.SharedKernel.Storage;
using Raffa.SharedKernel.Tenancy;
using Testcontainers.PostgreSql;

namespace Raffa.Renewals.Tests.Cases;

/// <summary>
/// Renewal plan F2/F3/F3b against a real Postgres: connect a company mailbox, send the approved email from it
/// (case address in CC and Reply-To, contract attached), receive the supplier's reply on the case address,
/// confirm it into the case, and the notifications (in-app and external channels) along the way.
/// </summary>
public sealed class CaseOutreachTests : IAsyncLifetime
{
    private const string LucaOid = "oid-luca";
    private const string MartaOid = "oid-marta";
    private static readonly Guid LucaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MartaId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 14, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer? _postgres =
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RAFFA_TEST_POSTGRES_ADMIN")) ? new PostgreSqlBuilder("postgres:16-alpine").Build() : null;
    private string _connection = string.Empty;
    private string? _database;
    private readonly TenantId _tenant = TenantId.New();
    private readonly TenantContext _tenantContext = new();
    private readonly CaseFacts _facts = new();
    private readonly FakeMailbox _mailbox = new();
    private readonly RecordingDispatcher _dispatcher = new();
    private readonly Storage _storage = new();

    private readonly CaseMailOptions _options = new()
    {
        TokenKey = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()),
        InboundDomain = "in.raffa.test",
        ApiPublicBaseUrl = "https://api.raffa.test",
        WebPublicBaseUrl = "https://app.raffa.test",
    };

    public async Task InitializeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.StartAsync();
            _connection = _postgres.GetConnectionString();
            return;
        }

        var server = Environment.GetEnvironmentVariable("RAFFA_TEST_POSTGRES_ADMIN")!;
        _database = "renewals_outreach_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(server))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\" TEMPLATE template0", admin);
            await create.ExecuteNonQueryAsync();
        }

        _connection = new NpgsqlConnectionStringBuilder(server) { Database = _database }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
        else if (_database is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(Environment.GetEnvironmentVariable("RAFFA_TEST_POSTGRES_ADMIN"));
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private RenewalsDbContext Db()
    {
        var b = new DbContextOptionsBuilder<RenewalsDbContext>();
        RenewalsDbContextOptions.Configure(b, _connection, _tenantContext);
        return new RenewalsDbContext(b.Options);
    }

    private sealed record Services(RenewalCaseService Cases, CaseMailService Mail, MailboxService Mailboxes, NotificationService Notifications);

    private async Task<Services> ServicesAsync()
    {
        await using (var migrate = Db())
        {
            await migrate.Database.MigrateAsync();
        }

        var clock = new FixedClock(Now);
        var notifications = new NotificationService(Db(), _tenantContext, clock, _options, new NotificationOptions(), _dispatcher);
        var cases = new RenewalCaseService(
            Db(), _tenantContext, _facts, new Members(), FixtureCaseScriptServices.Instance,
            new NoAnswerer(), new NoTerms(), new NoSavings(), new NoObjectives(),
            new NoOpenQuoteDecisions(), new NoDocumentsNeedingReview(), new RecordingAuditWriter(), clock,
            caseFileStorage: _storage, notifications: notifications);
        var mailboxes = new MailboxService(Db(), _tenantContext, clock, _options, [_mailbox]);
        var mail = new CaseMailService(Db(), _tenantContext, clock, _options, mailboxes, cases, new Members(), _facts, notifications, _storage, new ContractFiles());
        return new Services(cases, mail, mailboxes, notifications);
    }

    /// <summary>A case whose first email is approved (Marta owns it, Luca approves it).</summary>
    private async Task<CaseEnvelope> ApprovedFirstEmailAsync(Services s)
    {
        var open = await s.Cases.OpenAsync(_tenant, LucaOid, _facts.Id(4), null, MartaId);
        var e = open.Envelope!;
        foreach (var (who, type, p) in new[]
                 {
                     (MartaOid, "ans", new CaseActionPayload { Q = 0, Val = "keep" }),
                     (MartaOid, "ans", new CaseActionPayload { Q = 1, Val = "12" }),
                     (MartaOid, "ans", new CaseActionPayload { Q = 2, Val = "none" }),
                     (MartaOid, "q3done", CaseActionPayload.None),
                     (MartaOid, "confirmPlan", CaseActionPayload.None),
                     (MartaOid, "ui", new CaseActionPayload { K = "to:0", V = "anna@docusign.example" }),
                     (MartaOid, "requestAppr", new CaseActionPayload { Eid = 0 }),
                     (LucaOid, "approve", new CaseActionPayload { Eid = 0 }),
                 })
        {
            var r = await s.Cases.ActAsync(_tenant, who, e.CaseId, type, p, e.Version);
            Assert.True(r.Kind == CaseResultKind.Ok, type + " → " + r.Kind + " " + r.Error);
            e = r.Envelope!;
        }

        return e;
    }

    private async Task ConnectMartaAsync(Services s)
    {
        var url = s.Mailboxes.BeginConnect(_tenant, MartaOid, "microsoft", null);
        Assert.NotNull(url);
        var state = Uri.UnescapeDataString(url!.Split("state=")[1]);
        var tampered = await s.Mailboxes.CompleteConnectAsync("microsoft", state[..^2] + "xx", "code", null);
        Assert.False(tampered.Connected);
        var outcome = await s.Mailboxes.CompleteConnectAsync("microsoft", state, "code", null);
        Assert.True(outcome.Connected);
        Assert.Equal("https://app.raffa.test/settings/notifications?mailbox=connected", outcome.RedirectUrl);
    }

    [Fact]
    public async Task The_approved_email_is_sent_from_the_company_mailbox_with_the_case_address_and_recorded_as_sent()
    {
        var s = await ServicesAsync();
        var e = await ApprovedFirstEmailAsync(s);

        // Submitting the email for approval notified the approver.
        Assert.Contains((await s.Notifications.FeedAsync(_tenant, LucaOid)).Items, n => n.Kind == "approval_requested" && n.Link == "/renewals/" + e.CaseId);

        // Luca has no mailbox connected: nothing is sent.
        var noMailbox = await s.Mail.SendEmailAsync(_tenant, LucaOid, e.CaseId, 0, e.Version);
        Assert.Equal("no_mailbox", noMailbox.Code);
        Assert.Empty(_mailbox.Sent);

        // A client cannot claim Raffa sent it.
        Assert.Equal(CaseResultKind.BadRequest, (await s.Cases.ActAsync(_tenant, MartaOid, e.CaseId, "ui", new CaseActionPayload { K = "sentVia", V = "x@y.z" }, e.Version)).Kind);

        await ConnectMartaAsync(s);
        var sent = await s.Mail.SendEmailAsync(_tenant, MartaOid, e.CaseId, 0, e.Version);
        Assert.True(sent.Kind == CaseResultKind.Ok, sent.Error);

        var mail = Assert.Single(_mailbox.Sent);
        Assert.Equal("marta@acme.example", mail.FromAddress);
        Assert.Equal(["anna@docusign.example"], mail.To);
        var caseAddress = Assert.Single(mail.Cc);
        Assert.Matches(@"^r-[a-z0-9]{12}@in\.raffa\.test$", caseAddress);
        Assert.Equal(["marta@acme.example", caseAddress], mail.ReplyTo);
        Assert.NotEmpty(mail.Attachments);
        Assert.All(mail.Attachments, a => Assert.NotEmpty(a.Bytes));

        await using (var db = Db())
        {
            using var _ = _tenantContext.BeginScope(_tenant);
            var row = await db.CaseEmails.SingleAsync();
            Assert.Equal(EmailStates.Sent, row.State);
            Assert.Equal("marta@acme.example", row.SentVia);
            var connection = await db.MailboxConnections.SingleAsync();
            Assert.DoesNotContain("refresh", connection.RefreshTokenProtected);
            Assert.NotNull(connection.LastUsedAt);
        }

        // The rotated refresh token is the one used next time.
        Assert.Equal("refresh-1", _mailbox.TokensUsed.Single());
        Assert.Equal(caseAddress, (await s.Mail.StatusAsync(_tenant, MartaOid, e.CaseId))!.CaseAddress);

        // Sent once: a second send is refused.
        Assert.Equal("not_approved", (await s.Mail.SendEmailAsync(_tenant, MartaOid, e.CaseId, 0, null)).Code);
    }

    [Fact]
    public async Task A_revoked_mailbox_asks_to_reconnect_and_sends_nothing()
    {
        var s = await ServicesAsync();
        var e = await ApprovedFirstEmailAsync(s);
        await ConnectMartaAsync(s);
        _mailbox.Fail = new MailboxSendException("revoked", reconnect: true);

        var r = await s.Mail.SendEmailAsync(_tenant, MartaOid, e.CaseId, 0, e.Version);

        Assert.Equal("mailbox_reconnect", r.Code);
        Assert.True((await s.Mailboxes.StatusAsync(_tenant, MartaOid)).Mailbox!.NeedsReconnect);
        await using var db = Db();
        using var _ = _tenantContext.BeginScope(_tenant);
        Assert.Equal(EmailStates.Approved, (await db.CaseEmails.SingleAsync()).State);
    }

    [Fact]
    public async Task A_supplier_reply_on_the_case_address_waits_for_confirmation_notifies_the_owner_and_becomes_the_reply()
    {
        var s = await ServicesAsync();
        var e = await ApprovedFirstEmailAsync(s);
        await ConnectMartaAsync(s);
        var (slack, _) = await s.Notifications.AddChannelAsync(_tenant, MartaOid, NotificationChannelKinds.Slack, "https://hooks.slack.com/services/T0/B0/xyz", null);
        Assert.NotNull(slack);
        e = (await s.Mail.SendEmailAsync(_tenant, MartaOid, e.CaseId, 0, e.Version)).Envelope!;
        var address = (await s.Mail.StatusAsync(_tenant, MartaOid, e.CaseId))!.CaseAddress!;
        var raw = Encoding.UTF8.GetBytes("From: anna@docusign.example\r\nSubject: Re\r\n\r\nWe can offer 5% off for 24 months.");

        var unmatched = await s.Mail.ReceiveAsync(Inbound("em_0", "r-zzzzzzzzzzzz@in.raffa.test", raw));
        Assert.Equal("unmatched", unmatched.Result);
        var matched = await s.Mail.ReceiveAsync(Inbound("em_1", "Raffa <" + address.ToUpperInvariant() + ">", raw));
        Assert.Equal("matched", matched.Result);
        Assert.Equal("duplicate", (await s.Mail.ReceiveAsync(Inbound("em_1", address, raw))).Result);

        // Pending: the case did not move, the owner is notified in-app and on Slack (no email content).
        var status = (await s.Mail.StatusAsync(_tenant, MartaOid, e.CaseId))!;
        var pending = Assert.Single(status.Inbound);
        Assert.Equal("pending", pending.State);
        Assert.Equal(["OrderForm_draft.pdf"], pending.Attachments);
        var feed = await s.Notifications.FeedAsync(_tenant, MartaOid);
        var note = Assert.Single(feed.Items, n => n.Kind == "supplier_message");
        Assert.Equal(1, feed.Unread - feed.Items.Count(n => n.Kind != "supplier_message" && !n.Read));
        var delivery = Assert.Single(_dispatcher.Deliveries, d => d.Message.Kind == "supplier_message");
        Assert.Equal("https://hooks.slack.com/services/T0/B0/xyz", delivery.Target);
        Assert.Equal("https://app.raffa.test/renewals/" + e.CaseId, delivery.AbsoluteLink);
        Assert.DoesNotContain("5%", delivery.Message.Body);
        Assert.Contains("DocuSign", note.Title, StringComparison.OrdinalIgnoreCase);

        var accepted = await s.Mail.AcceptInboundAsync(_tenant, MartaOid, e.CaseId, pending.Id, null, bytes =>
            new CaseEmailUpload(new CaseUpload("message.eml", "message/rfc822", bytes), "From: anna@docusign.example\n\nWe can offer 5% off for 24 months.",
                [new CaseUpload("OrderForm_draft.pdf", "application/pdf", [1, 2, 3])]));
        Assert.True(accepted.Kind == CaseResultKind.Ok, accepted.Error);
        var after = (await s.Mail.StatusAsync(_tenant, MartaOid, e.CaseId))!.Inbound.Single();
        Assert.Equal("accepted", after.State);
        Assert.Equal(CaseFixtures.Marta, after.DecidedBy);
        var files = (await s.Cases.FilesAsync(_tenant, e.CaseId))!;
        Assert.Contains(files, f => f.Kind == CaseFileKinds.Email);
        Assert.Contains(files, f => f.Kind == CaseFileKinds.Attachment && f.FileName == "OrderForm_draft.pdf");
        Assert.False(await s.Mail.DismissInboundAsync(_tenant, MartaOid, e.CaseId, pending.Id));

        await s.Notifications.MarkReadAsync(_tenant, MartaOid, null);
        Assert.Equal(0, (await s.Notifications.FeedAsync(_tenant, MartaOid)).Unread);
    }

    [Fact]
    public async Task Channels_accept_only_their_own_targets_store_them_encrypted_and_a_disabled_one_gets_nothing()
    {
        var s = await ServicesAsync();
        Assert.Equal("Paste the Slack Incoming Webhook URL (https://hooks.slack.com/…).", (await s.Notifications.AddChannelAsync(_tenant, MartaOid, "slack", "https://evil.example/hook", null)).Error);
        Assert.NotNull((await s.Notifications.AddChannelAsync(_tenant, MartaOid, "teams", "http://prod-01.westeurope.logic.azure.com/x", null)).Error);
        Assert.NotNull((await s.Notifications.AddChannelAsync(_tenant, MartaOid, "email", "not-an-address", null)).Error);
        Assert.NotNull((await s.Notifications.AddChannelAsync(_tenant, MartaOid, "webpush", "{}", null)).Error);
        var (teams, error) = await s.Notifications.AddChannelAsync(_tenant, MartaOid, "teams", "https://prod-01.westeurope.logic.azure.com/workflows/abc", null);
        Assert.Null(error);
        var (email, _) = await s.Notifications.AddChannelAsync(_tenant, MartaOid, "email", "marta@acme.example", null);
        Assert.Equal("marta@acme.example", email!.Label);
        Assert.DoesNotContain("webpush", (await s.Notifications.SettingsAsync(_tenant, MartaOid)).Available);

        await using (var db = Db())
        {
            using var _ = _tenantContext.BeginScope(_tenant);
            Assert.All(await db.NotificationChannels.ToListAsync(), c => Assert.DoesNotContain("logic.azure.com", c.TargetProtected));
        }

        Assert.True(await s.Notifications.UpdateChannelAsync(_tenant, MartaOid, teams!.Id, false));
        Assert.False(await s.Notifications.UpdateChannelAsync(_tenant, LucaOid, email.Id, false));
        await s.Notifications.TestAsync(_tenant, MartaOid);
        Assert.Equal(["email"], _dispatcher.Deliveries.Select(d => d.ChannelKind));

        await s.Notifications.RecordDeliveryAsync(_tenant, email.Id, "gone", gone: true);
        Assert.False((await s.Notifications.SettingsAsync(_tenant, MartaOid)).Channels.Single(c => c.Id == email.Id).Enabled);
        Assert.True(await s.Notifications.RemoveChannelAsync(_tenant, MartaOid, teams.Id));
    }

    [Fact]
    public void The_secret_protector_round_trips_and_refuses_tampering()
    {
        var p = SecretProtector.From(_options)!;
        var sealedValue = p.Protect("refresh-token");
        Assert.Equal("refresh-token", p.Unprotect(sealedValue));
        Assert.Null(p.Unprotect(sealedValue[..^3] + "AAA"));
        var signed = p.Sign("{\"a\":1}");
        Assert.Equal("{\"a\":1}", p.Verify(signed));
        Assert.Null(p.Verify(signed + "x"));
        Assert.Null(SecretProtector.From(new CaseMailOptions { TokenKey = "short" }));
    }

    private static InboundMail Inbound(string id, string to, byte[] raw) =>
        new("resend", id, [to, "marta@acme.example"], "Anna <anna@docusign.example>", "Re: renewal", Now, "We can offer 5% off for 24 months.", ["OrderForm_draft.pdf"], raw);

    private sealed class FakeMailbox : IMailboxProvider
    {
        public List<OutgoingMail> Sent { get; } = [];

        public List<string> TokensUsed { get; } = [];

        public MailboxSendException? Fail { get; set; }

        public string Name => "microsoft";

        public bool IsConfigured => true;

        public string AuthorizeUrl(string state, string redirectUri, string? loginHint) => "https://login.test/authorize?redirect_uri=" + Uri.EscapeDataString(redirectUri) + "&state=" + Uri.EscapeDataString(state);

        public Task<MailboxGrant> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MailboxGrant("refresh-1", "marta@acme.example", "Marta Keller"));

        public Task<string?> SendAsync(string refreshToken, OutgoingMail mail, CancellationToken cancellationToken = default)
        {
            if (Fail is not null)
            {
                throw Fail;
            }

            TokensUsed.Add(refreshToken);
            Sent.Add(mail);
            return Task.FromResult<string?>("refresh-2");
        }
    }

    private sealed class RecordingDispatcher : INotificationDispatcher
    {
        public List<NotificationDelivery> Deliveries { get; } = [];

        public void Enqueue(NotificationDelivery delivery) => Deliveries.Add(delivery);
    }

    private sealed class ContractFiles : IContractDocumentFiles
    {
        public Task<ContractDocumentFile?> LoadAsync(TenantId tenantId, Guid contractId, string fileName, CancellationToken cancellationToken = default) =>
            Task.FromResult<ContractDocumentFile?>(new ContractDocumentFile(fileName, "application/pdf", [37, 80, 68, 70]));
    }

    private sealed class Storage : IDocumentStorage
    {
        private readonly Dictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);

        public async Task<string> SaveAsync(TenantId tenantId, EntityId documentId, int versionNumber, string fileName, Stream content, CancellationToken cancellationToken = default)
        {
            var path = DocumentStoragePath.Build(tenantId, documentId, versionNumber, fileName);
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            _blobs[path] = buffer.ToArray();
            return path;
        }

        public Task<byte[]?> LoadAsync(TenantId tenantId, string storagePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(_blobs.TryGetValue(storagePath, out var b) ? b : null);

        public Task DeleteAsync(TenantId tenantId, string storagePath, CancellationToken cancellationToken = default)
        {
            _blobs.Remove(storagePath);
            return Task.CompletedTask;
        }

        public Task<string> SavePreviewAsync(TenantId tenantId, EntityId documentId, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> SavePreviewPageAsync(TenantId tenantId, EntityId documentId, int page, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Members : ICaseMemberDirectory
    {
        public Task<IReadOnlyList<CaseMember>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CaseMember>>(
            [
                new CaseMember(LucaId, LucaOid, CaseFixtures.Luca, "Head of Procurement", true, "luca@acme.example"),
                new CaseMember(MartaId, MartaOid, CaseFixtures.Marta, "Buyer", false, "marta@acme.example"),
            ]);
    }

    private sealed class CaseFacts : IContractFactsReader
    {
        private readonly Dictionary<int, Guid> _ids = Enumerable.Range(1, 4).ToDictionary(i => i, _ => Guid.NewGuid());

        public Guid Id(int cid) => _ids[cid];

        public Task<IReadOnlyList<ContractFacts>> ListValidatedAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ContractFacts>>(_ids.Keys.Select(Facts).ToList());

        public Task<ContractFacts?> GetAsync(TenantId tenantId, EntityId contractId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_ids.Where(kv => kv.Value == contractId.Value).Select(kv => Facts(kv.Key)).FirstOrDefault());

        private ContractFacts Facts(int cid)
        {
            var c = CaseFixtures.Contract(cid);
            return new ContractFacts
            {
                ContractId = _ids[cid],
                FixtureId = cid,
                Supplier = c.Supplier,
                Name = c.Name,
                Type = c.Type,
                File = c.File,
                Spend = (decimal)c.Spend,
                StartDate = c.Start,
                EndDate = c.End,
                NoticeDeadline = c.Cancel,
                Notice = c.Notice,
                Auto = c.Auto ? "Yes" : "No",
                Uplift = c.Uplift,
                Deep = new ContractDeepFacts
                {
                    Products = c.Products.Select(p => new ContractProduct(p.Name, p.Sku, p.Unit, (decimal)p.Qty, (decimal)p.Price, 0)).ToList(),
                    Push = c.Push.Select(x => new ContractClausePoint(x.Type, x.Norm, x.Ask, x.Src, null)).ToList(),
                    Raise = c.Raise.Select(x => new ContractClausePoint(x.Type, x.Norm, x.Ask, x.Src, null)).ToList(),
                    Terms = c.Terms.Select(t => new ContractTerm(t.Label, t.Value)).ToList(),
                    OblYou = c.OblYou.Select(o => new ContractObligation(o.Text, o.When, o.Priority, o.Cadence)).ToList(),
                    OblSup = c.OblSup.Select(o => new ContractObligation(o.Text, o.When, o.Priority, o.Cadence)).ToList(),
                },
            };
        }
    }

    private sealed class NoAnswerer : ICaseQuestionAnswerer
    {
        public Task<CaseQuestionAnswer> AnswerAsync(TenantId tenantId, EntityId contractId, string text, string? lang = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CaseQuestionAnswer("No basis.", null));
    }

    private sealed class NoTerms : IContractTermWriter
    {
        public Task<Guid> WriteCurrentAsync(TenantId tenantId, ContractTermVersionInput input, CancellationToken cancellationToken = default) => Task.FromResult(Guid.NewGuid());
    }

    private sealed class NoSavings : ISavingResultRecorder
    {
        public Task<Guid> RecordAsync(TenantId tenantId, SavingResultRecord result, CancellationToken cancellationToken = default) => Task.FromResult(Guid.NewGuid());
    }

    private sealed class NoObjectives : ICaseObjectiveWriter
    {
        public Task UpsertAsync(TenantId tenantId, CaseObjectiveInfo objective, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
