using System.Net;
using System.Text.Json;
using Raffa.Audit.Infrastructure;
using Raffa.Documents.Contracts.Domain;
using Raffa.Renewals.Application;
using Raffa.Renewals.Domain;
using Raffa.Renewals.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.IntegrationTests;

/// <summary>
/// Proves the Definition of Done for task E03/F04/US01/T01 (r2-integration) and its parent story
/// us-01-final-integration: AC-1 ("Deterministic renewal/cancellation for every active contract
/// (where data exists)"), AC-2 ("Threshold events fire and recommendations do not invent dates")
/// and the pipeline/insight-card half of AC-3 ("Pipeline + insight card work on `demo` with tenant
/// isolation" — see <see cref="R2CrossTenantIsolationTests"/> for the isolation half, mirroring how
/// R1 split <see cref="R1EndToEndTests"/>/<see cref="R1CrossTenantIsolationTests"/>) — driven over
/// real HTTP through the real <c>Raffa.Api</c> composition root, against a real, migrated
/// Postgres+pgvector+RLS database (see <see cref="R2IntegrationFixture"/>).
///
/// R2's own leaf artifacts (this task's own <c>depends_on</c>: <c>renewal-opportunity</c>,
/// <c>renewal-priority-explain</c>, <c>renewal-alerts</c>, <c>renewal-action</c>) all take
/// already-validated contract data as an input; none of them produces it. So unlike
/// <see cref="R1EndToEndTests"/> (which proves the upload -&gt; extraction path that *creates* a
/// contract), this test seeds contracts directly against the real, RLS-enforced
/// <c>DocumentsContractsDbContext</c> (see <see cref="R2IntegrationFixture.SeedContractAsync"/>),
/// then proves the Renewals layer end to end on top of it. Reads response bodies as raw
/// <see cref="JsonElement"/>s rather than typed DTOs — same reason as
/// <see cref="R0EndToEndTests"/>/<see cref="R1EndToEndTests"/>'s own doc comments. Reuses
/// <see cref="R1EndToEndTests"/>'s <c>GetAsync</c>/<c>PostAsync</c>/<c>ParseAsync</c> helpers rather
/// than duplicating them — the same cross-class reuse <see cref="R1CrossTenantIsolationTests"/>
/// already established for this test assembly (they are generic HTTP plumbing, not R1-specific).
///
/// <para>
/// <b>Renewal alerts (updated by task E03/F02/US01/T02):</b> this task's own wave-spec
/// <c>depends_on</c> named <c>renewal-alerts</c> (task E03/F02/US01/T02, "Alert creation +
/// re-compute on correction"), which had not landed any code as of this task's own original run —
/// only the <c>renewal.approaching</c> <em>threshold event</em> (task E03/F02/US01/T01,
/// <c>threshold-scheduler</c>) existed then, a durable, queryable
/// <see cref="Raffa.SharedKernel.IAuditWriter"/> entry, not a persisted, de-duplicated
/// <see cref="RenewalAlert"/> row. Task E03/F02/US01/T02 has since landed both halves parent story
/// AC-2/AC-3 name: <see cref="Raffa.Renewals.Application.RenewalAlertService"/> persists a
/// de-duplicated <see cref="RenewalAlert"/> row per raised event, and recomputes/resolves a
/// contract's alerts when its terms are corrected (via
/// <c>Raffa.Api.RenewalAlertRecomputeService</c>, called from `PATCH /api/contracts/{id}`). See
/// <see cref="Renewal_alerts_are_created_from_thresholds_and_recomputed_on_contract_correction"/>
/// below for the proof — added by that task, not this one, so the assertions above (this task's own
/// original scope) are unchanged.
/// </para>
///
/// <para>
/// While proving AC-2 for real against this fixture's deliberately unprivileged, <c>NOBYPASSRLS</c>
/// Postgres role (see <see cref="R2IntegrationFixture"/>'s own doc comment), this task found and
/// fixed a real gap in the already-merged <c>threshold-scheduler</c> artifact:
/// <see cref="RenewalThresholdScheduler.EvaluateThresholdsAsync"/> wrote its
/// <c>renewal.approaching</c> audit entry without ever opening an <see cref="ITenantContext"/>
/// scope, so <c>TenantRlsConnectionInterceptor</c> left `app.tenant_id` unset and the Audit
/// module's own `AddTenantRowLevelSecurity` `WITH CHECK` policy rejected the insert outright — a
/// scheduler run that actually raised an event would throw, not "fire silently wrong". Neither
/// <c>Raffa.Renewals.Tests.RenewalThresholdSchedulerTests</c> (a <c>RecordingAuditWriter</c>, no
/// database) nor <c>Raffa.Worker.Tests.RenewalThresholdSchedulerHostedServiceTests</c> (a
/// syntactically-valid-but-never-dialled connection string, by its own design) ever exercised a
/// real RLS-enforced connection on this path, so this was previously undetected.
/// <see cref="RenewalThresholdScheduler"/> now opens its own scope, the same convention
/// <c>Raffa.Renewals.Application.RenewalActionService.SetActionAsync</c> already follows — the
/// threshold-scheduler assertions below are the proof the fix actually works end to end.
/// </para>
/// </summary>
public sealed class R2EndToEndTests : IClassFixture<R2IntegrationFixture>
{
    private readonly R2IntegrationFixture _fixture;

