using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Raffa.Renewals.Application.Cases;
using Raffa.Renewals.Domain.Cases;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Cases;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Renewals;
using Raffa.SharedKernel.Text;

namespace Raffa.Renewals.Application.CaseWork;

/// <summary>One Oggi item (BACKEND-SPEC §2.12).</summary>
/// <param name="Kind">An engine todo kind (approve, plan, send …), or <c>unassigned</c>, <c>quote</c>,
/// <c>review</c>, <c>confirmTerm</c> (renewal term to confirm) or <c>renewedInfo</c> (informational).</param>
/// <param name="Who">Display name of who acts.</param>
/// <param name="Assessment">The contract's situation assessment when its term is not simply active
/// (rolled over by itself, ending, renewal term to confirm): dates computed, wording by the model or the rules.</param>
public sealed record TodayItem(
    string Key,
    string Kind,
    Guid? CaseId,
    Guid? ContractId,
    Guid? QuoteId,
    Guid? DocumentId,
    string Supplier,
    string Contract,
    CaseText Title,
    CaseText Why,
    string Due,
    int DueDays,
    CaseText? DueLabel,
    int Stage,
    CaseText StageLabel,
    string Who,
    int Stalled,
    ContractAssessmentView? Assessment = null);

public sealed record TodayViewer(Guid? UserId, string Name, string Title, bool IsApprover, string Date);

/// <summary>The member a CPO hands a new renewal to ("Avvia e assegna a Marta").</summary>
public sealed record CaseAssignee(Guid UserId, string Name);

public sealed record TodayNextDeadline(string Supplier, string Date, int Days);

/// <param name="EmptyWorkspace">No validated contract yet ("Punto zero").</param>
/// <param name="Mine">The caller's own actions, nearest due first.</param>
/// <param name="Waiting">Items waiting on colleagues or suppliers.</param>
/// <param name="Info">Informational items, not an action (a contract that renewed by itself recently).</param>
public sealed record TodayFeed(
    TodayViewer Viewer,
    bool EmptyWorkspace,
    IReadOnlyList<TodayItem> Mine,
    IReadOnlyList<TodayItem> Waiting,
    IReadOnlyList<TodayItem> Snoozed,
    TodayNextDeadline? NextDeadline,
    CaseAssignee? Assignee,
    IReadOnlyList<DocumentNeedingReview> Documents,
    IReadOnlyList<TodayItem>? Info = null);

/// <param name="Notice">The effective notice deadline (the next one when the term rolled over by itself).</param>
/// <param name="TermStatus"><see cref="ContractTermStatus"/>: active | rolledOver | ending | expired | needsReview | unknown.</param>
/// <param name="RenewedOn">When the term rolled over by itself: the day the current term began.</param>
public sealed record RenewalsRow(
    Guid ContractId,
    Guid? CaseId,
    string Supplier,
    string Contract,
    int Stage,
    CaseText StageLabel,
    CaseText? Next,
    string Notice,
    int Days,
    string StartBy,
    string? Owner,
    bool MyTurn,
    bool HasCase,
    bool Done,
    bool Waiting,
    string TermStatus = ContractTermStatus.Active,
    string? RenewedOn = null,
    string? End = null,
    bool RenewedOnPassed = false);

public sealed record RenewalsGroup(string Key, CaseText Label, IReadOnlyList<RenewalsRow> Items);

public sealed record RenewalsBoard(
    TodayViewer Viewer,
    IReadOnlyList<RenewalsGroup> Groups,
    int OpenCases,
    int ToStart,
    bool Empty,
    CaseAssignee? Assignee,
    IReadOnlyList<RenewalsRow> Cases);

public sealed record ReminderView(Guid Id, Guid? CaseId, string Kind, string DueDate, string State);

/// <summary>Today feed, Renewals board, snoozes, reminders and the cross-module read models.</summary>
public sealed partial class RenewalCaseService : IContractCaseStatusReader, ICaseEmailDraftSink, ICaseGoalsReader
{
    /// <summary>A contract that renewed by itself shows once, as information, this many days after.</summary>
    public const int RenewedInfoDays = 30;

    /// <summary>Dismissing the informational item hides it past its window.</summary>
    public const int InfoSnoozeDays = RenewedInfoDays + 1;

