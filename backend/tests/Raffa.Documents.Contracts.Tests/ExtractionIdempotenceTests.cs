using System.Collections.Concurrent;
using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// F5-T01 (idempotent facts, human corrections protected), F5-T02 (typed stage failure, partial
/// documents, checkpoint and resume) and F5-D08 (cancellation deadline derived from the notice
/// period) -- proved against <see cref="StagedExtractionService"/> over the EF InMemory provider, so
/// they run without Docker. What InMemory cannot show (the partial unique index, the SQL of the
/// migration) is covered by the model assertions here and by the Postgres-backed tests.
/// </summary>
public sealed class ExtractionIdempotenceTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private const string UnavailableError = AiGatewayErrors.UnavailablePrefix + " 'https://foundry/extract' still failing after 3 retries (last outcome: 429 TooManyRequests).";

    private static readonly IReadOnlyList<DocumentPageText> Pages =
    [
        new DocumentPageText(1, "MSA header. Currency: USD. Notice: 90 days."),
        new DocumentPageText(2, "Commercial terms. Annual spend: $120,000.50."),
        new DocumentPageText(3, "Order form line items."),
        new DocumentPageText(4, "Termination and liability clauses."),
    ];

    // ------------------------------------------------------------------------------------------
    // Harness
    // ------------------------------------------------------------------------------------------

    /// <summary>Clock that moves forward on every read, so rows written in one run are strictly
    /// older than rows written in the next (the pipeline orders "latest" by timestamp).</summary>
    private sealed class AdvancingClock : IClock
    {
        private long _ticks;

        public DateTimeOffset UtcNow => Start.AddSeconds(Interlocked.Increment(ref _ticks));
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Written { get; } = [];

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Written.Add(entry);
            return Task.CompletedTask;
        }
    }

    /// <summary>Scripted `extract` gateway: one payload per stage, per-stage failures that can be
    /// switched on and off between runs, and a per-stage call counter.</summary>
    private sealed class ScriptedGateway(Dictionary<string, string> payloads) : IAiGateway
    {
        public Dictionary<string, string> Payloads { get; } = payloads;

        /// <summary>Stage name to the failure to return for it (an error string), or an exception.</summary>
        public ConcurrentDictionary<string, string> Failures { get; } = new();

        public ConcurrentDictionary<string, Exception> Throws { get; } = new();

        public ConcurrentDictionary<string, int> Calls { get; } = new();

        public int TotalCalls => Calls.Values.Sum();

        public Task<Result<AiExtractionResult>> ExtractAsync(
            AiExtractionRequest request, CancellationToken cancellationToken = default)
        {
            Calls.AddOrUpdate(request.StageName, 1, (_, count) => count + 1);

            if (Throws.TryGetValue(request.StageName, out var exception))
            {
                return Task.FromException<Result<AiExtractionResult>>(exception);
            }

            if (Failures.TryGetValue(request.StageName, out var error))
            {
                return Task.FromResult(Result<AiExtractionResult>.Failure(error));
            }

            var payload = Payloads.TryGetValue(request.StageName, out var json) ? json : "{}";
            var metadata = new AiCallMetadata("test-extract-model", "1", "test-v1", Start, "test-input-hash");
            return Task.FromResult(Result<AiExtractionResult>.Success(new AiExtractionResult(payload, metadata)));
        }

        public Task<Result<AiClassificationResult>> ClassifyAsync(
            AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiEmbeddingResult>> EmbedAsync(
            AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiAnswerResult>> AnswerAsync(
            AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiOcrResult>> OcrAsync(
            AiOcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class Harness
    {
        private readonly string _databaseName = Guid.NewGuid().ToString();

        public Harness(Dictionary<string, string>? payloads = null)
        {
            Gateway = new ScriptedGateway(payloads ?? HighConfidencePayloads());
        }

        public TenantId TenantId { get; } = TenantId.New();

        public TenantContext TenantContext { get; } = new();

        public ScriptedGateway Gateway { get; }

        public AdvancingClock Clock { get; } = new();

        public RecordingAuditWriter Audit { get; } = new();

        public EntityId DocumentId { get; private set; }

        public DocumentsContractsDbContext CreateContext() => InMemoryDocumentsDb.Create(_databaseName);

        public async Task SeedDocumentAsync()
        {
            await using var db = CreateContext();
            var document = new Document
            {
                TenantId = TenantId,
                FileName = "contract.pdf",
                MimeType = "application/pdf",
                StoragePath = $"{TenantId.Value:D}/documents/contract.pdf",
                Checksum = "checksum-1",
                ProcessingStatus = DocumentProcessingStatus.Uploaded,
                CreatedAt = Start,
            };
            db.Documents.Add(document);
            await db.SaveChangesAsync();
            DocumentId = document.Id;
        }

        /// <summary>One <c>RunAsync</c> on a fresh context, exactly as every worker delivery gets one.</summary>
        public async Task<StagedExtractionSummary> RunAsync(IReadOnlyList<DocumentPageText>? pages = null)
        {
            await using var db = CreateContext();
            var service = new StagedExtractionService(db, Gateway, TenantContext, Clock, Audit);
            var result = await service.RunAsync(TenantId, DocumentId, pages ?? Pages, classificationConfidence: null);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            return result.Value;
        }
    }

    private static Dictionary<string, string> HighConfidencePayloads() => new()
    {
        ["Metadata"] = """
            {"facts":[
                {"field":"supplier","value":"Salesforce, Inc.","sourcePage":1,"sourceSpan":"between Salesforce, Inc. and Contoso Ltd","confidence":0.95},
                {"field":"currency","value":"USD","sourcePage":1,"sourceSpan":"Currency: USD","confidence":0.95},
                {"field":"governingLaw","value":"State of Delaware","sourcePage":1,"sourceSpan":"Governing law: Delaware","confidence":0.9}
            ]}
            """,
        ["CommercialTerms"] = """
            {"facts":[
                {"field":"annualSpend","value":"120000.50","sourcePage":2,"sourceSpan":"Annual spend: $120,000.50","confidence":0.92},
                {"field":"totalContractValue","value":"360000","sourcePage":2,"sourceSpan":"TCV: $360,000","confidence":0.9},
                {"field":"paymentTerms","value":"Net 30","sourcePage":2,"sourceSpan":"Payment terms: Net 30","confidence":0.92}
            ]}
            """,
        ["DatesAndRenewalTerms"] = """
            {"facts":[
                {"field":"startDate","value":"2026-01-01","sourcePage":1,"confidence":0.95},
                {"field":"endDate","value":"2027-01-01","sourcePage":1,"sourceSpan":"Term ends 1 January 2027","confidence":0.95},
                {"field":"autoRenewal","value":"true","sourcePage":1,"confidence":0.9},
                {"field":"renewalTermMonths","value":"12","sourcePage":1,"confidence":0.9}
            ]}
            """,
        ["LineItems"] = """
            {"items":[
                {"sku":"SKU-1","description":"Enterprise seats","quantity":100,"unit":"seat","unitPrice":10.5,"sourcePage":3,"sourceSpan":"100 seats @ $10.50","confidence":0.9},
                {"sku":"SKU-2","description":"Premier support","quantity":1,"unit":"year","unitPrice":5000,"sourcePage":3,"sourceSpan":"Premier support","confidence":0.9}
            ]}
            """,
        ["LegalClauses"] = """
            {"items":[
                {"clauseType":"termination","rawText":"Either party may terminate for convenience with 90 days notice.","riskLevel":"Medium","sourcePage":4,"sourceSpan":"Termination clause","confidence":0.92},
                {"clauseType":"liability","rawText":"Liability is not capped.","riskLevel":"High","sourcePage":4,"sourceSpan":"Liability clause","confidence":0.92}
            ]}
            """,
        ["Obligations"] = """
            {"items":[
                {"party":"Customer","obligationType":"payment","description":"Pay invoice within 30 days of receipt","dueDate":"2026-02-01","sourcePage":2,"sourceSpan":"Payment obligation","confidence":0.92}
            ]}
            """,
        ["Risk"] = """
            {"items":[
                {"riskType":"liability","severity":"High","description":"Uncapped liability clause","sourcePage":4,"sourceSpan":"Liability clause","confidence":0.92}
            ]}
            """,
    };

    private static async Task<Counts> CountsAsync(Harness harness)
    {
        await using var db = harness.CreateContext();
        return new Counts(
            await db.ContractLineItems.CountAsync(),
            await db.Clauses.CountAsync(),
            await db.Obligations.CountAsync(),
            await db.Risks.CountAsync());
    }

    private sealed record Counts(int LineItems, int Clauses, int Obligations, int Risks);

    // ------------------------------------------------------------------------------------------
    // F5-T01 -- idempotent facts
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Reprocessing_twice_leaves_exactly_one_copy_of_every_line_clause_obligation_and_risk()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();

        var first = await harness.RunAsync();
        var afterFirst = await CountsAsync(harness);
        Assert.Equal(new Counts(2, 2, 1, 1), afterFirst);

        await harness.RunAsync();
        await harness.RunAsync();

        // 0 duplicates in the four lists after two more runs.
        Assert.Equal(afterFirst, await CountsAsync(harness));

        await using var db = harness.CreateContext();
        var runIds = await db.ExtractionJobs
            .Where(j => j.Stage != ExtractionStage.Classification)
            .Select(j => j.ExtractionRunId)
            .Distinct()
            .ToListAsync();
        Assert.Equal(3, runIds.Count); // every run is its own run, none is resumed

        // The rows that remain are the latest run's, stamped with its id, and tied to the document.
        var latestRun = (await db.ExtractionJobs
            .Where(j => j.Stage == ExtractionStage.Risk)
            .OrderByDescending(j => j.QueuedAt)
            .FirstAsync()).ExtractionRunId;
        Assert.All(await db.ContractLineItems.ToListAsync(), row =>
        {
            Assert.Equal(latestRun, row.ExtractionRunId);
            Assert.Equal(harness.DocumentId, row.SourceDocumentId);
        });
        Assert.All(await db.Clauses.ToListAsync(), row => Assert.Equal(latestRun, row.ExtractionRunId));
        Assert.All(await db.Obligations.ToListAsync(), row => Assert.Equal(latestRun, row.ExtractionRunId));
        Assert.All(await db.Risks.ToListAsync(), row =>
        {
            Assert.Equal(latestRun, row.ExtractionRunId);
            Assert.Equal(harness.DocumentId, row.SourceDocumentId);
        });
        Assert.Equal(first.ContractId, (await db.Documents.SingleAsync()).ContractId);
        Assert.Equal(1, await db.Contracts.CountAsync());
    }

    [Fact]
    public async Task A_reprocess_replaces_the_previous_run_with_the_new_reading_instead_of_adding_to_it()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();
        await harness.RunAsync();

        harness.Gateway.Payloads["Risk"] = """
            {"items":[{"riskType":"renewal","severity":"Medium","description":"Auto-renews without reminder","sourcePage":1,"sourceSpan":"Auto renewal","confidence":0.92}]}
            """;
        await harness.RunAsync();

        await using var db = harness.CreateContext();
        var risk = await db.Risks.SingleAsync();
        Assert.Equal("renewal", risk.RiskType);
    }

    [Fact]
    public async Task Recovery_after_a_hang_resumes_the_open_run_and_leaves_no_duplicates()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();

        // The first delivery dies after the first five stages applied: the last two never answered.
        harness.Gateway.Failures["Obligations"] = UnavailableError;
        harness.Gateway.Failures["Risk"] = UnavailableError;
        await harness.RunAsync();

        // ...and what the dead worker left behind for them is a job still marked Running.
        await using (var db = harness.CreateContext())
        {
            foreach (var job in await db.ExtractionJobs
                .Where(j => j.Stage == ExtractionStage.Obligations || j.Stage == ExtractionStage.Risk)
                .ToListAsync())
            {
                job.Status = ExtractionJobStatus.Running;
                job.CompletedAt = null;
                job.ErrorDetail = null;
                job.FailureKind = null;
            }

            await db.SaveChangesAsync();
        }

        Assert.Equal(new Counts(2, 2, 0, 0), await CountsAsync(harness));

        harness.Gateway.Failures.Clear();
        harness.Gateway.Calls.Clear();

        var recovered = await harness.RunAsync();

        // Only the two stages that never finished were asked again.
        Assert.Equal(2, harness.Gateway.TotalCalls);
        Assert.Equal(1, harness.Gateway.Calls["Obligations"]);
        Assert.Equal(1, harness.Gateway.Calls["Risk"]);

        // Four lists: the five stages' rows were not duplicated, the missing two arrived.
        Assert.Equal(new Counts(2, 2, 1, 1), await CountsAsync(harness));
        Assert.Equal(DocumentProcessingStatus.Completed, recovered.DocumentProcessingStatus);

        // The abandoned Running jobs were closed as interrupted, and the run kept its id.
        await using var read = harness.CreateContext();
        var jobs = await read.ExtractionJobs.Where(j => j.Stage != ExtractionStage.Classification).ToListAsync();
        Assert.DoesNotContain(jobs, j => j.Status == ExtractionJobStatus.Running);
        Assert.Single(jobs.Select(j => j.ExtractionRunId).Distinct());
        Assert.Contains(harness.Audit.Written, e => e.Detail!.Contains("resumedStages="));
    }

    [Fact]
    public async Task Legacy_duplicates_written_before_run_ids_existed_are_cleaned_up_by_the_next_run()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();
        var first = await harness.RunAsync();

        // Two more copies of everything, the way two old reprocesses used to leave them: no run
        // id, and no source document on line items and risks.
        await using (var db = harness.CreateContext())
        {
            foreach (var copy in Enumerable.Range(0, 2))
            {
                db.ContractLineItems.Add(new ContractLineItem
                {
                    TenantId = harness.TenantId, ContractId = first.ContractId, Description = "Enterprise seats",
                    Sku = "SKU-1", CreatedAt = Start,
                });
                db.Risks.Add(new Risk
                {
                    TenantId = harness.TenantId, ContractId = first.ContractId, RiskType = "liability",
                    Severity = RiskSeverity.High, Description = "Uncapped liability clause", IdentifiedAt = Start,
                });
                db.Clauses.Add(new Clause
                {
                    TenantId = harness.TenantId, ContractId = first.ContractId, SourceDocumentId = harness.DocumentId,
                    ClauseType = "termination", RawText = "Old text", CreatedAt = Start,
                });
                db.Obligations.Add(new Obligation
                {
                    TenantId = harness.TenantId, ContractId = first.ContractId, SourceDocumentId = harness.DocumentId,
                    Party = "Customer", ObligationType = "payment", Description = "Old", CreatedAt = Start,
                });
            }

            await db.SaveChangesAsync();
        }

        Assert.True((await CountsAsync(harness)).LineItems > 2);

        await harness.RunAsync();

        Assert.Equal(new Counts(2, 2, 1, 1), await CountsAsync(harness));
    }

    [Fact]
    public async Task Rows_a_human_corrected_survive_a_reprocess_and_are_not_added_again()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();
        await harness.RunAsync();

        EntityId correctedClauseId;
        await using (var db = harness.CreateContext())
        {
            var clause = await db.Clauses.FirstAsync(c => c.ClauseType == "termination");
            correctedClauseId = clause.Id;
            clause.NormalizedValue = "90 days (confirmed by legal)";
            db.CorrectionHistories.Add(new CorrectionHistory
            {
                TenantId = harness.TenantId,
                TargetEntityType = nameof(Clause),
                TargetEntityId = clause.Id,
                FieldName = "normalizedValue",
                PreviousValue = null,
                NewValue = clause.NormalizedValue,
                CorrectedBy = "reviewer@example.com",
                CorrectedAt = Start,
            });
            await db.SaveChangesAsync();
        }

        await harness.RunAsync();
        await harness.RunAsync();

        await using var read = harness.CreateContext();
        var clauses = await read.Clauses.ToListAsync();
        Assert.Equal(2, clauses.Count); // the corrected termination clause + the new liability clause
        var kept = clauses.Single(c => c.Id == correctedClauseId);
        Assert.Equal("90 days (confirmed by legal)", kept.NormalizedValue);
        Assert.Single(clauses, c => c.ClauseType == "termination");
    }

    [Fact]
    public async Task A_stage_that_fails_leaves_the_rows_an_earlier_run_stored_untouched()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();
        await harness.RunAsync();
        var before = await CountsAsync(harness);

        harness.Gateway.Failures["LegalClauses"] = "Simulated rejection.";
        harness.Gateway.Payloads["LineItems"] = "not json at all";
        await harness.RunAsync();

        Assert.Equal(before, await CountsAsync(harness));
    }

    // ------------------------------------------------------------------------------------------
    // F5-T01 -- human-owned scalar fields
    // ------------------------------------------------------------------------------------------

    private static async Task CorrectAsync(Harness harness, EntityId contractId, string field, Action<Contract> apply)
    {
        await using var db = harness.CreateContext();
        var contract = await db.Contracts.SingleAsync(c => c.Id == contractId);
        apply(contract);
        db.CorrectionHistories.Add(new CorrectionHistory
        {
            TenantId = harness.TenantId,
            TargetEntityType = nameof(Contract),
            TargetEntityId = contractId,
            FieldName = field,
            CorrectedBy = "reviewer@example.com",
            CorrectedAt = Start,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_field_with_a_correction_history_is_not_overwritten_and_the_new_reading_is_only_proposed()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();
        var first = await harness.RunAsync();

        await CorrectAsync(harness, first.ContractId, "annualSpend", c => c.AnnualSpend = 99000m);

        harness.Gateway.Payloads["CommercialTerms"] = """
            {"facts":[
                {"field":"annualSpend","value":"150000","sourcePage":2,"sourceSpan":"Annual spend: $150,000","confidence":0.97},
                {"field":"totalContractValue","value":"450000","sourcePage":2,"confidence":0.95}
            ]}
            """;
        var second = await harness.RunAsync();

        await using var db = harness.CreateContext();
        var contract = await db.Contracts.SingleAsync();
        Assert.Equal(99000m, contract.AnnualSpend); // the human's value stands
        Assert.Equal(450000m, contract.TotalContractValue); // an untouched field still follows the document

        var latest = (await db.ExtractionEvidences.Where(e => e.FieldName == "annualSpend").ToListAsync())
            .OrderByDescending(e => e.CreatedAt).First();
        Assert.Equal("150000", latest.Value); // the proposal is on the list...
        Assert.Equal(ExtractionConfidencePolicy.ReviewRequired, latest.Decision); // ...to be reviewed, never applied
        Assert.Equal(DocumentProcessingStatus.NeedsReview, second.DocumentProcessingStatus);
    }

    [Fact]
    public async Task A_field_whose_latest_evidence_is_human_accepted_is_not_overwritten()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();
        var first = await harness.RunAsync();

        await using (var db = harness.CreateContext())
        {
            var latest = (await db.ExtractionEvidences.Where(e => e.FieldName == "endDate").ToListAsync())
                .OrderByDescending(e => e.CreatedAt).First();
            latest.Decision = ExtractionConfidencePolicy.HumanAccepted;
            (await db.Contracts.SingleAsync()).EndDate = new DateOnly(2028, 6, 30);
            await db.SaveChangesAsync();
        }

        await harness.RunAsync();
        await harness.RunAsync();

        await using var read = harness.CreateContext();
        var contract = await read.Contracts.SingleAsync(c => c.Id == first.ContractId);
        Assert.Equal(new DateOnly(2028, 6, 30), contract.EndDate);
        Assert.Equal(new DateOnly(2026, 1, 1), contract.StartDate); // not accepted by a human: still extracted
    }

    [Fact]
    public async Task Re_reading_the_value_the_human_already_confirmed_adds_nothing_to_review()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();
        await harness.RunAsync();

        await using (var db = harness.CreateContext())
        {
            foreach (var evidence in await db.ExtractionEvidences.ToListAsync())
            {
                evidence.Decision = ExtractionConfidencePolicy.HumanAccepted;
            }

            (await db.Contracts.SingleAsync()).SupplierId = EntityId.New(); // the supplier is linked
            await db.SaveChangesAsync();
        }

        await using (var db = harness.CreateContext())
        {
            var before = await db.ExtractionEvidences.CountAsync();
            var second = await harness.RunAsync();
            var after = await db.ExtractionEvidences.CountAsync();

            Assert.Equal(before, after); // nothing re-proposed, nothing re-derived
            Assert.Equal(DocumentProcessingStatus.Completed, second.DocumentProcessingStatus);
        }
    }

    [Fact]
    public async Task A_status_a_human_set_is_not_re_derived_from_the_dates()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();
        var first = await harness.RunAsync();

        await CorrectAsync(harness, first.ContractId, "status", c => c.Status = "terminated");
        await harness.RunAsync();

        await using var db = harness.CreateContext();
        Assert.Equal("terminated", (await db.Contracts.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_supplier_a_human_corrected_is_not_handed_back_to_the_supplier_link()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();
        var first = await harness.RunAsync();
        Assert.Equal("Salesforce, Inc.", first.AcceptedSupplierName);

        await CorrectAsync(harness, first.ContractId, "supplier", _ => { });
        var second = await harness.RunAsync();

        Assert.Null(second.AcceptedSupplierName); // the pipeline will not relink over the human's choice
    }

    [Fact]
    public async Task A_supplier_name_a_reviewer_accepted_while_nothing_was_linked_does_not_block_a_later_link()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();
        await harness.RunAsync();

        await using (var db = harness.CreateContext())
        {
            // validate-document stamped the supplier evidence human_accepted; no supplier was ever linked.
            foreach (var evidence in await db.ExtractionEvidences.Where(e => e.FieldName == "supplier").ToListAsync())
            {
                evidence.Decision = ExtractionConfidencePolicy.HumanAccepted;
            }

            await db.SaveChangesAsync();
        }

        var second = await harness.RunAsync();

        Assert.Equal("Salesforce, Inc.", second.AcceptedSupplierName);
    }

    // ------------------------------------------------------------------------------------------
    // F5-T02 -- typed failures, partial documents, checkpoint and resume
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task All_seven_stages_run_on_a_fresh_document()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();

        var summary = await harness.RunAsync();

        Assert.Equal(7, summary.Stages.Count);
        Assert.Equal(7, harness.Gateway.TotalCalls);
        Assert.All(harness.Gateway.Calls, call => Assert.Equal(1, call.Value));
        Assert.Equal(DocumentProcessingStatus.Completed, summary.DocumentProcessingStatus);
    }

    [Fact]
    public async Task A_stage_that_failed_transiently_never_leaves_the_document_completed()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();
        harness.Gateway.Failures["LegalClauses"] = UnavailableError;

        var summary = await harness.RunAsync();

        // Every fact that did arrive is above the bar -- the old rule completed the document anyway.
        Assert.NotEqual(DocumentProcessingStatus.Completed, summary.DocumentProcessingStatus);
        Assert.Equal(DocumentProcessingStatus.NeedsReview, summary.DocumentProcessingStatus);

        var failed = summary.Stages.Single(s => s.Stage == ExtractionStage.LegalClauses);
        Assert.Equal(ExtractionJobStatus.Failed, failed.Status);
        Assert.Equal(ExtractionStageFailureKind.Transient, failed.FailureKind);
        Assert.Contains(harness.Audit.Written, e => e.Detail!.Contains("partialStages=LegalClauses(Transient)"));

        await using var db = harness.CreateContext();
        Assert.Equal(DocumentProcessingStatus.NeedsReview, (await db.Documents.SingleAsync()).ProcessingStatus);
        var job = await db.ExtractionJobs.SingleAsync(j => j.Stage == ExtractionStage.LegalClauses);
        Assert.Equal(ExtractionStageFailureKind.Transient, job.FailureKind);

        // The partial state is visible to the document list...
        var partial = await ExtractionPartialState.LoadAsync(db, harness.TenantId, [harness.DocumentId], default);
        Assert.Equal([ExtractionStage.LegalClauses], partial[harness.DocumentId].Select(p => p.Stage));

        // ...and the "nothing left to review" auto-validation cannot complete it.
        var validator = new NothingToReviewAutoValidator(db, harness.TenantContext, harness.Audit, harness.Clock);
        await validator.AutoValidateInTenantAsync(harness.TenantId);
        await using var after = harness.CreateContext();
        Assert.Equal(DocumentProcessingStatus.NeedsReview, (await after.Documents.SingleAsync()).ProcessingStatus);
    }

    [Fact]
    public async Task A_stage_that_failed_for_good_is_typed_permanent_and_also_partial()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();
        harness.Gateway.Payloads["Obligations"] = "{ not json";

        var summary = await harness.RunAsync();

        Assert.Equal(DocumentProcessingStatus.NeedsReview, summary.DocumentProcessingStatus);
        var failed = summary.Stages.Single(s => s.Stage == ExtractionStage.Obligations);
        Assert.Equal(ExtractionStageFailureKind.Permanent, failed.FailureKind);
    }

    [Fact]
    public async Task A_network_fault_thrown_by_the_gateway_fails_only_its_stage_as_transient()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();
        harness.Gateway.Throws["Risk"] = new HttpRequestException("connection reset");

        var summary = await harness.RunAsync();

        Assert.Equal(DocumentProcessingStatus.NeedsReview, summary.DocumentProcessingStatus);
        Assert.Equal(ExtractionStageFailureKind.Transient, summary.Stages.Single(s => s.Stage == ExtractionStage.Risk).FailureKind);
        Assert.Equal(6, summary.Stages.Count(s => s.Status == ExtractionJobStatus.Completed));
    }

    [Fact]
    public async Task Retrying_a_partial_document_runs_only_the_failed_stage_and_then_completes_it()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();
        harness.Gateway.Failures["LegalClauses"] = UnavailableError;
        harness.Gateway.Failures["Risk"] = UnavailableError;
        await harness.RunAsync();
        Assert.Equal(7, harness.Gateway.TotalCalls);

        harness.Gateway.Failures.Clear();
        harness.Gateway.Calls.Clear();
        var retry = await harness.RunAsync();

        Assert.Equal(2, harness.Gateway.TotalCalls);
        Assert.Equal(1, harness.Gateway.Calls["LegalClauses"]);
        Assert.Equal(1, harness.Gateway.Calls["Risk"]);
        Assert.Equal(DocumentProcessingStatus.Completed, retry.DocumentProcessingStatus);
        Assert.All(retry.Stages, s => Assert.Equal(ExtractionJobStatus.Completed, s.Status));
        Assert.Equal(new Counts(2, 2, 1, 1), await CountsAsync(harness));

        // Reused stages report the counts they had; the supplier they accepted is still handed on.
        Assert.Equal(3, retry.Stages.Single(s => s.Stage == ExtractionStage.Metadata).ExtractedCount);
        Assert.Equal("Salesforce, Inc.", retry.AcceptedSupplierName);

        await using var db = harness.CreateContext();
        var partial = await ExtractionPartialState.LoadAsync(db, harness.TenantId, [harness.DocumentId], default);
        Assert.Empty(partial); // the failed jobs are history now; nothing stands failed
    }

    [Fact]
    public async Task A_run_that_finished_is_never_resumed_a_reprocess_extracts_everything_again()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();
        await harness.RunAsync();
        harness.Gateway.Calls.Clear();

        await harness.RunAsync();

        Assert.Equal(7, harness.Gateway.TotalCalls);
    }

    [Fact]
    public async Task A_different_text_never_reuses_a_checkpoint()
    {
        var harness = new Harness();
        await harness.SeedDocumentAsync();
        harness.Gateway.Failures["Risk"] = UnavailableError;
        await harness.RunAsync();
        harness.Gateway.Failures.Clear();
        harness.Gateway.Calls.Clear();

        await harness.RunAsync([new DocumentPageText(1, "A different parse of the same document.")]);

        Assert.Equal(7, harness.Gateway.TotalCalls);
    }

    // ------------------------------------------------------------------------------------------
    // F5-D08 -- notice period and the derived deadline
    // ------------------------------------------------------------------------------------------

    private static Dictionary<string, string> PayloadsWithNotice(string deadlineFromModel = "2026-10-01")
    {
        var payloads = HighConfidencePayloads();
        payloads["DatesAndRenewalTerms"] = $$"""
            {"facts":[
                {"field":"startDate","value":"2026-01-01","sourcePage":1,"confidence":0.95},
                {"field":"endDate","value":"2027-01-01","sourcePage":1,"sourceSpan":"Term ends 1 January 2027","confidence":0.95},
                {"field":"noticePeriodDays","value":"90","sourcePage":4,"sourceSpan":"90 days notice before the end of the term","confidence":0.93},
                {"field":"cancellationDeadline","value":"{{deadlineFromModel}}","sourcePage":4,"confidence":0.95},
                {"field":"autoRenewal","value":"true","sourcePage":1,"confidence":0.9}
            ]}
            """;
        return payloads;
    }

    [Fact]
    public async Task The_cancellation_deadline_is_end_date_minus_notice_days_not_the_models_own_arithmetic()
    {
        var harness = new Harness(PayloadsWithNotice(deadlineFromModel: "2026-10-01"));
        await harness.SeedDocumentAsync();

        var summary = await harness.RunAsync();

        await using var db = harness.CreateContext();
        var contract = await db.Contracts.SingleAsync();
        Assert.Equal(90, contract.NoticePeriodDays);
        Assert.Equal(new DateOnly(2027, 1, 1).AddDays(-90), contract.CancellationDeadline); // 2026-10-03, not the model's 2026-10-01

        var derived = (await db.ExtractionEvidences.Where(e => e.FieldName == "cancellationDeadline").ToListAsync())
            .OrderByDescending(e => e.CreatedAt).First();
        Assert.Equal("2026-10-03", derived.Value);
        Assert.Equal(4, derived.SourcePage); // page and span of the notice clause
        Assert.Equal("90 days notice before the end of the term", derived.SourceSpan);
        Assert.Equal(0.93, derived.Confidence); // the weaker of the two inputs
        Assert.Equal(ExtractionConfidencePolicy.AutoAccepted, derived.Decision);
        Assert.Equal(DocumentProcessingStatus.Completed, summary.DocumentProcessingStatus);
    }

    [Fact]
    public async Task A_shaky_notice_period_makes_the_derived_deadline_a_reviewable_fact()
    {
        var payloads = PayloadsWithNotice();
        payloads["DatesAndRenewalTerms"] = payloads["DatesAndRenewalTerms"].Replace("\"confidence\":0.93", "\"confidence\":0.55");
        var harness = new Harness(payloads);
        await harness.SeedDocumentAsync();

        var summary = await harness.RunAsync();

        Assert.Equal(DocumentProcessingStatus.NeedsReview, summary.DocumentProcessingStatus);
        await using var db = harness.CreateContext();
        var derived = (await db.ExtractionEvidences.Where(e => e.FieldName == "cancellationDeadline").ToListAsync())
            .OrderByDescending(e => e.CreatedAt).First();
        Assert.Equal(ExtractionConfidencePolicy.ReviewRequired, derived.Decision);
    }

    [Fact]
    public async Task Without_a_notice_period_the_models_deadline_is_left_as_it_is()
    {
        var payloads = PayloadsWithNotice("2026-10-01");
        payloads["DatesAndRenewalTerms"] = payloads["DatesAndRenewalTerms"]
            .Replace("{\"field\":\"noticePeriodDays\",\"value\":\"90\",\"sourcePage\":4,\"sourceSpan\":\"90 days notice before the end of the term\",\"confidence\":0.93},", string.Empty);
        var harness = new Harness(payloads);
        await harness.SeedDocumentAsync();

        await harness.RunAsync();

        await using var db = harness.CreateContext();
        var contract = await db.Contracts.SingleAsync();
        Assert.Null(contract.NoticePeriodDays);
        Assert.Equal(new DateOnly(2026, 10, 1), contract.CancellationDeadline);
    }

    [Fact]
    public async Task A_deadline_a_human_set_is_not_re_derived()
    {
        var harness = new Harness(PayloadsWithNotice());
        await harness.SeedDocumentAsync();
        var first = await harness.RunAsync();

        await CorrectAsync(harness, first.ContractId, "cancellationDeadline", c => c.CancellationDeadline = new DateOnly(2026, 11, 15));
        await harness.RunAsync();

        await using var db = harness.CreateContext();
        Assert.Equal(new DateOnly(2026, 11, 15), (await db.Contracts.SingleAsync()).CancellationDeadline);
    }

    [Fact]
    public void The_notice_period_can_be_corrected_by_a_human_like_every_other_extracted_field()
    {
        Assert.Contains("noticePeriodDays", ContractCorrectionService.CorrectableFieldNames);
    }

    // ------------------------------------------------------------------------------------------
    // F5-D01 -- the model backs the upload dedupe
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void The_document_table_has_a_unique_partial_index_on_tenant_and_checksum()
    {
        using var db = new Harness().CreateContext();
        var entity = db.Model.FindEntityType(typeof(Document))!;

        var index = Assert.Single(
            entity.GetIndexes(),
            i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual([nameof(Document.TenantId), nameof(Document.Checksum)]));
        Assert.Contains("Rejected", index.GetFilter());
    }
}
