using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Raffa.Renewals.Application.CaseWork;
using Raffa.Renewals.Application.Cases;
using Raffa.SharedKernel.Text;
using Raffa.Renewals.Infrastructure;
using Raffa.Renewals.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Cases;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Renewals;
using Raffa.SharedKernel.Savings;
using Raffa.SharedKernel.Tenancy;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Renewals.Tests.Cases;

/// <summary>
/// BACKEND-SPEC §7 acceptance tests 1–8 for the AskRaffa 4.0 renewal case service, against a real
/// Postgres (the migrations, the unique open-case index, the concurrency token, RLS scopes).
/// </summary>
public sealed class RenewalCaseServiceTests : IAsyncLifetime
{
    private const string LucaOid = "oid-luca";
    private const string MartaOid = "oid-marta";
    private static readonly Guid LucaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MartaId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // RAFFA_TEST_POSTGRES_ADMIN: an existing server (a database is created per run) instead of a container, where Docker is not available.
    private readonly PostgreSqlContainer? _postgres =
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RAFFA_TEST_POSTGRES_ADMIN")) ? new PostgreSqlBuilder("postgres:16-alpine").Build() : null;
    private string _connection = string.Empty;
    private string? _database;
    private readonly TenantId _tenant = TenantId.New();
    private readonly TenantContext _tenantContext = new();
    private readonly FakeFacts _facts = new();
    private readonly RecordingTermWriter _terms = new();
    private readonly RecordingAuditWriter _audit = new();