    private sealed record Portfolio(
        IReadOnlyList<CaseMember> Members,
        CaseCaller Caller,
        string? Approver,
        List<(ContractFacts Facts, CaseContractFacts Cf, RenewalCaseRecord? Record, RenewalCase? Case)> Contracts)
    {
        public Dictionary<Guid, EffectiveContractTerm> Terms { get; init; } = [];

        /// <summary>The company's own lead time (days before a notice deadline), from its rules.</summary>
        public int StartWindowDays { get; init; } = CompanyRulesDefaults.StartWindowDays;
    }

    private async Task<Portfolio> LoadAsync(TenantId tenantId, string? callerIdentity, CancellationToken ct)
    {
        var members = await memberDirectory.ListAsync(tenantId, ct).ConfigureAwait(false);
        var caller = CaseCaller.From(members, callerIdentity);
        var facts = await factsReader.ListValidatedAsync(tenantId, ct).ConfigureAwait(false);
        List<RenewalCaseRecord> records;
        int window;
        using (tenantContext.BeginScope(tenantId))
        {
            records = await db.RenewalCases.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
            window = await db.CompanyRules.AsNoTracking().Select(r => (int?)r.StartWindowDays).FirstOrDefaultAsync(ct).ConfigureAwait(false)
                ?? CompanyRulesDefaults.StartWindowDays;
        }

        var list = facts.Select(f =>
        {
            var rec = records.Where(r => r.ContractId.Value == f.ContractId)
                .OrderBy(r => r.ClosedAt is null ? 0 : 1).ThenByDescending(r => r.OpenedAt).FirstOrDefault();
            return (f, CaseFactsMapper.ToCaseFacts(f, Today), rec, rec is null ? null : RenewalCaseJson.Deserialize(rec.State));
        }).ToList();
        var today = Today;
        return new Portfolio(members, caller, Approver(members), list)
        {
            Terms = facts.GroupBy(f => f.ContractId).ToDictionary(g => g.Key, g => ContractTermCalculator.Compute(g.First(), today)),
            StartWindowDays = window,
        };
    }

    private TodayViewer Viewer(Portfolio p) =>
        new(p.Caller.UserId, p.Caller.Name, p.Caller.Title, p.Caller.Name == p.Approver, CaseFormat.Date(Today));

    /// <summary>A real colleague to hand a renewal to: a Procurement member (a "Buyer") other than the
    /// approver. Null when the workspace has none — the screens then offer only "Start the renewal".</summary>
    private static CaseAssignee? Assignee(Portfolio p) =>
        p.Members.Where(m => m.Name != p.Approver && !m.IsAdmin && m.Title == "Buyer" && !string.IsNullOrWhiteSpace(m.Name))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .Select(m => new CaseAssignee(m.UserId, m.Name)).FirstOrDefault();

    private async Task<ContractAssessmentView> AssessAsync(TenantId tenantId, ContractFacts f, CancellationToken ct)
    {
        var assessor = situationAssessor ?? new RulesContractSituationAssessor();
        return ContractAssessmentView.Of(await assessor.AssessAsync(tenantId, f, Today, ct).ConfigureAwait(false));
    }