    public R2EndToEndTests(R2IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Dates_priority_alerts_and_action_compose_into_one_prioritized_pipeline()
    {
        var client = _fixture.CreateClient();
        var tenantId = TenantId.New();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

        // Contract A: near-term, high spend, a known raw cancellation-deadline fact, High risk —
        // expected to lead the pipeline and to cross a configured threshold window (AC-1, AC-2).
        var contractA = await _fixture.SeedContractAsync(
            tenantId, supplierId: EntityId.New(), annualSpend: 300_000m,
            endDate: today.AddDays(90), cancellationDeadline: today.AddDays(60),
            autoRenewal: true, risk: RiskSeverity.High);

        // Contract B: far out, low spend, no independently-known cancellation deadline — expected
        // to sort after A and to raise no threshold event (400 is not one of the default
        // 365/270/180/120/90/60/30-day windows).
        var contractB = await _fixture.SeedContractAsync(
            tenantId, annualSpend: 10_000m, endDate: today.AddDays(400), autoRenewal: true);

        // Contract C: AutoRenewal is true but EndDate is unknown — AC-1's own "(where data exists)"
        // boundary and AC-2's "recommendations do not invent dates": every layer below must abstain
        // honestly instead of guessing (Appendix C rule 10).
        var contractC = await _fixture.SeedContractAsync(tenantId, autoRenewal: true, endDate: null);

        // ----- GET /api/renewals: prioritized pipeline + insight card (AC-1, AC-3) -----

        var pipelineResponse = await R1EndToEndTests.GetAsync(client, "/api/renewals", tenantId.Value);
        Assert.Equal(HttpStatusCode.OK, pipelineResponse.StatusCode);
        var pipelineBody = await R1EndToEndTests.ParseAsync(pipelineResponse);
        var items = pipelineBody.GetProperty("items").EnumerateArray().ToList();

        // Most-urgent-first: A (60 days to its known cancellation deadline) before B (400 days to
        // its renewal date, no cancellation deadline known) before C (nothing determinable at all).
        Assert.Equal(
            new[] { contractA.Id.Value, contractB.Id.Value, contractC.Id.Value },
            items.Select(i => i.GetProperty("contractId").GetGuid()).ToArray());

        var itemA = items[0];
        Assert.Equal("Determined", itemA.GetProperty("status").GetString());
        Assert.Equal(today.AddDays(90), DateOnly.Parse(itemA.GetProperty("renewalDate").GetString()!));
        Assert.Equal(90, itemA.GetProperty("daysUntilRenewal").GetInt32());
        Assert.Equal(60, itemA.GetProperty("daysUntilCancellationDeadline").GetInt32());
        Assert.Equal("Start negotiation now", itemA.GetProperty("action").GetString());
        Assert.Equal(300_000m, itemA.GetProperty("annualSpend").GetDecimal());

        var itemB = items[1];
        Assert.Equal("Determined", itemB.GetProperty("status").GetString());
        Assert.Equal(400, itemB.GetProperty("daysUntilRenewal").GetInt32());
        Assert.Equal(JsonValueKind.Null, itemB.GetProperty("daysUntilCancellationDeadline").ValueKind);
        Assert.Equal("Monitor — no action needed yet", itemB.GetProperty("action").GetString());

        // AC-2 "do not invent dates": C never gets a fabricated renewal date just because the
        // pipeline needs to show *something*.
        var itemC = items[2];
        Assert.Equal("CannotDetermine", itemC.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, itemC.GetProperty("renewalDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, itemC.GetProperty("daysUntilRenewal").ValueKind);
        Assert.Equal("Review contract — missing end date", itemC.GetProperty("action").GetString());

        // Insight card separates facts from recommendations (AC-3, spec §9.3) for the lead row.
        var insightCard = itemA.GetProperty("insightCard");
        Assert.Equal(300_000m, insightCard.GetProperty("facts").GetProperty("annualSpend").GetDecimal());
        Assert.Equal(
            "Start negotiation now",
            insightCard.GetProperty("recommendations").GetProperty("recommendedAction").GetString());

        // ----- GET /api/renewals/{id}/priority: explainable, component-scored priority
        //       (renewal-priority-explain) -----

        var priorityAResponse = await R1EndToEndTests.GetAsync(
            client, $"/api/renewals/{contractA.Id.Value}/priority", tenantId.Value);
        Assert.Equal(HttpStatusCode.OK, priorityAResponse.StatusCode);
        var priorityABody = await R1EndToEndTests.ParseAsync(priorityAResponse);
        var componentsA = priorityABody.GetProperty("components");

        // Exact numbers under the spec-default weights (PriorityScoreWeightsOptions, max 20 each):
        // spend >=250k -> 0.8*20=16; 90 days until renewal -> 0.75*20=15; no benchmark data ->
        // neutral 10; no uplift data -> 0; High contract risk -> 0.75*20=15. Total 16+15+10+0+15=56.
        Assert.Equal(16m, componentsA.GetProperty("spendWeight").GetProperty("score").GetDecimal());
        Assert.Equal(15m, componentsA.GetProperty("timeUrgency").GetProperty("score").GetDecimal());
        Assert.Equal(10m, componentsA.GetProperty("benchmarkOpportunity").GetProperty("score").GetDecimal());
        Assert.Equal(0m, componentsA.GetProperty("priceIncreaseRisk").GetProperty("score").GetDecimal());
        Assert.Equal(15m, componentsA.GetProperty("contractRisk").GetProperty("score").GetDecimal());
        Assert.Equal(56m, priorityABody.GetProperty("totalScore").GetDecimal());

        // AC-2 "do not invent dates" holds at the priority layer too: C's unknown renewal date
        // never fabricates a time-urgency score — it honestly defaults to the minimum.
        var priorityCResponse = await R1EndToEndTests.GetAsync(
            client, $"/api/renewals/{contractC.Id.Value}/priority", tenantId.Value);
        Assert.Equal(HttpStatusCode.OK, priorityCResponse.StatusCode);
        var priorityCBody = await R1EndToEndTests.ParseAsync(priorityCResponse);
        Assert.Equal(
            0m, priorityCBody.GetProperty("components").GetProperty("timeUrgency").GetProperty("score").GetDecimal());

        // ----- Threshold scheduler: "Threshold events fire" (AC-2), never fabricated -----

        using (var scope = _fixture.Services.CreateScope())
        {
            var scheduler = scope.ServiceProvider.GetRequiredService<RenewalThresholdScheduler>();
            var terms = new[]
            {
                new ContractRenewalTerms(contractA.Id, today.AddDays(90), AutoRenewal: true, CancellationNoticeDays: null),
                new ContractRenewalTerms(contractB.Id, today.AddDays(400), AutoRenewal: true, CancellationNoticeDays: null),
                new ContractRenewalTerms(contractC.Id, EndDate: null, AutoRenewal: true, CancellationNoticeDays: null),
            };

            var events = await scheduler.EvaluateThresholdsAsync(tenantId, terms);

            // Exactly one real threshold crossing (A at 90 days) — B (400) matches none of the
            // default 365/270/180/120/90/60/30 windows and C has nothing determinable at all, so
            // neither ever raises a fabricated event (Appendix C rule 10).
            var raised = Assert.Single(events);
            Assert.Equal(contractA.Id, raised.ContractId);
            Assert.Equal(RenewalMilestoneKind.RenewalDate, raised.Milestone);
            Assert.Equal(90, raised.ThresholdDays);
            Assert.Equal(today.AddDays(90), raised.MilestoneDate);
        }

        // The event above is durable and queryable (spec Appendix B), not just an in-memory return
        // value — this is the actual proof of the RLS-scope fix this task made (see the type doc
        // comment): the audit insert really committed against this fixture's RLS-enforced,
        // NOBYPASSRLS connection, not a superuser connection that would let it through regardless.
        using (var scope = _fixture.Services.CreateScope())
        {
            var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var tenantScope = tenantContext.BeginScope(tenantId);

            var approaching = await auditDb.AuditEvents
                .Where(e => e.Action == RenewalApproachingEvent.EventName)
                .ToListAsync();

            var entry = Assert.Single(approaching);
            Assert.Equal(contractA.Id.Value.ToString(), entry.ResourceId);
            Assert.Equal(RenewalThresholdScheduler.SchedulerActor, entry.Actor);
        }

        // ----- POST /api/renewals/{id}/action: renewal-action, then upsert -----

        var setActionResponse = await R1EndToEndTests.PostAsync(
            client, $"/api/renewals/{contractA.Id.Value}/action", tenantId.Value,
            new { owner = "procurement@acme.example", status = "InProgress", action = "Started negotiation" });
        Assert.Equal(HttpStatusCode.OK, setActionResponse.StatusCode);
        var setActionBody = await R1EndToEndTests.ParseAsync(setActionResponse);
        Assert.Equal("InProgress", setActionBody.GetProperty("status").GetString());

        // Upsert, not a second row — and no dedicated GET route exists yet (see
        // RenewalActionService's own doc comment), so read back through the same service the host
        // resolves, the same "prove persistence really happened" shape R1EndToEndTests already
        // uses for ExtractionEvidence.
        var updateActionResponse = await R1EndToEndTests.PostAsync(
            client, $"/api/renewals/{contractA.Id.Value}/action", tenantId.Value,
            new { owner = "procurement@acme.example", status = "Completed", action = "Renewed at same terms" });
        Assert.Equal(HttpStatusCode.OK, updateActionResponse.StatusCode);

        using (var scope = _fixture.Services.CreateScope())
        {
            var actionService = scope.ServiceProvider.GetRequiredService<RenewalActionService>();
            var action = await actionService.GetActionAsync(tenantId, contractA.Id);

            Assert.NotNull(action);
            Assert.Equal(RenewalActionStatus.Completed, action!.Status);
            Assert.Equal("Renewed at same terms", action.Action);
        }
    }

    /// <summary>
    /// Proves the Definition of Done for task E03/F02/US01/T02 (renewal-alerts) and closes the gap
    /// this file's own class doc comment used to name: end to end, over real HTTP against a real,
    /// migrated, RLS-enforced Postgres — AC-2 ("Emits <c>renewal.approaching</c> events creating
    /// alerts") and AC-3 ("Scheduler recomputes when a contract/term is corrected").
    ///
    /// Alert creation itself is exercised directly against the API host's own DI container (the
    /// same two-step composition <c>Raffa.Worker.Scheduling.RenewalThresholdSchedulerHostedService
    /// .RunOnceAsync</c> performs on a real tick — no separate Worker host runs in this fixture, see
    /// <see cref="R2IntegrationFixture"/>'s own doc comment), then the recompute half is driven
    /// through the real `PATCH /api/contracts/{id}` endpoint so the actual
    /// <c>Raffa.Api.RenewalAlertRecomputeService</c> wiring — not just
    /// <see cref="Raffa.Renewals.Application.RenewalAlertService"/> in isolation (already proved
    /// by <c>Raffa.Renewals.Tests.RenewalAlertServiceTests</c>) — is what this test proves.
    /// </summary>
    [Fact]
    public async Task Renewal_alerts_are_created_from_thresholds_and_recomputed_on_contract_correction()
    {
        var client = _fixture.CreateClient();
        var tenantId = TenantId.New();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

        // Seeded exactly 90 days out -- a configured threshold window (AC-2).
        var contract = await _fixture.SeedContractAsync(
            tenantId, annualSpend: 50_000m, endDate: today.AddDays(90), autoRenewal: true);

        using (var scope = _fixture.Services.CreateScope())
        {
            var scheduler = scope.ServiceProvider.GetRequiredService<RenewalThresholdScheduler>();
            var alertService = scope.ServiceProvider.GetRequiredService<RenewalAlertService>();
            var terms = new ContractRenewalTerms(
                contract.Id, today.AddDays(90), AutoRenewal: true, CancellationNoticeDays: null);

            var events = await scheduler.EvaluateThresholdsAsync(tenantId, [terms]);
            await alertService.CreateFromEventsAsync(tenantId, events);
        }

        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RenewalsDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var tenantScope = tenantContext.BeginScope(tenantId);

            var alert = Assert.Single(await db.RenewalAlerts.Where(a => a.ContractId == contract.Id).ToListAsync());
            Assert.Equal(RenewalAlertStatus.Active, alert.Status);
            Assert.Equal(90, alert.ThresholdDays);
            Assert.Equal(today.AddDays(90), alert.MilestoneDate);
        }

        // ----- AC-3: correcting endDate off every configured threshold resolves the alert
        //       automatically, as a side effect of PATCH /api/contracts/{id} -- no separate call. -----

        var offThresholdResponse = await R1EndToEndTests.PatchAsync(
            client, $"/api/contracts/{contract.Id.Value}", tenantId.Value,
            new { corrections = new Dictionary<string, string?> { ["endDate"] = today.AddDays(200).ToString("yyyy-MM-dd") } });
        Assert.Equal(HttpStatusCode.OK, offThresholdResponse.StatusCode);

        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RenewalsDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var tenantScope = tenantContext.BeginScope(tenantId);

            // Superseded, not deleted (Appendix C rule 5) -- the stale row's own MilestoneDate is
            // untouched even though it no longer matches reality.
            var alert = Assert.Single(await db.RenewalAlerts.Where(a => a.ContractId == contract.Id).ToListAsync());
            Assert.Equal(RenewalAlertStatus.Resolved, alert.Status);
            Assert.Equal(today.AddDays(90), alert.MilestoneDate);
        }

        // ----- A further correction landing exactly on a new threshold creates a fresh alert
        //       (same PATCH endpoint, same automatic recompute). -----

        var newThresholdResponse = await R1EndToEndTests.PatchAsync(
            client, $"/api/contracts/{contract.Id.Value}", tenantId.Value,
            new { corrections = new Dictionary<string, string?> { ["endDate"] = today.AddDays(60).ToString("yyyy-MM-dd") } });
        Assert.Equal(HttpStatusCode.OK, newThresholdResponse.StatusCode);

        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RenewalsDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var tenantScope = tenantContext.BeginScope(tenantId);

            var alerts = await db.RenewalAlerts.Where(a => a.ContractId == contract.Id).ToListAsync();
            // The original (now Resolved, kept as honest history) plus one fresh Active alert --
            // never a second row for the same threshold, never a silently-discarded first one.
            Assert.Equal(2, alerts.Count);
            Assert.Single(alerts, a => a.Status == RenewalAlertStatus.Resolved);
            var active = Assert.Single(alerts, a => a.Status == RenewalAlertStatus.Active);
            Assert.Equal(60, active.ThresholdDays);
            Assert.Equal(today.AddDays(60), active.MilestoneDate);
        }
    }
}
