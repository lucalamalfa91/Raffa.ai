using Raffa.Renewals.Application;
using Raffa.Renewals.Configuration;
using Raffa.Renewals.Domain;
using Raffa.Renewals.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Renewals.Tests;

/// <summary>
/// Proves task E03/F02/US01/T01's own artifact (wave-spec: <c>threshold-scheduler</c>; parent
/// story us-01-threshold-scheduler AC-1 "Threshold windows ... configurable" and AC-2 "Emits
/// renewal.approaching events creating alerts (spec App B)") end to end: given a set of
/// <see cref="ContractRenewalTerms"/> and a fixed "today",
/// <see cref="RenewalThresholdScheduler"/> raises exactly the <see cref="RenewalApproachingEvent"/>s
/// product spec §9.1's threshold ladder predicts, and durably records each one through
/// <see cref="Raffa.SharedKernel.IAuditWriter"/> — no database, no HTTP call, no LLM call
/// anywhere in the path (Appendix C rule 6).
/// </summary>
public sealed class RenewalThresholdSchedulerTests
{
    private static readonly DateOnly AsOf = new(2026, 1, 1);
    private static readonly FixedClock Clock = new(new DateTimeOffset(AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
    private static readonly TenantId Tenant = TenantId.New();

    private readonly RecordingAuditWriter _auditWriter = new();
    private readonly RenewalThresholdScheduler _scheduler;

    public RenewalThresholdSchedulerTests()
    {
        _scheduler = new RenewalThresholdScheduler(
            Clock, new RenewalEngine(Clock), _auditWriter, new ThresholdWindowOptions(), new TenantContext());
    }

    [Fact]
    public void Default_thresholds_match_product_spec_9_1()
    {
        Assert.Equal([365, 270, 180, 120, 90, 60, 30], new ThresholdWindowOptions().DaysBeforeDeadline);
    }

    [Fact]
    public async Task Emits_a_renewal_date_event_when_days_until_renewal_matches_a_configured_threshold()
    {
        var terms = new ContractRenewalTerms(EntityId.New(), AsOf.AddDays(90), AutoRenewal: true, CancellationNoticeDays: null);

        var events = await _scheduler.EvaluateThresholdsAsync(Tenant, [terms]);

        var raised = Assert.Single(events);
        Assert.Equal(terms.ContractId, raised.ContractId);
        Assert.Equal(RenewalMilestoneKind.RenewalDate, raised.Milestone);
        Assert.Equal(AsOf.AddDays(90), raised.MilestoneDate);
        Assert.Equal(90, raised.ThresholdDays);
        Assert.Equal(90, raised.DaysRemaining);
        Assert.Equal(Tenant, raised.TenantId);
        Assert.Equal(Clock.UtcNow, raised.OccurredAt);
    }

    [Fact]
    public async Task Emits_a_cancellation_deadline_event_when_days_until_deadline_matches_a_configured_threshold()
    {
        // EndDate 121 days out, 31 days' notice => cancellation deadline exactly 90 days out, and
        // the renewal date itself (121) does not match any default threshold (unlike 120, which
        // would also match and turn this into a two-event case).
        var terms = new ContractRenewalTerms(EntityId.New(), AsOf.AddDays(121), AutoRenewal: true, CancellationNoticeDays: 31);

        var events = await _scheduler.EvaluateThresholdsAsync(Tenant, [terms]);

        var raised = Assert.Single(events);
        Assert.Equal(RenewalMilestoneKind.CancellationDeadline, raised.Milestone);
        Assert.Equal(AsOf.AddDays(90), raised.MilestoneDate);
        Assert.Equal(90, raised.ThresholdDays);
        Assert.Equal(90, raised.DaysRemaining);
    }

    [Fact]
    public async Task Emits_both_events_when_renewal_date_and_cancellation_deadline_both_match_a_threshold()
    {
        // CancellationNoticeDays = 0 => cancellation deadline == renewal date == end date, both 60
        // days out: two distinct milestones, both due today's run.
        var terms = new ContractRenewalTerms(EntityId.New(), AsOf.AddDays(60), AutoRenewal: true, CancellationNoticeDays: 0);

        var events = await _scheduler.EvaluateThresholdsAsync(Tenant, [terms]);

        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.Milestone == RenewalMilestoneKind.RenewalDate);
        Assert.Contains(events, e => e.Milestone == RenewalMilestoneKind.CancellationDeadline);
        Assert.All(events, e => Assert.Equal(60, e.ThresholdDays));
    }

    [Fact]
    public async Task Emits_nothing_when_no_configured_threshold_matches()
    {
        var terms = new ContractRenewalTerms(EntityId.New(), AsOf.AddDays(91), AutoRenewal: true, CancellationNoticeDays: null);

        var events = await _scheduler.EvaluateThresholdsAsync(Tenant, [terms]);

        Assert.Empty(events);
        Assert.Empty(_auditWriter.Written);
    }

    [Fact]
    public async Task Emits_nothing_for_a_contract_that_does_not_auto_renew()
    {
        // AutoRenewal=false => RenewalEngine returns NoRenewal, both day-counts null, even though
        // 90 days out would otherwise match a default threshold.
        var terms = new ContractRenewalTerms(EntityId.New(), AsOf.AddDays(90), AutoRenewal: false, CancellationNoticeDays: 30);

        var events = await _scheduler.EvaluateThresholdsAsync(Tenant, [terms]);

        Assert.Empty(events);
    }

    [Fact]
    public async Task Emits_nothing_when_the_end_date_cannot_be_determined()
    {
        var terms = new ContractRenewalTerms(EntityId.New(), EndDate: null, AutoRenewal: true, CancellationNoticeDays: 30);

        var events = await _scheduler.EvaluateThresholdsAsync(Tenant, [terms]);

        Assert.Empty(events);
    }

    [Fact]
    public async Task Writes_one_audit_entry_per_emitted_event()
    {
        var terms = new ContractRenewalTerms(EntityId.New(), AsOf.AddDays(60), AutoRenewal: true, CancellationNoticeDays: 0);

        var events = await _scheduler.EvaluateThresholdsAsync(Tenant, [terms]);

        Assert.Equal(events.Count, _auditWriter.Written.Count);
        Assert.All(_auditWriter.Written, entry =>
        {
            Assert.Equal(RenewalApproachingEvent.EventName, entry.Action);
            Assert.Equal("renewal.approaching", entry.Action);
            Assert.Equal("Contract", entry.ResourceType);
            Assert.Equal(terms.ContractId.ToString(), entry.ResourceId);
            Assert.Equal(RenewalThresholdScheduler.SchedulerActor, entry.Actor);
            Assert.Equal(Tenant, entry.TenantId);
        });
    }

    [Fact]
    public async Task Honours_a_custom_configured_threshold_list()
    {
        var customOptions = new ThresholdWindowOptions { DaysBeforeDeadline = [45] };
        var scheduler = new RenewalThresholdScheduler(
            Clock, new RenewalEngine(Clock), _auditWriter, customOptions, new TenantContext());
        var matching = new ContractRenewalTerms(EntityId.New(), AsOf.AddDays(45), AutoRenewal: true, CancellationNoticeDays: null);
        var nonMatching = new ContractRenewalTerms(EntityId.New(), AsOf.AddDays(90), AutoRenewal: true, CancellationNoticeDays: null);

        var events = await scheduler.EvaluateThresholdsAsync(Tenant, [matching, nonMatching]);

        var raised = Assert.Single(events);
        Assert.Equal(matching.ContractId, raised.ContractId);
    }

    [Fact]
    public async Task Rejects_a_null_contracts_argument()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _scheduler.EvaluateThresholdsAsync(Tenant, null!));
    }

    [Fact]
    public async Task Processes_many_contracts_in_one_run_independently_preserving_correlation()
    {
        var first = new ContractRenewalTerms(EntityId.New(), AsOf.AddDays(30), AutoRenewal: true, CancellationNoticeDays: null);
        var second = new ContractRenewalTerms(EntityId.New(), AsOf.AddDays(365), AutoRenewal: true, CancellationNoticeDays: null);
        var untouched = new ContractRenewalTerms(EntityId.New(), AsOf.AddDays(100), AutoRenewal: true, CancellationNoticeDays: null);

        var events = await _scheduler.EvaluateThresholdsAsync(Tenant, [first, second, untouched]);

        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.ContractId == first.ContractId && e.ThresholdDays == 30);
        Assert.Contains(events, e => e.ContractId == second.ContractId && e.ThresholdDays == 365);
    }

    [Fact]
    public async Task Same_inputs_and_the_same_clock_produce_the_same_events_every_time()
    {
        var terms = new ContractRenewalTerms(EntityId.New(), AsOf.AddDays(90), AutoRenewal: true, CancellationNoticeDays: null);

        var first = await _scheduler.EvaluateThresholdsAsync(Tenant, [terms]);
        var second = await _scheduler.EvaluateThresholdsAsync(Tenant, [terms]);

        // RenewalApproachingEvent is a record: value equality across every field except EventId
        // (a fresh Guid per raise, by design — two separate raises are two separate events even
        // when every other field matches), so compare everything else explicitly.
        var a = Assert.Single(first);
        var b = Assert.Single(second);
        Assert.Equal(a with { EventId = default }, b with { EventId = default });
    }
}
