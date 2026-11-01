using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Application.Facts;
using Raffa.Documents.Contracts.Application.V4;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Documents.Contracts.Infrastructure.Fixtures;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Fixtures;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>AskRaffa 4.0 slice 4: Revisione reading of the sample, decision points, renewed terms,
/// Contratti/Contratto read models.</summary>
public sealed class ContractsV4Tests
{
    private static readonly TenantId Tenant = new(Guid.Parse("4a5ec0de-a4a4-4f00-8000-000000000001"));
    private static readonly DateTimeOffset Start = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    private sealed class NullAudit : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CaseReader(ContractCaseStatus status) : IContractCaseStatusReader
    {
        public Task<IReadOnlyDictionary<Guid, ContractCaseStatus>> GetAsync(
            TenantId tenantId, IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ContractCaseStatus>>(
                contractIds.ToDictionary(id => id, id => status with { ContractId = id }));
    }

    private static FixtureSeedContext Context(DateTimeOffset now) =>
        new(Tenant, "Helvetia Foods AG", [], now, DataDirectory());

    private static string DataDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "backend", "fixtures", "askraffa4");
            if (File.Exists(Path.Combine(candidate, "contracts.json")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("backend/fixtures/askraffa4 not found");
    }

    private sealed record Harness(
        DocumentsContractsDbContext Db,
        MutableClock Clock,
        DocumentReadingService Reading,
        ContractFactsReader Facts,
        ContractsV4QueryService Query,
        ContractDecisionPointService Points,
        FixtureSampleDocuments Samples);

    private static Harness Create(IContractCaseStatusReader? cases = null)
    {
        var db = InMemoryDocumentsDb.Create(Guid.NewGuid().ToString());
        var tenantContext = new TenantContext();
        var clock = new MutableClock(Start);
        var audit = new NullAudit();
        var reading = new DocumentReadingService(
            db, tenantContext, clock,
            new ContractCorrectionService(db, tenantContext, clock, audit),
            new DocumentValidationService(db, tenantContext, audit, clock),
            [new FixtureSampleDocumentReading()]);
        var facts = new ContractFactsReader(db, tenantContext, clock);
        var points = new ContractDecisionPointService(db, tenantContext, facts, clock);
        var query = new ContractsV4QueryService(facts, cases ?? new NoContractCaseStatusReader(), points, reading, clock);
        return new Harness(db, clock, reading, facts, query, points, new FixtureSampleDocuments(db, tenantContext, clock));
    }

    [Fact]
    public async Task Sample_reading_follows_the_prototype_timer_then_asks_the_two_fields()
    {
        var h = Create();
        var documentId = new EntityId(await h.Samples.UploadAsync(Context(Start)));

        h.Clock.UtcNow = Start.AddMilliseconds(700);
        var first = await h.Reading.GetAsync(Tenant, documentId);
        Assert.Equal(ReadingStatus.Processing, first!.Status);
        Assert.Equal("Uploading", first.StageLabel.Id);
        Assert.Equal("Salesforce_MSA_2024.pdf", first.File);

        h.Clock.UtcNow = Start.AddMilliseconds(1300);
        Assert.Equal("Classification", (await h.Reading.GetAsync(Tenant, documentId))!.StageLabel.Id);

        h.Clock.UtcNow = Start.AddMilliseconds(3100);
        Assert.Equal("Extracting the facts", (await h.Reading.GetAsync(Tenant, documentId))!.StageLabel.Id);

        h.Clock.UtcNow = Start.AddMilliseconds(3700);
        var review = await h.Reading.GetAsync(Tenant, documentId);
        Assert.Equal(ReadingStatus.Review, review!.Status);
        Assert.Equal(["sf_renewterm", "sf_uplift"], review.Fields.Select(f => f.Key));
        Assert.Equal("Renewal term", review.Fields[0].Label);
        Assert.Equal("renew for successive periods equal in length to the initial Subscription Term", review.Fields[0].Quote!.Mark);
        Assert.True(review.Fields[0].Critical);

        var attention = await h.Reading.GetAttentionAsync(Tenant);
        Assert.Equal(2, attention!.Flagged);

        // Uploading the sample again returns the same document.
        Assert.Equal(documentId.Value, await h.Samples.UploadAsync(Context(Start)));
    }

    [Fact]
    public async Task Accepting_both_fields_validates_the_contract_for_every_reader()
    {
        var h = Create();
        var documentId = new EntityId(await h.Samples.UploadAsync(Context(Start)));
        h.Clock.UtcNow = Start.AddSeconds(4);
        Assert.Empty(await h.Facts.ListValidatedAsync(Tenant));

        var after1 = await h.Reading.DecideAsync(Tenant, documentId, "sf_renewterm", FieldDecisions.Accept, null, "luca-oid", "Luca Lamalfa");
        Assert.True(after1!.IsSuccess);
        Assert.Equal(ReadingStatus.Review, after1.Value.Status);

        var after2 = await h.Reading.DecideAsync(Tenant, documentId, "sf_uplift", FieldDecisions.Correct, "7%", "luca-oid", "Luca Lamalfa");
        Assert.Equal(ReadingStatus.Done, after2!.Value.Status);
        Assert.Equal("Salesforce", after2.Value.Supplier);

        var validated = Assert.Single(await h.Facts.ListValidatedAsync(Tenant));
        Assert.Equal("Salesforce", validated.Supplier);
        Assert.Equal("7%", validated.ReviewFields.Single(f => f.Ev == "sf_uplift").Value);
        Assert.Null(await h.Reading.GetAttentionAsync(Tenant));

        var list = await h.Query.ListAsync(Tenant);
        var row = Assert.Single(list.Rows);
        Assert.Equal("18 Oct 2026", row.NoticeDeadline);
        Assert.Equal(40, row.NoticeDays);
        Assert.Equal(ContractRowStatus.Validated, row.Status);
    }

    [Fact]
    public async Task Decisions_on_points_are_recorded_and_cleared()
    {
        var h = Create();
        var documentId = new EntityId(await h.Samples.UploadAsync(Context(Start)));
        h.Clock.UtcNow = Start.AddSeconds(4);
        await h.Reading.DecideAsync(Tenant, documentId, "sf_renewterm", FieldDecisions.Accept, null, "s", "Luca Lamalfa");
        await h.Reading.DecideAsync(Tenant, documentId, "sf_uplift", FieldDecisions.Accept, null, "s", "Luca Lamalfa");
        var contractId = FixtureIds.Contract(Tenant, 1);

        var points = (await h.Points.ListAsync(Tenant, contractId))!;
        Assert.Equal(["price-increase", "auto-renewal", "limitation-of-liability"], points.Select(p => p.Key));
        Assert.Equal(["discuss", "discuss", "risk"], points.Select(p => p.Tag));
        Assert.Equal("Costs money or freedom at renewal if left as is.", Assert.IsType<ContractMessage>(points[0].Why).Id);
        Assert.StartsWith("Missing 18 Oct 2026", Assert.IsType<string>(points[1].Why), StringComparison.Ordinal);
        Assert.NotNull(points[2].PolicyNote);

        var set = await h.Points.SetAsync(Tenant, contractId, "auto-renewal", PointDecisions.Negotiate, "Luca Lamalfa");
        Assert.Equal(PointDecisions.Negotiate, set!.Value.Single(p => p.Key == "auto-renewal").Decision);
        Assert.Equal("Luca Lamalfa", set.Value.Single(p => p.Key == "auto-renewal").DecidedBy);

        var cleared = await h.Points.SetAsync(Tenant, contractId, "auto-renewal", null, "Luca Lamalfa");
        Assert.Null(cleared!.Value.Single(p => p.Key == "auto-renewal").Decision);

        Assert.True((await h.Points.SetAsync(Tenant, contractId, "nope", PointDecisions.Accept, "Luca"))!.IsFailure);
        Assert.True((await h.Points.SetAsync(Tenant, contractId, "auto-renewal", "maybe", "Luca"))!.IsFailure);
    }

    [Fact]
    public async Task Renewed_terms_replace_dates_and_spend_and_the_row_shows_the_outcome()
    {
        var h = Create(new CaseReader(new ContractCaseStatus(Guid.Empty, false, Guid.NewGuid(), ContractCaseOutcome.Renewed, null)));
        var documentId = new EntityId(await h.Samples.UploadAsync(Context(Start)));
        h.Clock.UtcNow = Start.AddSeconds(4);
        await h.Reading.DecideAsync(Tenant, documentId, "sf_renewterm", FieldDecisions.Accept, null, "s", "Luca");
        await h.Reading.DecideAsync(Tenant, documentId, "sf_uplift", FieldDecisions.Accept, null, "s", "Luca");
        var contractId = FixtureIds.Contract(Tenant, 1);

        var writer = new ContractTermWriter(h.Db, new TenantContext(), h.Clock);
        await writer.WriteCurrentAsync(Tenant, new ContractTermVersionInput(
            contractId.Value, null, new DateOnly(2027, 1, 16), new DateOnly(2028, 1, 15), 90, 620000m, "CHF", true, 3m));
        await writer.WriteCurrentAsync(Tenant, new ContractTermVersionInput(
            contractId.Value, null, new DateOnly(2027, 1, 16), new DateOnly(2029, 1, 15), 60, 600000m, "CHF", false, 0m));

        var facts = await h.Facts.GetAsync(Tenant, contractId);
        Assert.Equal(new DateOnly(2029, 1, 15), facts!.EndDate);
        Assert.Equal(new DateOnly(2028, 11, 16), facts.NoticeDeadline);
        Assert.Equal("16 Nov 2028", facts.Cancel);
        Assert.Equal(600000m, facts.Spend);
        Assert.Equal("No", facts.Auto);

        var row = Assert.Single((await h.Query.ListAsync(Tenant)).Rows);
        Assert.Equal(ContractRowStatus.Renewed, row.Status);
        Assert.Equal("15 Jan 2029", row.End);
        Assert.Equal(1, h.Db.ContractTermVersions.Count(v => v.IsCurrent));
    }

    [Fact]
    public async Task Contract_view_carries_dates_points_and_the_case_next_action()
    {
        var caseId = Guid.NewGuid();
        var next = ContractMessage.Of("{who} — {action}", ("who", "You"), ("action", "Answer the questions"));
        var h = Create(new CaseReader(new ContractCaseStatus(Guid.Empty, true, caseId, null, next)));
        var documentId = new EntityId(await h.Samples.UploadAsync(Context(Start)));
        h.Clock.UtcNow = Start.AddSeconds(4);
        await h.Reading.DecideAsync(Tenant, documentId, "sf_renewterm", FieldDecisions.Accept, null, "s", "Luca");
        await h.Reading.DecideAsync(Tenant, documentId, "sf_uplift", FieldDecisions.Accept, null, "s", "Luca");

        var view = await h.Query.GetAsync(Tenant, FixtureIds.Contract(Tenant, 1));
        Assert.Equal("Sales Cloud Enterprise — MSA + Order Form", view!.Name);
        Assert.Equal(40, view.Dates.NoticeDays);
        Assert.Equal("01 Mar 2024", view.Dates.Start);
        Assert.Equal(12, view.Dates.RenewalMonths);
        Assert.Equal(3, view.DecisionPoints.Count);
        Assert.Equal(caseId, view.NextAction.CaseId);
        Assert.Same(next, view.NextAction.Text);
        Assert.Equal(3, view.Products.Count);
        Assert.Equal(ContractRowStatus.CaseOpen, Assert.Single((await h.Query.ListAsync(Tenant)).Rows).Status);
    }

    [Theory]
    [InlineData(40, "Start the renewal and assign an owner")]
    [InlineData(200, "Start the renewal by {date}")]
    public void Next_action_without_a_case_starts_the_renewal(int noticeDays, string expected)
    {
        var facts = new ContractFacts { NoticeDeadline = new DateOnly(2027, 3, 1) };
        var next = ContractsV4QueryService.NextAction(facts, ContractCaseStatus.None(Guid.NewGuid()), noticeDays);
        Assert.False(next.HasCase);
        Assert.Equal(expected, next.Text.Id);
    }

    [Fact]
    public void Flag_reason_names_the_confidence_and_criticality()
    {
        var critical = ExtractionFlagReason.For("annualSpend", 0.64);
        Assert.Contains("Critical field", critical.Id, StringComparison.Ordinal);
        Assert.Equal(64, critical.Vars!["pct"]);
        Assert.Equal(90, critical.Vars["threshold"]);
        Assert.DoesNotContain("Critical", ExtractionFlagReason.For("paymentTerms", 0.5).Id, StringComparison.Ordinal);
        var quote = ExtractionFlagReason.Quote("abc DEF ghi", 4, 3, null);
        Assert.Equal(new ReadingQuote("abc ", "DEF", " ghi"), quote);
    }
}