    public async Task InitializeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.StartAsync();
            _connection = _postgres.GetConnectionString();
            return;
        }

        var server = Environment.GetEnvironmentVariable("RAFFA_TEST_POSTGRES_ADMIN")!;
        _database = "renewals_test_" + Guid.NewGuid().ToString("N");
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

    private async Task<RenewalCaseService> ServiceAsync(ICaseScriptServices? script = null, DateTimeOffset? now = null, ICaseAiReader? aiReader = null, Raffa.SharedKernel.Market.IMarketObservationSink? marketSink = null)
    {
        var db = Db();
        await db.Database.MigrateAsync();
        return new RenewalCaseService(
            db, _tenantContext, _facts, new Members(), script ?? FixtureCaseScriptServices.Instance,
            new NoAnswerer(), _terms, new NoSavings(), new NoObjectives(),
            new NoOpenQuoteDecisions(), new NoDocumentsNeedingReview(), _audit, new FixedClock(now ?? Now),
            aiReader: aiReader, marketSink: marketSink);
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 8, 14, 0, 0, TimeSpan.Zero);

    private static string Oid(string name) => name == CaseFixtures.Luca ? LucaOid : MartaOid;

    private async Task<CaseEnvelope> OpenAsync(RenewalCaseService s, int cid, string caller = LucaOid, Guid? owner = null)
    {
        var r = await s.OpenAsync(_tenant, caller, _facts.Id(cid), null, owner ?? MartaId);
        Assert.Equal(CaseResultKind.Ok, r.Kind);
        return r.Envelope!;
    }

    /// <summary>Plan confirmed for a contract: email 0 drafted, no recipient.</summary>
    private static async Task<CaseEnvelope> ToFirstEmailAsync(RenewalCaseService s, TenantId t, CaseEnvelope e)
    {
        foreach (var (type, p) in new[]
                 {
                     ("ans", new CaseActionPayload { Q = 0, Val = "keep" }),
                     ("ans", new CaseActionPayload { Q = 1, Val = "12" }),
                     ("ans", new CaseActionPayload { Q = 2, Val = "none" }),
                     ("q3done", CaseActionPayload.None),
                     ("confirmPlan", CaseActionPayload.None),
                 })
        {
            var r = await s.ActAsync(t, MartaOid, e.CaseId, type, p, e.Version);
            Assert.Equal(CaseResultKind.Ok, r.Kind);
            e = r.Envelope!;
        }

        return e;
    }

    [Fact]
    public async Task Opening_a_case_twice_returns_the_same_case()
    {
        var s = await ServiceAsync();
        var a = await OpenAsync(s, 4);
        var b = await OpenAsync(s, 4, MartaOid);
        Assert.Equal(a.CaseId, b.CaseId);
        using var _ = _tenantContext.BeginScope(_tenant);
        await using var db = Db();
        Assert.Equal(1, await db.RenewalCases.CountAsync());
    }

    [Fact]
    public async Task An_email_cannot_leave_draft_without_a_recipient_and_production_never_generates_one()
    {
        var s = await ServiceAsync(new ProductionCaseScriptServices());
        var e = await ToFirstEmailAsync(s, _tenant, await OpenAsync(s, 4));
        Assert.DoesNotContain(e.View.Page.Current.Buttons.Concat(e.View.Page.Current.More), b => b.Act == "toEx");

        var ex = await s.ActAsync(_tenant, MartaOid, e.CaseId, "toEx", new CaseActionPayload { Eid = 0 }, e.Version);
        var sub = await s.ActAsync(_tenant, MartaOid, e.CaseId, "requestAppr", new CaseActionPayload { Eid = 0 }, ex.Envelope!.Version);
        Assert.Equal(CaseResultKind.Ok, sub.Kind);
        Assert.NotNull(sub.Envelope!.View.Hdr.Warn);
        using var _ = _tenantContext.BeginScope(_tenant);
        await using var db = Db();
        var mail = await db.CaseEmails.SingleAsync();
        Assert.Equal(EmailStates.Draft, mail.State);
        Assert.Null(mail.ToAddress);
    }

    [Fact]
    public async Task Editing_an_approved_email_makes_a_new_draft_version_and_invalidates_the_approval()
    {
        var s = await ServiceAsync();
        var e = await ToFirstEmailAsync(s, _tenant, await OpenAsync(s, 4));
        e = (await s.ActAsync(_tenant, MartaOid, e.CaseId, "ui", new CaseActionPayload { K = "to:0", V = "anna@docusign.example" }, e.Version)).Envelope!;
        e = (await s.ActAsync(_tenant, MartaOid, e.CaseId, "requestAppr", new CaseActionPayload { Eid = 0 }, e.Version)).Envelope!;
        e = (await s.ActAsync(_tenant, LucaOid, e.CaseId, "approve", new CaseActionPayload { Eid = 0 }, e.Version)).Envelope!;
        var edited = await s.ActManyAsync(_tenant, MartaOid, e.CaseId, e.Version, _ =>
        [
            ("edit", new CaseActionPayload { Eid = 0 }),
            ("ui", new CaseActionPayload { K = "editBody", V = "A different body" }),
            ("saveEdit", new CaseActionPayload { Eid = 0 }),
        ]);
        Assert.Equal(CaseResultKind.Ok, edited.Kind);

        using var _ = _tenantContext.BeginScope(_tenant);
        await using var db = Db();
        var mail = await db.CaseEmails.SingleAsync();
        Assert.Equal(EmailStates.Draft, mail.State);
        Assert.Equal(2, mail.Version);
        Assert.Null(mail.ApprovedVersion);
        Assert.Null(mail.ApprovedBy);
        Assert.True(await db.CaseEvents.AnyAsync(x => x.Type == "email_edited_invalidated"));
    }

    [Fact]
    public async Task Only_the_approver_or_an_explicit_delegate_approves_and_close_is_a_separate_right()
    {
        var s = await ServiceAsync();
        var e = await ToFirstEmailAsync(s, _tenant, await OpenAsync(s, 4));
        e = (await s.ActAsync(_tenant, MartaOid, e.CaseId, "ui", new CaseActionPayload { K = "to:0", V = "anna@docusign.example" }, e.Version)).Envelope!;
        e = (await s.ActAsync(_tenant, MartaOid, e.CaseId, "requestAppr", new CaseActionPayload { Eid = 0 }, e.Version)).Envelope!;

        var denied = await s.ActAsync(_tenant, MartaOid, e.CaseId, "approve", new CaseActionPayload { Eid = 0 }, e.Version);
        Assert.Equal(CaseResultKind.Forbidden, denied.Kind);
        Assert.False(denied.Envelope!.Capabilities.CanApprove);
        Assert.Equal(e.Version, denied.Envelope.Version);

        e = (await s.ActAsync(_tenant, LucaOid, e.CaseId, "delegate", new CaseActionPayload { Who = CaseFixtures.Marta, On = true }, e.Version)).Envelope!;
        var asMarta = await s.GetAsync(_tenant, MartaOid, e.CaseId, false);
        Assert.True(asMarta.Envelope!.Capabilities.CanApprove);
        Assert.False(asMarta.Envelope.Capabilities.CanClose);

        var ok = await s.ActAsync(_tenant, MartaOid, e.CaseId, "approve", new CaseActionPayload { Eid = 0 }, e.Version);
        Assert.Equal(CaseResultKind.Ok, ok.Kind);
        using var _ = _tenantContext.BeginScope(_tenant);
        await using var db = Db();
        Assert.Equal(EmailStates.Approved, (await db.CaseEmails.SingleAsync()).State);
        Assert.True(await db.CaseDelegations.AnyAsync(d => d.UserId == MartaId && d.CanApprove && !d.CanClose));
    }

    [Fact]
    public async Task Pasting_the_same_vendor_reply_twice_is_refused_with_the_first_message_id()
    {
        var s = await ServiceAsync();
        var e = await OpenAsync(s, 4);
        const string text = "Hi, we can offer 5% off for 24 months.";
        var first = await s.ActAsync(_tenant, MartaOid, e.CaseId, "reply", new CaseActionPayload { Text = text }, e.Version);
        Assert.Equal(CaseResultKind.Ok, first.Kind);
        var second = await s.ActAsync(_tenant, MartaOid, e.CaseId, "reply", new CaseActionPayload { Text = "Hi,  we can offer 5% off for 24 months. " }, first.Envelope!.Version);
        Assert.Equal(CaseResultKind.Duplicate, second.Kind);

        using var _ = _tenantContext.BeginScope(_tenant);
        await using var db = Db();
        var stored = await db.CaseVendorMessages.SingleAsync();
        Assert.Equal(stored.Id.Value, second.ExistingMessageId);
    }

    [Fact]
    public async Task Copy_never_marks_sent_and_sent_records_the_text_actually_sent()
    {
        var s = await ServiceAsync();
        var e = await ToFirstEmailAsync(s, _tenant, await OpenAsync(s, 4));
        e = (await s.ActAsync(_tenant, MartaOid, e.CaseId, "ui", new CaseActionPayload { K = "to:0", V = "anna@docusign.example" }, e.Version)).Envelope!;
        e = (await s.ActAsync(_tenant, MartaOid, e.CaseId, "requestAppr", new CaseActionPayload { Eid = 0 }, e.Version)).Envelope!;
        e = (await s.ActAsync(_tenant, LucaOid, e.CaseId, "approve", new CaseActionPayload { Eid = 0 }, e.Version)).Envelope!;
        var copied = await s.ActAsync(_tenant, MartaOid, e.CaseId, "copy", new CaseActionPayload { Eid = 0 }, e.Version);
        Assert.NotNull(copied.Envelope!.Clip);
        await using (var db = Db())
        {
            using var _ = _tenantContext.BeginScope(_tenant);
            Assert.Equal(EmailStates.Copied, (await db.CaseEmails.SingleAsync()).State);
        }

        var sent = await s.ActManyAsync(_tenant, MartaOid, e.CaseId, copied.Envelope.Version, _ =>
        [
            ("sentOpen", new CaseActionPayload { Eid = 0 }),
            ("ui", new CaseActionPayload { K = "sentEdited", V = true }),
            ("ui", new CaseActionPayload { K = "sentText", V = "What I really sent" }),
            ("sentOk", new CaseActionPayload { Eid = 0 }),
        ]);
        Assert.Equal(CaseResultKind.Ok, sent.Kind);
        await using var db2 = Db();
        using var __ = _tenantContext.BeginScope(_tenant);
        var mail = await db2.CaseEmails.SingleAsync();
        Assert.Equal(EmailStates.Sent, mail.State);
        Assert.Equal("What I really sent", mail.SentBody);
    }

    [Fact]
    public async Task A_stale_version_gets_a_conflict_with_the_current_case_and_no_overwrite()
    {
        var s = await ServiceAsync();
        var e = await OpenAsync(s, 4);
        var a = await s.ActAsync(_tenant, MartaOid, e.CaseId, "ans", new CaseActionPayload { Q = 0, Val = "keep" }, e.Version);
        Assert.Equal(CaseResultKind.Ok, a.Kind);
        var stale = await s.ActAsync(_tenant, MartaOid, e.CaseId, "ans", new CaseActionPayload { Q = 0, Val = "reduce" }, e.Version);
        Assert.Equal(CaseResultKind.Conflict, stale.Kind);
        Assert.Equal(a.Envelope!.Version, stale.Envelope!.Version);
    }

    [Fact]
    public async Task Closing_as_renewed_writes_the_renewed_terms_and_closes_the_case()
    {
        var s = await ServiceAsync();
        var flow = CaseFixtures.Flows()["salesforce_da_zero"]!.AsObject();
        var e = await OpenAsync(s, 1);
        var version = e.Version;
        foreach (var step in flow["steps"]!.AsArray())
        {
            var a = step!.AsArray();
            var who = a[0]!.GetValue<string>();
            var type = a[1]!.GetValue<string>();
            var p = CaseFixtures.Payload(type, a.Count > 2 ? a[2] as JsonObject : null);
            var r = await s.ActAsync(_tenant, Oid(who), e.CaseId, type, p, null);
            Assert.True(r.Kind is CaseResultKind.Ok or CaseResultKind.Forbidden, type + " → " + r.Kind);
            version = r.Envelope!.Version;
        }

        var written = Assert.Single(_terms.Written);
        Assert.Equal(_facts.Id(1), written.ContractId);
        Assert.True(written.ValidTo > new DateOnly(2027, 1, 15));
        Assert.Equal(e.CaseId, written.CaseId);
        IContractCaseStatusReader reader = s;
        Assert.True(version > 1);
        var status = (await reader.GetAsync(_tenant, [_facts.Id(1)]))[_facts.Id(1)];
        Assert.Equal(e.CaseId, status.CaseId);
        Assert.Equal("renewed", status.Outcome);
        Assert.False(status.HasOpenCase);

        // Closed: the next renewal cycle may open a new case for the same contract.
        var open = await s.OpenAsync(_tenant, LucaOid, _facts.Id(1), null, null);
        Assert.NotEqual(e.CaseId, open.Envelope!.CaseId);
    }

    [Fact]
    public async Task Today_lists_the_unassigned_deadline_for_the_approver_only_and_honours_snooze()
    {
        var s = await ServiceAsync();
        var luca = await s.TodayAsync(_tenant, LucaOid);
        Assert.Contains(luca.Mine, x => x.Kind == "unassigned" && x.ContractId == _facts.Id(4));
        var marta = await s.TodayAsync(_tenant, MartaOid);
        Assert.DoesNotContain(marta.Mine, x => x.Kind == "unassigned");

        var key = luca.Mine.First().Key;
        Assert.True(await s.SnoozeAsync(_tenant, LucaOid, key, true));
        var after = await s.TodayAsync(_tenant, LucaOid);
        Assert.DoesNotContain(after.Mine, x => x.Key == key);
        Assert.Contains(after.Snoozed, x => x.Key == key);
    }

    [Fact]
    public async Task A_contract_with_no_dates_read_does_not_break_Today_or_Renewals()
    {
        _facts.IncludeUndated = true;
        var s = await ServiceAsync();

        var board = await s.BoardAsync(_tenant, LucaOid);
        var row = board.Groups.SelectMany(g => g.Items).Single(r => r.ContractId == _facts.UndatedId);
        Assert.Equal("—", row.Notice);
        Assert.Equal("—", row.StartBy);
        Assert.Equal(int.MaxValue, row.Days);
        Assert.Equal(ContractTermStatus.Unknown, row.TermStatus);
        // The dated contracts still come first.
        Assert.Equal(_facts.UndatedId, board.Groups.SelectMany(g => g.Items).OrderBy(r => r.Days).Last().ContractId);

        var today = await s.TodayAsync(_tenant, LucaOid);
        Assert.DoesNotContain(today.Mine.Concat(today.Waiting), x => x.ContractId == _facts.UndatedId);
    }

    [Fact]
    public async Task A_contract_that_cannot_be_read_is_left_out_and_never_fails_Today_or_Renewals()
    {
        _facts.IncludeBroken = true;
        var s = await ServiceAsync();

        var board = await s.BoardAsync(_tenant, LucaOid);
        var ids = board.Groups.SelectMany(g => g.Items).Select(r => r.ContractId).ToList();
        Assert.DoesNotContain(_facts.BrokenId, ids);
        Assert.Contains(_facts.Id(4), ids);

        var today = await s.TodayAsync(_tenant, LucaOid);
        Assert.DoesNotContain(today.Mine.Concat(today.Waiting), x => x.ContractId == _facts.BrokenId);
        Assert.Contains(today.Mine, x => x.ContractId == _facts.Id(4));
    }

    [Fact]
    public async Task Deleting_a_contract_removes_its_case_and_everything_attached_to_it()
    {
        var s = await ServiceAsync();
        var e = await ToFirstEmailAsync(s, _tenant, await OpenAsync(s, 4));
        var other = await OpenAsync(s, 1);
        var luca = await s.TodayAsync(_tenant, LucaOid);
        Assert.True(await s.SnoozeAsync(_tenant, LucaOid, luca.Mine.Concat(luca.Waiting).First(x => x.ContractId == _facts.Id(4)).Key, true));

        var removed = await s.DeleteForContractsAsync(_tenant, [_facts.Id(4)]);
        Assert.Equal(1, removed);

        using var _ = _tenantContext.BeginScope(_tenant);
        await using var db = Db();
        Assert.Equal(other.CaseId, (await db.RenewalCases.SingleAsync()).Id.Value);
        Assert.Empty(await db.CaseEmails.ToListAsync());
        Assert.Empty(await db.CaseEvents.Where(x => x.CaseId == new Raffa.SharedKernel.EntityId(e.CaseId)).ToListAsync());
        Assert.Empty(await db.TodaySnoozes.Where(x => x.ItemKey.StartsWith(_facts.Id(4).ToString())).ToListAsync());
        Assert.Equal(0, await s.DeleteForContractsAsync(_tenant, []));
    }

    private CompanyRulesService RulesService(RenewalsDbContext db) =>
        new(db, _tenantContext, new ScriptedCompanyRulesAssistant(), new Members(), new FixedClock(Now));

    [Fact]
    public async Task A_case_deadline_beyond_the_companys_lead_time_stays_off_Today_but_on_Rinnovi()
    {
        var s = await ServiceAsync();
        var e = await OpenAsync(s, 4);

        // Default lead time (120 days): DocuSign's notice deadline is 24 days away, so it shows.
        Assert.Contains((await s.TodayAsync(_tenant, MartaOid)).Mine.Concat((await s.TodayAsync(_tenant, MartaOid)).Waiting), x => x.CaseId == e.CaseId);

        await using (var db = Db())
        {
            var saved = await RulesService(db).SaveAsync(_tenant, LucaOid, "Start late.", 14, null);
            Assert.Equal(14, saved!.StartWindowDays);
        }

        var luca = await s.TodayAsync(_tenant, LucaOid);
        var marta = await s.TodayAsync(_tenant, MartaOid);
        Assert.DoesNotContain(luca.Mine.Concat(luca.Waiting).Concat(marta.Mine).Concat(marta.Waiting), x => x.CaseId == e.CaseId);
        Assert.Contains((await s.BoardAsync(_tenant, LucaOid)).Cases, r => r.CaseId == e.CaseId);
    }

    [Fact]
    public async Task Company_rules_round_trip_clamp_and_are_written_by_an_admin_only()
    {
        await using var db = Db();
        await db.Database.MigrateAsync();
        var rules = RulesService(db);

        var none = await rules.GetAsync(_tenant);
        Assert.True(none.IsDefault);
        Assert.Equal(CompanyRulesDefaults.StartWindowDays, none.StartWindowDays);

        Assert.True(await rules.CanEditAsync(_tenant, LucaOid));
        Assert.False(await rules.CanEditAsync(_tenant, MartaOid));

        var saved = await rules.SaveAsync(_tenant, LucaOid, "  No auto-renewal.  ", 5, "policy.pdf");
        Assert.Equal("No auto-renewal.", saved!.Text);
        Assert.Equal(CompanyRulesDefaults.MinStartWindowDays, saved.StartWindowDays);
        Assert.Equal("policy.pdf", saved.SourceFile);

        // Saving again updates the one row and keeps the file name and window it was not given.
        var again = await rules.SaveAsync(_tenant, LucaOid, "No auto-renewal, Net 60.", null, null);
        Assert.Equal(CompanyRulesDefaults.MinStartWindowDays, again!.StartWindowDays);
        Assert.Equal("policy.pdf", again.SourceFile);
        Assert.Equal(1, await db.CompanyRules.CountAsync());
        Assert.Null(await rules.SaveAsync(_tenant, LucaOid, new string('x', CompanyRulesDefaults.MaxTextLength + 1), null, null));
    }

    [Fact]
    public async Task Today_never_lists_a_passed_notice_deadline_as_without_an_owner()
    {
        // 01 Nov 2026: DocuSign's notice (02 Oct) and Salesforce's (18 Oct) have passed; both renew
        // by themselves for 12 months, so their next notice deadlines are a year away.
        var s = await ServiceAsync(now: new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));
        var luca = await s.TodayAsync(_tenant, LucaOid);

        Assert.DoesNotContain(luca.Mine.Concat(luca.Waiting), x => x.Kind == "unassigned");
        Assert.All(luca.Mine.Concat(luca.Waiting).Concat(luca.Info!), x => Assert.True(x.DueDays >= 0, x.Key));

        // DocuSign renewed by itself yesterday: one informational item, not "your action".
        var docusign = Assert.Single(luca.Info!, x => x.ContractId == _facts.Id(4));
        Assert.Equal("renewedInfo", docusign.Kind);
        Assert.Equal("Renewed by itself on {date}", docusign.Title.Id);
        Assert.Equal("02 Oct 2027", docusign.Due);
        Assert.Equal(ContractTermStatus.RolledOver, docusign.Assessment!.Status);
        Assert.Equal("31 Oct 2027", docusign.Assessment.End);
        var row = (await s.BoardAsync(_tenant, LucaOid)).Groups.SelectMany(g => g.Items).Single(r => r.ContractId == _facts.Id(4));
        Assert.Equal("02 Oct 2027", row.Notice);
        Assert.Equal(ContractTermStatus.RolledOver, row.TermStatus);
        Assert.Equal("31 Oct 2026", row.RenewedOn);
        Assert.True(row.RenewedOnPassed);

        // The next notice deadline still ahead is Microsoft's, not a passed one.
        Assert.Equal("Microsoft", luca.NextDeadline!.Supplier);

        // Dismissed once, it does not come back.
        Assert.True(await s.SnoozeAsync(_tenant, LucaOid, docusign.Key, true));
        Assert.DoesNotContain((await s.TodayAsync(_tenant, LucaOid)).Info!, x => x.Key == docusign.Key);
    }

    [Fact]
    public async Task Without_a_procurement_colleague_there_is_no_assignee()
    {
        var db = Db();
        await db.Database.MigrateAsync();
        var s = new RenewalCaseService(
            db, _tenantContext, _facts, new OnlyAdmin(), FixtureCaseScriptServices.Instance,
            new NoAnswerer(), _terms, new NoSavings(), new NoObjectives(),
            new NoOpenQuoteDecisions(), new NoDocumentsNeedingReview(), _audit, new FixedClock(Now));
        var feed = await s.TodayAsync(_tenant, LucaOid);
        Assert.Null(feed.Assignee);
        Assert.Contains(feed.Mine, x => x.Kind == "unassigned");
    }

    private sealed class OnlyAdmin : ICaseMemberDirectory
    {
        public Task<IReadOnlyList<CaseMember>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CaseMember>>([new CaseMember(LucaId, LucaOid, CaseFixtures.Luca, "Head of Procurement", true)]);
    }

    [Fact]
    public async Task A_read_supplier_reply_feeds_the_market_benchmark_invisibly_and_a_failing_sink_never_breaks_the_case()
    {
        var sink = new RecordingMarketSink();
        var s = await ServiceAsync(new ProductionCaseScriptServices(), aiReader: new FixedReplyReader(), marketSink: sink);
        var e = await ToFirstEmailAsync(s, _tenant, await OpenAsync(s, 4));

        var reply = await s.ActAsync(_tenant, MartaOid, e.CaseId, "reply", new CaseActionPayload { Text = CaseText.Of("We can do CHF 11.11 per seat for 24 months, 10% off.") }, e.Version);

        Assert.Equal(CaseResultKind.Ok, reply.Kind);
        var o = Assert.Single(sink.Recorded);
        Assert.Equal(24, o.TermMonths);
        Assert.Equal(11.11m, o.UnitPrice);
        Assert.Equal(10, o.DiscountPct);
        Assert.Equal("CHF", o.Currency);
        Assert.False(string.IsNullOrWhiteSpace(o.Supplier));

        // A sink that throws is invisible to the user: the next reply still lands.
        var s2 = await ServiceAsync(new ProductionCaseScriptServices(), aiReader: new FixedReplyReader(), marketSink: new ThrowingMarketSink());
        var e2 = await ToFirstEmailAsync(s2, _tenant, await OpenAsync(s2, 3));
        var ok = await s2.ActAsync(_tenant, MartaOid, e2.CaseId, "reply", new CaseActionPayload { Text = CaseText.Of("Offer: CHF 9 for 24 months.") }, e2.Version);
        Assert.Equal(CaseResultKind.Ok, ok.Kind);
    }

    private sealed class FixedReplyReader : ICaseAiReader
    {
        public Task<CaseReplyAnalysis?> ReadReplyAsync(TenantId tenantId, RenewalCase c, CaseContractFacts contract, string replyText, int round, string lang, CancellationToken cancellationToken = default) =>
            Task.FromResult<CaseReplyAnalysis?>(new CaseReplyAnalysis(
                [new CaseReplyRow { Label = CaseText.Of("Price"), Said = CaseText.Of("CHF 11.11"), Plan = CaseText.Of("-10%"), State = "proposed" }],
                [],
                new Dictionary<int, double> { [24] = 11.11 },
                new CaseReplyBenchmark(10, null, null, null, 24)));

        public Task<CaseFeatureSet?> ReadFeaturesAsync(TenantId tenantId, CaseContractFacts contract, string lang, CancellationToken cancellationToken = default) => Task.FromResult<CaseFeatureSet?>(null);

        public Task<IReadOnlyList<CaseDraftRow>?> ReadDraftAsync(TenantId tenantId, RenewalCase c, CaseContractFacts contract, string fileName, string documentText, string lang, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CaseDraftRow>?>(null);

        public Task<CaseSignedCheck?> ReadSignedAsync(TenantId tenantId, RenewalCase c, CaseContractFacts contract, string fileName, string documentText, string lang, CancellationToken cancellationToken = default) => Task.FromResult<CaseSignedCheck?>(null);

        public Task<CaseAltOffer?> ReadOfferAsync(TenantId tenantId, RenewalCase c, CaseContractFacts contract, string vendor, string documentText, string by, string lang, CancellationToken cancellationToken = default) => Task.FromResult<CaseAltOffer?>(null);
    }

    private sealed class RecordingMarketSink : Raffa.SharedKernel.Market.IMarketObservationSink
    {
        public List<Raffa.SharedKernel.Market.SupplierReplyObservation> Recorded { get; } = [];

        public Task RecordAsync(TenantId tenantId, string actor, string correlationId, Raffa.SharedKernel.Market.SupplierReplyObservation observation, CancellationToken cancellationToken = default)
        {
            Recorded.Add(observation);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingMarketSink : Raffa.SharedKernel.Market.IMarketObservationSink
    {
        public Task RecordAsync(TenantId tenantId, string actor, string correlationId, Raffa.SharedKernel.Market.SupplierReplyObservation observation, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The market store is down.");
    }

    private sealed class Members : ICaseMemberDirectory
    {
        public Task<IReadOnlyList<CaseMember>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CaseMember>>(
            [
                new CaseMember(LucaId, LucaOid, CaseFixtures.Luca, "Head of Procurement", true),
                new CaseMember(MartaId, MartaOid, CaseFixtures.Marta, "Buyer", false),
            ]);

        public Task<(string? Currency, string? Country)> MarketProfileAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<(string?, string?)>(("CHF", "CH"));
    }

    private sealed class RecordingTermWriter : IContractTermWriter
    {
        public List<ContractTermVersionInput> Written { get; } = [];

        public Task<Guid> WriteCurrentAsync(TenantId tenantId, ContractTermVersionInput input, CancellationToken cancellationToken = default)
        {
            Written.Add(input);
            return Task.FromResult(Guid.NewGuid());
        }
    }

    private sealed class NoAnswerer : ICaseQuestionAnswerer
    {
        public Task<CaseQuestionAnswer> AnswerAsync(TenantId tenantId, EntityId contractId, string text, string? lang = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CaseQuestionAnswer("No basis.", null));
    }

    private sealed class NoSavings : ISavingResultRecorder
    {
        public Task<Guid> RecordAsync(TenantId tenantId, SavingResultRecord result, CancellationToken cancellationToken = default) => Task.FromResult(Guid.NewGuid());
    }

    private sealed class NoObjectives : ICaseObjectiveWriter
    {
        public Task UpsertAsync(TenantId tenantId, CaseObjectiveInfo objective, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>The four prototype contracts as <see cref="ContractFacts"/> (validated).</summary>
    private sealed class FakeFacts : IContractFactsReader
    {
        private readonly Dictionary<int, Guid> _ids = Enumerable.Range(1, 4).ToDictionary(i => i, _ => Guid.NewGuid());

        public Guid Id(int cid) => _ids[cid];

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

        /// <summary>A validated contract whose dates were not read (a technical answer filed as a SOW): no
        /// start, end or notice deadline, a 60-day notice period.</summary>
        public bool IncludeUndated { get; set; }

        /// <summary>A contract whose facts cannot be mapped at all (a damaged read): the board must leave it out, not fail.</summary>
        public bool IncludeBroken { get; set; }

        public Guid BrokenId { get; } = Guid.NewGuid();

        public Guid UndatedId { get; } = Guid.NewGuid();

        private ContractFacts Undated() => Facts(1) with
        {
            ContractId = UndatedId,
            FixtureId = null,
            Supplier = "Risposta alle specifiche tecniche v1.3.pdf",
            Name = "Risposta alle specifiche tecniche v1.3.pdf",
            Spend = 0,
            StartDate = null,
            EndDate = null,
            NoticeDeadline = null,
            Start = "",
            End = "",
            Cancel = "",
            Notice = 60,
        };

        public Task<IReadOnlyList<ContractFacts>> ListValidatedAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ContractFacts>>(_ids.Keys.Select(Facts).Concat(IncludeUndated ? [Undated()] : []).Concat(IncludeBroken ? [Facts(2) with { ContractId = BrokenId, Deep = null! }] : []).ToList());

        public Task<ContractFacts?> GetAsync(TenantId tenantId, EntityId contractId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_ids.FirstOrDefault(x => x.Value == contractId.Value) is { Value: var v, Key: var k } && v != Guid.Empty ? Facts(k) : null);
    }
}