    /// <summary>Every item of the tenant (RC.todo of each open case + unassigned deadlines + quotes + reviews),
    /// and the informational ones apart.</summary>
    private async Task<(List<TodayItem> Items, List<TodayItem> Info)> AllItemsAsync(TenantId tenantId, Portfolio p, CancellationToken ct)
    {
        var items = new List<TodayItem>();
        var info = new List<TodayItem>();
        foreach (var (f, cf, rec, c) in p.Contracts)
        {
            var shortName = CaseFactsMapper.ShortName(cf.Name);
            if (rec is not null && c is not null)
            {
                if (c.Outcome is not null)
                {
                    continue;
                }

                foreach (var x in RenewalCaseEngine.Todo(c, Ctx(cf, p.Caller.Name)))
                {
                    // Too early to matter: the company's own lead time has not started. The case stays
                    // on Rinnovi; Oggi brings it back when its deadline comes within the window.
                    if (x.DueDays > p.StartWindowDays)
                    {
                        continue;
                    }

                    items.Add(new TodayItem(
                        f.ContractId + ":" + x.Kind, x.Kind, rec.Id.Value, f.ContractId, null, null, cf.Supplier, shortName,
                        x.Title, x.Why, x.Due, x.DueDays, null, x.Stage, x.StageLabel, x.Who, x.Stalled));
                }

                continue;
            }

            if (p.Approver is null)
            {
                continue;
            }

            var term = p.Terms[f.ContractId];
            switch (term.Status)
            {
                case ContractTermStatus.Active:
                {
                    var days = cf.Cancel.DayNumber - Today.DayNumber;
                    if (days <= p.StartWindowDays)
                    {
                        var date = CaseFormat.Date(cf.Cancel);
                        items.Add(Unassigned(f, cf, shortName, p.Approver, CaseText.Message("Notice deadline without an owner"), ActiveWhy(cf, term, date), date, days, null, null));
                    }

                    break;
                }

                case ContractTermStatus.RolledOver:
                {
                    var days = term.NoticeDays ?? int.MaxValue;
                    var assessment = days <= p.StartWindowDays || term.DaysSinceRenewal is <= RenewedInfoDays
                        ? await AssessAsync(tenantId, f, ct).ConfigureAwait(false)
                        : null;
                    if (days <= p.StartWindowDays)
                    {
                        var date = CaseFormat.Date(term.NoticeDeadline!.Value);
                        items.Add(Unassigned(f, cf, shortName, p.Approver, CaseText.Message("Next notice deadline without an owner"),
                            CaseText.Of(assessment!.Why.En), date, days, CaseText.Message("Next notice deadline"), assessment));
                    }
                    else if (assessment is not null)
                    {
                        var renewed = CaseFormat.Date(term.RenewedOn!.Value);
                        info.Add(new TodayItem(
                            "info:" + f.ContractId + ":renewed:" + term.RenewedOn.Value.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture),
                            "renewedInfo", null, f.ContractId, null, null, cf.Supplier, shortName,
                            CaseText.Message(term.RenewedOn <= Today ? "Renewed by itself on {date}" : "Renews by itself on {date}", ("date", renewed)),
                            CaseText.Of(assessment.Why.En),
                            CaseFormat.Date(term.NoticeDeadline!.Value), days, CaseText.Message("Next notice by"), -1,
                            CaseText.Message("Renewed by itself"), string.Empty, 0, assessment));
                    }

                    break;
                }

                case ContractTermStatus.Ending:
                {
                    var days = term.EndDays ?? int.MaxValue;
                    if (days <= p.StartWindowDays)
                    {
                        var assessment = await AssessAsync(tenantId, f, ct).ConfigureAwait(false);
                        items.Add(Unassigned(f, cf, shortName, p.Approver,
                            CaseText.Message("Ends on {date} without renewal", ("date", CaseFormat.Date(term.End!.Value))),
                            CaseText.Of(assessment.Why.En), CaseFormat.Date(term.End!.Value), days, CaseText.Message("Contract ends"), assessment));
                    }

                    break;
                }

                case ContractTermStatus.NeedsReview:
                {
                    var assessment = await AssessAsync(tenantId, f, ct).ConfigureAwait(false);
                    var passed = term.OriginalNoticeDeadline is { } od ? CaseFormat.Date(od) : string.Empty;
                    items.Add(new TodayItem(
                        f.ContractId + ":confirmTerm", "confirmTerm", null, f.ContractId, null, null, cf.Supplier, shortName,
                        CaseText.Message("Confirm the renewal term"), CaseText.Of(assessment.Why.En),
                        passed, 0, CaseText.Message("Notice deadline passed on"), -1, CaseText.Message("To verify"), p.Approver, 0, assessment));
                    break;
                }

                // Expired: Contratti shows it as "Scaduto"; nothing to do today. Unknown dates: the
                // document review asks for them.
                default:
                    break;
            }
        }

        foreach (var q in await quoteDecisions.ListAsync(tenantId, ct).ConfigureAwait(false))
        {
            var contract = p.Contracts.FirstOrDefault(x => x.Facts.ContractId == q.ContractId);
            items.Add(new TodayItem(
                "q:" + q.QuoteId, "quote", contract.Record?.Id.Value, q.ContractId, q.QuoteId, null, q.Supplier,
                q.Contract ?? (contract.Cf is null ? CaseText.Message("New need").Render() : CaseFactsMapper.ShortName(contract.Cf.Name)),
                CaseText.Message("Decide on the proposal {supplier}", ("supplier", q.Supplier)), q.Why,
                CaseFormat.Date(q.ValidUntil), q.ValidUntil.DayNumber - Today.DayNumber, CaseText.Message("Proposal valid until"),
                -1, CaseText.Message("Quotes"), q.OwnerName, 0));
        }

