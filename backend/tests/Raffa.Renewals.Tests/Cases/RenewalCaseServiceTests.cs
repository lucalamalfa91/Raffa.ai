using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Raffa.Renewals.Application.CaseWork;
using Raffa.Renewals.Application.Cases;
using Raffa.Renewals.Infrastructure;
using Raffa.Renewals.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Cases;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Tenancy;
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

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly TenantId _tenant = TenantId.New();
    private readonly TenantContext _tenantContext = new();
    private readonly FakeFacts _facts = new();
    private readonly RecordingTermWriter _terms = new();
    private readonly RecordingAuditWriter _audit = new();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private RenewalsDbContext Db()
    {
        var b = new DbContextOptionsBuilder<RenewalsDbContext>();
        RenewalsDbContextOptions.Configure(b, _postgres.GetConnectionString(), _tenantContext);
        return new RenewalsDbContext(b.Options);
    }

    private async Task<RenewalCaseService> ServiceAsync(ICaseScriptServices? script = null)
    {
        var db = Db();
        await db.Database.MigrateAsync();
        return new RenewalCaseService(
            db, _tenantContext, _facts, new Members(), script ?? FixtureCaseScriptServices.Instance,
            new ContractFactsQuestionAnswerer(_facts, new FixedClock(Now)), _terms, new NoSavingResultWriter(),
            new NoOpenQuoteDecisions(), new NoDocumentsNeedingReview(), _audit, new FixedClock(Now));
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
        IContractCaseStatusReader reader = s;
        Assert.True(version > 1);
        var status = (await reader.ListAsync(_tenant)).Single(x => x.CaseId == e.CaseId);
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

    private sealed class Members : ICaseMemberDirectory
    {
        public Task<IReadOnlyList<CaseMember>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CaseMember>>(
            [
                new CaseMember(LucaId, LucaOid, CaseFixtures.Luca, "Head of Procurement", true),
                new CaseMember(MartaId, MartaOid, CaseFixtures.Marta, "Buyer", false),
            ]);
    }

    private sealed class RecordingTermWriter : IContractTermWriter
    {
        public List<RenewedContractTerms> Written { get; } = [];

        public Task WriteRenewedTermsAsync(TenantId tenantId, RenewedContractTerms terms, CancellationToken cancellationToken = default)
        {
            Written.Add(terms);
            return Task.CompletedTask;
        }
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

        public Task<IReadOnlyList<ContractFacts>> ListValidatedAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ContractFacts>>(_ids.Keys.Select(Facts).ToList());

        public Task<ContractFacts?> GetAsync(TenantId tenantId, EntityId contractId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_ids.FirstOrDefault(x => x.Value == contractId.Value) is { Value: var v, Key: var k } && v != Guid.Empty ? Facts(k) : null);
    }
}
