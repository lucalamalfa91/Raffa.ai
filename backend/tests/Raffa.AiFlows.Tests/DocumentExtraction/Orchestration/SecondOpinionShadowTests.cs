using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Raffa.AiFlows.DocumentExtraction.Orchestration;
using Raffa.AiFlows.DocumentExtraction.Stages;
using Raffa.AiFlows.Shared.Parsing;
using Raffa.AiFlows.Tests.TestSupport;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure.Extraction;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiFlows.Tests.DocumentExtraction.Orchestration;

/// <summary>A-07: the second opinion on high-impact fields is shadow-only and off by default.</summary>
public sealed class SecondOpinionShadowTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private const string DatesPrimary =
        """{"facts":[{"field":"endDate","value":"2027-12-31","sourcePage":1,"sourceSpan":"31 Dec 2027","confidence":0.97},{"field":"noticePeriodDays","value":"90","sourcePage":1,"sourceSpan":"90 days","confidence":0.97}]}""";

    private const string DatesShadowDisagree =
        """{"facts":[{"field":"endDate","value":"2028-06-30","sourcePage":1,"sourceSpan":"x","confidence":0.9},{"field":"noticePeriodDays","value":"90","sourcePage":1,"sourceSpan":"90 days","confidence":0.9}]}""";

    private sealed class Gateway : IAiGateway
    {
        public List<AiExtractionRequest> Requests { get; } = [];

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default)
        {
            string payload;
            lock (Requests)
            {
                Requests.Add(request);
            }

            // The focused (narrowed) schema is the shadow call.
            var shadow = request.StageName == "DatesAndRenewalTerms" && !request.JsonSchema.Contains("startDate", StringComparison.Ordinal);
            payload = request.StageName == "DatesAndRenewalTerms" ? (shadow ? DatesShadowDisagree : DatesPrimary) : "{}";
            return Task.FromResult(Result<AiExtractionResult>.Success(
                new AiExtractionResult(payload, new AiCallMetadata("m", "1", "p", Start, "h"))));
        }

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static async Task<(Result<StagedExtractionSummary> Result, Gateway Gateway, Contract? Contract)> RunAsync(
        ExtractionOrchestrationOptions options)
    {
        var database = Guid.NewGuid().ToString("N");
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        EntityId documentId;
        await using (var seed = InMemoryDocumentsDb.Create(database))
        {
            var document = new Document
            {
                TenantId = tenantId,
                FileName = "c.pdf",
                MimeType = "application/pdf",
                StoragePath = $"{tenantId.Value:D}/documents/c.pdf",
                Checksum = "c",
                ProcessingStatus = DocumentProcessingStatus.Uploaded,
                CreatedAt = Start,
            };
            seed.Documents.Add(document);
            await seed.SaveChangesAsync();
            documentId = document.Id;
        }

        var gateway = new Gateway();
        await using var db = InMemoryDocumentsDb.Create(database);
        var orchestrator = new ExtractionOrchestrator(
            new ExtractionRunStore(db, tenantContext, new FixedClock(Start), new RecordingAuditWriter()),
            gateway,
            ExtractionStages.Default,
            tenantContext,
            options);

        var result = await orchestrator.RunAsync(tenantId, documentId, [new DocumentPageText(1, "31 Dec 2027, 90 days")]);
        await using var read = InMemoryDocumentsDb.Create(database);
        var contract = await read.Contracts.AsNoTracking().FirstOrDefaultAsync();
        return (result, gateway, contract);
    }

    [Fact]
    public async Task It_is_off_by_default_and_costs_no_extra_call()
    {
        Assert.False(new ExtractionOrchestrationOptions().SecondOpinionShadow);

        var (result, gateway, _) = await RunAsync(new ExtractionOrchestrationOptions());

        Assert.True(result.IsSuccess);
        Assert.Equal(7, gateway.Requests.Count);
    }

    [Fact]
    public async Task When_on_it_adds_focused_calls_only_for_stages_owning_high_impact_fields_and_changes_nothing()
    {
        var (off, _, offContract) = await RunAsync(new ExtractionOrchestrationOptions());

        var observed = new List<(string Field, string Outcome)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "raffa.extraction.second_opinion")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var field = tags.ToArray().Single(t => t.Key == "field").Value?.ToString() ?? "";
            var outcome = tags.ToArray().Single(t => t.Key == "outcome").Value?.ToString() ?? "";
            lock (observed)
            {
                observed.Add((field, outcome));
            }
        });
        listener.Start();

        var (on, gateway, onContract) = await RunAsync(new ExtractionOrchestrationOptions { SecondOpinionShadow = true });

        // 7 primary + Dates (endDate, noticePeriodDays) + CommercialTerms (annualSpend) focused calls.
        Assert.Equal(9, gateway.Requests.Count);
        var focused = gateway.Requests.Skip(7).ToList();
        Assert.Equal(["CommercialTerms", "DatesAndRenewalTerms"], focused.Select(r => r.StageName).Order());
        Assert.All(focused, r => Assert.DoesNotContain("startDate", r.JsonSchema, StringComparison.Ordinal));

        // Nothing that changes values: same outcome, same persisted contract values.
        Assert.Equal(off.Value.DocumentProcessingStatus, on.Value.DocumentProcessingStatus);
        Assert.Equal(off.Value.Stages, on.Value.Stages);
        Assert.Equal(new DateOnly(2027, 12, 31), onContract!.EndDate);
        Assert.Equal(offContract!.EndDate, onContract.EndDate);
        Assert.Equal(offContract.NoticePeriodDays, onContract.NoticePeriodDays);

        lock (observed)
        {
            Assert.Contains(("endDate", "Disagree"), observed);
            Assert.Contains(("noticePeriodDays", "Agree"), observed);
            Assert.Contains(("annualSpend", "BothAbsent"), observed);
        }
    }

    [Fact]
    public void Compare_treats_equivalent_formats_as_agreement_and_reports_one_sided_readings()
    {
        var primary = new StageFacts.Scalars(ExtractionStage.DatesAndRenewalTerms,
        [
            new ExtractedFact("endDate", "2027-12-31", 1, "a", 0.9),
            new ExtractedFact("noticePeriodDays", "90", 1, "a", 0.9),
            new ExtractedFact("annualSpend", "12000.00", 1, "a", 0.9),
        ]);
        var second = new StageFacts.Scalars(ExtractionStage.DatesAndRenewalTerms,
        [
            new ExtractedFact("endDate", "12/31/2027", 1, "a", 0.9),
            new ExtractedFact("noticePeriodDays", "60", 1, "a", 0.9),
        ]);

        var outcomes = SecondOpinionShadow.Compare(primary, second, SecondOpinionShadow.HighImpactFields)
            .ToDictionary(o => o.Field, o => o.Outcome);

        Assert.Equal(SecondOpinionOutcome.Agree, outcomes["endDate"]);
        Assert.Equal(SecondOpinionOutcome.Disagree, outcomes["noticePeriodDays"]);
        Assert.Equal(SecondOpinionOutcome.PrimaryOnly, outcomes["annualSpend"]);
    }

    [Fact]
    public void Only_the_dates_and_commercial_stages_own_high_impact_fields()
    {
        Assert.Equal(["endDate", "noticePeriodDays"], SecondOpinionShadow.FieldsOf(ExtractionStage.DatesAndRenewalTerms));
        Assert.Equal(["annualSpend"], SecondOpinionShadow.FieldsOf(ExtractionStage.CommercialTerms));
        Assert.Empty(SecondOpinionShadow.FieldsOf(ExtractionStage.Metadata));
        Assert.Empty(SecondOpinionShadow.FieldsOf(ExtractionStage.Risk));
    }
}