        foreach (var d in await documentsNeedingReview.ListAsync(tenantId, ct).ConfigureAwait(false))
        {
            var due = d.Due ?? Today;
            items.Add(new TodayItem(
                "d:" + d.DocumentId, "review", null, null, null, d.DocumentId, d.File, string.Empty,
                CaseText.Message("Verify the doubtful fields"),
                CaseText.Message("{n} fields to check before use", ("n", d.FieldsToCheck)),
                CaseFormat.Date(due), due.DayNumber - Today.DayNumber, null, -1, CaseText.Message("Documents"),
                d.OwnerName ?? p.Approver ?? p.Caller.Name, 0));
        }

        return (items, info);
    }

    private static TodayItem Unassigned(
        ContractFacts f, CaseContractFacts cf, string shortName, string approver, CaseText title, CaseText why, string date, int days, CaseText? dueLabel, ContractAssessmentView? assessment) =>
        new(f.ContractId + ":unassigned", "unassigned", null, f.ContractId, null, null, cf.Supplier, shortName,
            title, why, date, days, dueLabel, -1, CaseText.Message("Not started"), approver, 0, assessment);

    /// <summary>The prototype's copy for a notice deadline still ahead (unchanged), with the renewal
    /// term as the contract states it instead of an assumed 12 months.</summary>
    private static CaseText ActiveWhy(CaseContractFacts cf, EffectiveContractTerm term, string date)
    {
        if (!cf.Auto)
        {
            return CaseText.Message("Expires if nothing happens by {date}. Start the renewal and assign it.", ("date", date));
        }

        return term.RenewalTermMonths switch
        {
            12 => CaseText.Message("Renews by itself for 12 months if nothing happens by {date}. Start the renewal and assign it.", ("date", date)),
            { } n => CaseText.Message("Renews by itself for {n} months if nothing happens by {date}. Start the renewal and assign it.", ("n", n), ("date", date)),
            null => CaseText.Message("Renews by itself if nothing happens by {date}. Start the renewal and assign it.", ("date", date)),
        };
    }

    /// <summary><c>GET /api/today</c>: the caller's items by due date, then what waits on others; snoozed apart.</summary>
    public async Task<TodayFeed> TodayAsync(TenantId tenantId, string? callerIdentity, CancellationToken ct = default)
    {
        var p = await LoadAsync(tenantId, callerIdentity, ct).ConfigureAwait(false);
        var (items, info) = await AllItemsAsync(tenantId, p, ct).ConfigureAwait(false);
        HashSet<string> snoozed = [];
        if (p.Caller.UserId is { } uid)
        {
            using var scope = tenantContext.BeginScope(tenantId);
            var today = Today;
            snoozed = (await db.TodaySnoozes.AsNoTracking().Where(x => x.UserId == uid && x.Until > today).Select(x => x.ItemKey).ToListAsync(ct).ConfigureAwait(false))
                .ToHashSet(StringComparer.Ordinal);
        }

        var visible = items.Where(x => !snoozed.Contains(x.Key)).ToList();
        var mine = visible.Where(x => x.Who == p.Caller.Name).OrderBy(x => x.DueDays).ToList();
        var waiting = visible.Where(x => x.Who != p.Caller.Name).OrderBy(x => x.DueDays).ToList();
        // The next notice deadline still ahead, on the effective (possibly rolled-over) term.
        var next = p.Contracts
            .Where(x => x.Case?.Outcome is null && p.Terms[x.Facts.ContractId].NoticeOpen)
            .OrderBy(x => x.Cf.Cancel)
            .Select(x => new TodayNextDeadline(x.Cf.Supplier, CaseFormat.Date(x.Cf.Cancel), x.Cf.Cancel.DayNumber - Today.DayNumber))
            .FirstOrDefault();
        var docs = await documentsNeedingReview.ListAsync(tenantId, ct).ConfigureAwait(false);
        return new TodayFeed(
            Viewer(p),
            p.Contracts.Count == 0,
            mine,
            waiting,
            items.Where(x => snoozed.Contains(x.Key)).ToList(),
            next,
            Assignee(p),
            docs,
            p.Approver == p.Caller.Name ? info.Where(x => !snoozed.Contains(x.Key)).OrderBy(x => x.DueDays).ToList() : []);
    }

    /// <summary><c>GET /api/renewals?view=v4</c>: rows by notice deadline in five groups (Your turn … Closed),
    /// plus the contracts that ended without renewal.</summary>
    public async Task<RenewalsBoard> BoardAsync(TenantId tenantId, string? callerIdentity, CancellationToken ct = default)
    {
        var p = await LoadAsync(tenantId, callerIdentity, ct).ConfigureAwait(false);
        var (items, _) = await AllItemsAsync(tenantId, p, ct).ConfigureAwait(false);
        var rows = p.Contracts.Select(x =>
        {
            var (f, cf, rec, c) = x;
            var term = p.Terms[f.ContractId];
            var it = items.FirstOrDefault(i => i.ContractId == f.ContractId && i.Kind != "quote");
            var myTurn = it is not null && it.Who == p.Caller.Name;
            var stage = c is null ? -1 : RenewalCaseEngine.Stage(c);
            var next = c is null ? null : RenewalCaseEngine.View(c, Ctx(cf, p.Caller.Name)).Hdr.Next;
            return new RenewalsRow(
                f.ContractId, rec?.Id.Value, cf.Supplier, CaseFactsMapper.ShortName(cf.Name), stage,
                RenewalCaseEngine.StageLabel(stage), next, CaseFormat.Date(cf.Cancel), cf.Cancel.DayNumber - Today.DayNumber,
                CaseFormat.Date(cf.Cancel.AddDays(-60)), c?.Owner, myTurn, c is not null, c?.Outcome is not null,
                c is not null && c.Phase is CasePhases.Waiting or CasePhases.DocWait,
                term.Status, term.RenewedOn is { } r ? CaseFormat.Date(r) : null, term.End is { } e ? CaseFormat.Date(e) : null,
                term.RenewedOn is { } rp && rp <= Today);
        }).OrderBy(r => r.Days).ToList();

        static bool Expired(RenewalsRow r) => !r.HasCase && r.TermStatus == ContractTermStatus.Expired;
        var groups = new (string Key, string Label, Func<RenewalsRow, bool> Fn)[]
        {
            ("mine", "Your turn", r => !r.Done && r.MyTurn),
            ("team", "In progress with the team", r => !r.Done && !r.MyTurn && r.HasCase && !r.Waiting),
            ("supplier", "Waiting for the supplier", r => !r.Done && r.Waiting && !r.MyTurn),
            ("start", "To start", r => !r.Done && !r.HasCase && !r.MyTurn && !Expired(r)),
            ("closed", "Closed", r => r.Done),
            ("expired", "Ended without renewal", Expired),
        };
        return new RenewalsBoard(
            Viewer(p),
            groups.Select(g => new RenewalsGroup(g.Key, CaseText.Message(g.Label), rows.Where(g.Fn).ToList())).Where(g => g.Items.Count > 0).ToList(),
            rows.Count(r => r.HasCase && !r.Done),
            rows.Count(r => !r.HasCase && !Expired(r)),
            rows.Count == 0,
            Assignee(p),
            rows.Where(r => r.HasCase && !r.Done).ToList());
    }

    /// <summary>An informational item, once dismissed, stays hidden past its window; anything else 3 days.</summary>
    private static int SnoozeLength(string key) => key.StartsWith("info:", StringComparison.Ordinal) ? InfoSnoozeDays : SnoozeDays;

    /// <summary><c>POST/DELETE /api/today/{key}/snooze</c> (3 days; an informational item: for good).</summary>
    public async Task<bool> SnoozeAsync(TenantId tenantId, string? callerIdentity, string key, bool on, CancellationToken ct = default)
    {
        var members = await memberDirectory.ListAsync(tenantId, ct).ConfigureAwait(false);
        var caller = CaseCaller.From(members, callerIdentity);
        if (caller.UserId is not { } uid || string.IsNullOrWhiteSpace(key) || key.Length > 200)
        {
            return false;
        }

        using var scope = tenantContext.BeginScope(tenantId);
        var row = await db.TodaySnoozes.FirstOrDefaultAsync(x => x.UserId == uid && x.ItemKey == key, ct).ConfigureAwait(false);
        if (on)
        {
            if (row is null)
            {
                db.TodaySnoozes.Add(new TodaySnoozeRecord { TenantId = tenantId, UserId = uid, ItemKey = key, Until = Today.AddDays(SnoozeLength(key)), CreatedAt = clock.UtcNow });
            }
            else
            {
                row.Until = Today.AddDays(SnoozeLength(key));
            }
        }
        else if (row is not null)
        {
            db.TodaySnoozes.Remove(row);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary><c>GET /api/reminders</c>: the caller's open "check date" reminders (never the notice deadline).</summary>
    public async Task<IReadOnlyList<ReminderView>> RemindersAsync(TenantId tenantId, string? callerIdentity, CancellationToken ct = default)
    {
        var members = await memberDirectory.ListAsync(tenantId, ct).ConfigureAwait(false);
        var caller = CaseCaller.From(members, callerIdentity);
        using var scope = tenantContext.BeginScope(tenantId);
        var rows = await db.UserReminders.AsNoTracking()
            .Where(x => (x.UserId == caller.UserId || x.UserName == caller.Name) && x.State != "done" && x.State != "dismissed" && x.State != "cancelled")
            .OrderBy(x => x.DueDate).ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(ToView).ToList();
    }

    /// <summary><c>POST /api/reminders</c>: a custom reminder of the caller.</summary>
    public async Task<ReminderView?> AddReminderAsync(TenantId tenantId, string? callerIdentity, Guid? caseId, DateOnly due, CancellationToken ct = default)
    {
        var members = await memberDirectory.ListAsync(tenantId, ct).ConfigureAwait(false);
        var caller = CaseCaller.From(members, callerIdentity);
        if (caller.UserId is null)
        {
            return null;
        }

        using var scope = tenantContext.BeginScope(tenantId);
        var row = new UserReminderRecord { TenantId = tenantId, CaseId = caseId, UserId = caller.UserId, UserName = caller.Name, Kind = "custom", DueDate = due, State = "set", CreatedAt = clock.UtcNow };
        db.UserReminders.Add(row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToView(row);
    }

    /// <summary><c>PATCH /api/reminders/{id}</c>: done | dismissed | set.</summary>
    public async Task<ReminderView?> SetReminderStateAsync(TenantId tenantId, string? callerIdentity, Guid id, string state, CancellationToken ct = default)
    {
        if (state is not ("done" or "dismissed" or "set"))
        {
            return null;
        }

        var members = await memberDirectory.ListAsync(tenantId, ct).ConfigureAwait(false);
        var caller = CaseCaller.From(members, callerIdentity);
        using var scope = tenantContext.BeginScope(tenantId);
        var row = await db.UserReminders.FirstOrDefaultAsync(x => x.Id == new EntityId(id) && (x.UserId == caller.UserId || x.UserName == caller.Name), ct).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        row.State = state;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToView(row);
    }

    private static ReminderView ToView(UserReminderRecord r) => new(r.Id.Value, r.CaseId, r.Kind, CaseFormat.Date(r.DueDate), r.State);

    /* ---------------- cross-module read models ---------------- */

    /// <summary>Contratti/Contratto (slice 4): per contract, the latest case (open first), its outcome and next action.</summary>
    async Task<IReadOnlyDictionary<Guid, ContractCaseStatus>> IContractCaseStatusReader.GetAsync(TenantId tenantId, IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contractIds);
        List<RenewalCaseRecord> records;
        using (tenantContext.BeginScope(tenantId))
        {
            var ids = contractIds.Distinct().Select(x => new EntityId(x)).ToList();
            records = await db.RenewalCases.AsNoTracking().Where(r => ids.Contains(r.ContractId)).ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        var result = new Dictionary<Guid, ContractCaseStatus>();
        foreach (var id in contractIds.Distinct())
        {
            var r = records.Where(x => x.ContractId.Value == id).OrderBy(x => x.ClosedAt is null ? 0 : 1).ThenByDescending(x => x.OpenedAt).FirstOrDefault();
            if (r is null)
            {
                result[id] = ContractCaseStatus.None(id);
                continue;
            }

            var c = RenewalCaseJson.Deserialize(r.State);
            ContractMessage? next = null;
            if (r.ClosedAt is null)
            {
                var facts = await factsReader.GetAsync(tenantId, r.ContractId, cancellationToken).ConfigureAwait(false);
                if (facts is not null)
                {
                    var hdrNext = RenewalCaseEngine.View(c, Ctx(CaseFactsMapper.ToCaseFacts(facts, Today), c.Owner)).Hdr.Next;
                    next = ToMessage(hdrNext);
                }
            }

            result[id] = new ContractCaseStatus(id, r.ClosedAt is null, r.Id.Value, r.OutcomeKind, next);
        }

        return result;
    }

    private static ContractMessage ToMessage(CaseText t) =>
        t.IsMessage
            ? new ContractMessage(t.Id!, t.Vars?.ToDictionary(kv => kv.Key, kv => (object?)(kv.Value.IsMessage ? ToMessage(kv.Value) : kv.Value.Plain), StringComparer.Ordinal))
            : new ContractMessage(t.Plain ?? string.Empty);

    /// <summary>Offerta (slice 6): a counter-proposal draft enters the open case's email lifecycle — draft, no recipient, never sent.</summary>
    async Task<CaseEmailDraftResult> ICaseEmailDraftSink.CreateDraftAsync(TenantId tenantId, CaseEmailDraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var scope = tenantContext.BeginScope(tenantId);
        RenewalCaseRecord? record = null;
        if (request.CaseId is { } cid)
        {
            record = await db.RenewalCases.FirstOrDefaultAsync(x => x.Id == cid && x.ClosedAt == null, cancellationToken).ConfigureAwait(false);
        }

        if (record is null && request.ContractId is { } kid)
        {
            record = await OpenRecordAsync(tenantId, kid.Value, cancellationToken).ConfigureAwait(false);
        }

        if (record is null)
        {
            return CaseEmailDraftResult.NotAvailable;
        }

        var members = await memberDirectory.ListAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var actor = members.FirstOrDefault(m => m.ExternalSubjectId == request.Actor || m.Name == request.Actor)?.Name ?? request.Actor;
        var cf = await FactsAsync(tenantId, record, cancellationToken).ConfigureAwait(false);
        if (cf is null)
        {
            return CaseEmailDraftResult.NotAvailable;
        }

        var c = RenewalCaseEngine.AddExternalDraft(RenewalCaseJson.Deserialize(record.State), Ctx(cf, actor), "counter", request.Subject, request.Body);
        await AppendEventAsync(tenantId, record, actor, "email_drafted", CaseActionPayload.None, c.Emails.Count - 1, cancellationToken).ConfigureAwait(false);
        await SaveStateAsync(record, c, cf, members, cancellationToken).ConfigureAwait(false);
        var emailRow = await db.CaseEmails.AsNoTracking().Where(x => x.CaseId == record.Id && x.EmailIndex == c.Emails.Count - 1)
            .Select(x => (Guid?)x.Id.Value).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return new CaseEmailDraftResult(true, record.Id, emailRow is { } eid ? new EntityId(eid) : null);
    }

    /// <summary>Offerta (slice 6): the case target, planned term and liability policy for the quote assessment.</summary>
    async Task<Raffa.SharedKernel.Renewals.CaseGoals?> ICaseGoalsReader.GetAsync(TenantId tenantId, EntityId? caseId, EntityId? contractId, CancellationToken cancellationToken)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        RenewalCaseRecord? record = null;
        if (caseId is { } cid)
        {
            record = await db.RenewalCases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cid, cancellationToken).ConfigureAwait(false);
        }

        if (record is null && contractId is { } kid)
        {
            record = await db.RenewalCases.AsNoTracking().Where(x => x.ContractId == kid)
                .OrderBy(x => x.ClosedAt == null ? 0 : 1).ThenByDescending(x => x.OpenedAt).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }

        if (record is null)
        {
            return null;
        }

        var c = RenewalCaseJson.Deserialize(record.State);
        int? term = int.TryParse(c.Goals.Term, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : null;
        return new Raffa.SharedKernel.Renewals.CaseGoals(record.Id, c.Goals.Target, term, c.Goals.Policy ? 24 : null);
    }

    /// <summary>Snapshot used by tests and the fixture seeder.</summary>
    internal static string StateJson(RenewalCase c) => JsonSerializer.Serialize(c, RenewalCaseJson.Options);
}
