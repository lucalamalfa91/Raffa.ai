using Contigo.AiGateway;
using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Contracts;
using Contigo.AiGateway.Fixtures;
using Contigo.AiGateway.Foundry;
using Contigo.Documents.Contracts.Application.Extraction;
using Contigo.Documents.Contracts.Domain;
using Contigo.Documents.Contracts.Infrastructure;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Contigo.Documents.Contracts.Tests;

/// <summary>
/// Proves the Definition of Done for task E02/F01/US02/T01 (us-02-staged-extraction): AC-1 (all
/// seven stages run, in order, each its own <see cref="ExtractionJob"/>), AC-2 (every persisted
/// fact carries source span + confidence — both on the "one row = one fact" entities and via
/// <see cref="ExtractionEvidence"/> for <see cref="Contract"/>'s own scalar fields), and that a
/// stage which cannot be trusted (low confidence, unparseable payload, a failed gateway call)
/// is recorded as such rather than silently treated as a clean success (product principle:
/// "Human-in-the-loop for consequential decisions... low-confidence extraction... must be
/// reviewable").
///
/// Runs against a real Postgres+pgvector Testcontainer (matching
/// <see cref="ContractLineItemSchemaTests"/>'s pattern) rather than an in-memory provider —
/// <see cref="StagedExtractionService"/> persists through <see cref="DocumentsContractsDbContext"/>
/// exactly like every other application service in this module, so a fake provider would not
/// prove the EF Core mappings (snake_case columns, FK ordering across a single
/// <c>SaveChangesAsync</c> call, <c>vector</c> extension) actually work.
/// </summary>
public sealed class StagedExtractionServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var db = CreateContext(new TenantContext());
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private DocumentsContractsDbContext CreateContext(ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString(), tenantContext);
        return new DocumentsContractsDbContext(optionsBuilder.Options);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
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

    /// <summary>Test-only <see cref="IAiGateway"/> that returns a scripted `extract` payload per
    /// stage (keyed by <see cref="AiExtractionRequest.StageName"/>, i.e. <see cref="ExtractionStage"/>'s
    /// own <c>ToString()</c>) — everything else this pipeline does not call.
    /// <see cref="Contigo.AiGateway.Fixtures.FixtureAiGateway"/> already proves the real,
    /// currently-registered gateway's own contract (always returns "{}" — see
    /// <see cref="Fixture_ai_gateway_empty_payload_is_handled_without_throwing"/> below for that
    /// case specifically); this fake exists to prove the pipeline's own parsing/persistence logic
    /// against payload shapes a live structured-output model would actually return.</summary>
    private sealed class ScriptedAiGateway(
        IReadOnlyDictionary<string, string> payloadByStage,
        IReadOnlySet<string>? failStages = null) : IAiGateway
    {
        public Task<Result<AiClassificationResult>> ClassifyAsync(
            AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("StagedExtractionService does not call ClassifyAsync.");

        /// <summary>Every JSON schema the pipeline sent, in call order — so a test can prove they
        /// are what a strict-mode structured-output call would accept.</summary>
        public List<string> SentSchemas { get; } = [];

        public Task<Result<AiExtractionResult>> ExtractAsync(
            AiExtractionRequest request, CancellationToken cancellationToken = default)
        {
            SentSchemas.Add(request.JsonSchema);

            if (failStages?.Contains(request.StageName) == true)
            {
                return Task.FromResult(Result<AiExtractionResult>.Failure(
                    $"Simulated gateway failure for stage {request.StageName}."));
            }

            var payloadJson = payloadByStage.TryGetValue(request.StageName, out var payload) ? payload : "{}";
            var metadata = new AiCallMetadata("test-extract-model", "1", "test-v1", Now, "test-input-hash");

            return Task.FromResult(Result<AiExtractionResult>.Success(new AiExtractionResult(payloadJson, metadata)));
        }

        public Task<Result<AiEmbeddingResult>> EmbedAsync(
            AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("StagedExtractionService does not call EmbedAsync.");

        public Task<Result<AiAnswerResult>> AnswerAsync(
            AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("StagedExtractionService does not call AnswerAsync.");

        public Task<Result<AiOcrResult>> OcrAsync(
            AiOcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("StagedExtractionService does not call OcrAsync.");
    }

    /// <summary>The supplier legal name every payload below reports for the `supplier` critical
    /// fact (task E13/F03/US01/T02, requirements R-SUP-01) — "as written in the document", legal
    /// suffix and all; normalizing it for matching is <c>Contigo.Suppliers.Products</c>'s job, not
    /// this pipeline's.</summary>
    private const string SupplierLegalName = "Salesforce, Inc.";

    /// <summary>High-confidence payload for every AC-1 stage, used by the happy-path test.
    /// Field/item shapes mirror <see cref="StagedExtractionJsonSchemas"/> exactly.</summary>
    private static Dictionary<string, string> HighConfidencePayloads() => new()
    {
        ["Metadata"] = $$"""
            {"facts":[
                {"field":"supplier","value":"{{SupplierLegalName}}","sourcePage":1,"sourceSpan":"between Salesforce, Inc. and Contoso Ltd","confidence":0.95},
                {"field":"currency","value":"USD","sourcePage":1,"sourceSpan":"Currency: USD","confidence":0.95},
                {"field":"governingLaw","value":"State of Delaware","sourcePage":1,"sourceSpan":"Governing law: Delaware","confidence":0.9},
                {"field":"status","value":"Active","sourcePage":1,"sourceSpan":"Status: Active","confidence":0.9}
            ]}
            """,
        ["CommercialTerms"] = """
            {"facts":[
                {"field":"annualSpend","value":"120000.50","sourcePage":2,"sourceSpan":"Annual spend: $120,000.50","confidence":0.92},
                {"field":"totalContractValue","value":"360000","sourcePage":2,"sourceSpan":"TCV: $360,000","confidence":0.9},
                {"field":"paymentTerms","value":"Net 30","sourcePage":2,"sourceSpan":"Payment terms: Net 30","confidence":0.88}
            ]}
            """,
        ["DatesAndRenewalTerms"] = """
            {"facts":[
                {"field":"startDate","value":"2026-01-01","sourcePage":1,"confidence":0.95},
                {"field":"endDate","value":"2027-01-01","sourcePage":1,"confidence":0.95},
                {"field":"autoRenewal","value":"true","sourcePage":1,"confidence":0.9},
                {"field":"renewalTermMonths","value":"12","sourcePage":1,"confidence":0.9}
            ]}
            """,
        ["LineItems"] = """
            {"items":[
                {"sku":"SKU-1","description":"Enterprise seats","quantity":100,"unit":"seat","unitPrice":10.5,"sourcePage":3,"sourceSpan":"100 seats @ $10.50","confidence":0.9}
            ]}
            """,
        ["LegalClauses"] = """
            {"items":[
                {"clauseType":"termination","rawText":"Either party may terminate for convenience with 90 days notice.","riskLevel":"Medium","sourcePage":4,"sourceSpan":"Termination clause","confidence":0.85}
            ]}
            """,
        ["Obligations"] = """
            {"items":[
                {"party":"Customer","obligationType":"payment","description":"Pay invoice within 30 days of receipt","dueDate":"2026-02-01","sourcePage":2,"sourceSpan":"Payment obligation","confidence":0.8}
            ]}
            """,
        ["Risk"] = """
            {"items":[
                {"riskType":"liability","severity":"High","description":"Uncapped liability clause","sourcePage":4,"sourceSpan":"Liability clause","confidence":0.75}
            ]}
            """,
    };

    private async Task<(TenantId TenantId, Document Document)> SeedDocumentAsync(DocumentsContractsDbContext db, TenantId tenantId)
    {
        var document = new Document
        {
            TenantId = tenantId,
            FileName = "contract.pdf",
            MimeType = "application/pdf",
            StoragePath = $"{tenantId.Value:D}/documents/contract.pdf",
            Checksum = "test-checksum",
            ProcessingStatus = DocumentProcessingStatus.Uploaded,
            CreatedAt = Now,
        };

        db.Documents.Add(document);
        await db.SaveChangesAsync();

        return (tenantId, document);
    }

    [Fact]
    public async Task Staged_pipeline_runs_all_seven_stages_and_persists_evidenced_facts()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        await using var seedDb = CreateContext(tenantContext);
        var (_, document) = await SeedDocumentAsync(seedDb, tenantId);

        var gateway = new ScriptedAiGateway(HighConfidencePayloads());
        var auditWriter = new RecordingAuditWriter();

        await using var runDb = CreateContext(tenantContext);
        var service = new StagedExtractionService(runDb, gateway, tenantContext, new FixedClock(Now), auditWriter);

        IReadOnlyList<DocumentPageText> pages =
        [
            new DocumentPageText(1, "MSA header. Currency: USD. Governing law: Delaware."),
            new DocumentPageText(2, "Commercial terms. Annual spend: $120,000.50."),
            new DocumentPageText(3, "Order form line items."),
            new DocumentPageText(4, "Termination and liability clauses."),
        ];

        var result = await service.RunAsync(tenantId, document.Id, pages);

        Assert.True(result.IsSuccess);
        var summary = result.Value;

        // AC-1: every stage ran, in AC-1's own order, each Completed (every scripted fact above
        // is at/above the pipeline's low-confidence threshold).
        Assert.Equal(
            [
                ExtractionStage.Metadata, ExtractionStage.CommercialTerms, ExtractionStage.DatesAndRenewalTerms,
                ExtractionStage.LineItems, ExtractionStage.LegalClauses, ExtractionStage.Obligations, ExtractionStage.Risk,
            ],
            summary.Stages.Select(s => s.Stage));
        Assert.All(summary.Stages, s => Assert.Equal(ExtractionJobStatus.Completed, s.Status));
        Assert.Equal(DocumentProcessingStatus.Completed, summary.DocumentProcessingStatus);

        await using var readDb = CreateContext(tenantContext);
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var storedDocument = await readDb.Documents.SingleAsync(d => d.Id == document.Id);
        Assert.Equal(summary.ContractId, storedDocument.ContractId);
        Assert.Equal(DocumentProcessingStatus.Completed, storedDocument.ProcessingStatus);

        // Metadata + CommercialTerms + DatesAndRenewalTerms stages: applied onto Contract itself.
        var contract = await readDb.Contracts.SingleAsync(c => c.Id == summary.ContractId);
        Assert.Equal("USD", contract.Currency);
        Assert.Equal("State of Delaware", contract.GoverningLaw);
        Assert.Equal("Active", contract.Status);
        Assert.Equal(120000.50m, contract.AnnualSpend);
        Assert.Equal(360000m, contract.TotalContractValue);
        Assert.Equal("Net 30", contract.PaymentTerms);
        Assert.Equal(new DateOnly(2026, 1, 1), contract.StartDate);
        Assert.Equal(new DateOnly(2027, 1, 1), contract.EndDate);
        Assert.True(contract.AutoRenewal);
        Assert.Equal(12, contract.RenewalTermMonths);

        // AC-2: every Contract-level scalar fact has its own evidence row.
        var evidence = await readDb.ExtractionEvidences
            .Where(e => e.ContractId == summary.ContractId)
            .ToListAsync();
        Assert.Equal(11, evidence.Count); // 4 metadata (incl. supplier) + 3 commercial + 4 dates facts

        // Task E13/F03/US01/T02 (R-SUP-01): the `supplier` critical fact rides the same evidence
        // path as every other metadata fact — and, cleared for use at 0.95, is reported on the
        // summary for DocumentProcessingPipeline to resolve into Contract.SupplierId (this service
        // deliberately writes no SupplierId itself — ADR-002).
        var supplierEvidence = Assert.Single(evidence, e => e.FieldName == "supplier");
        Assert.Equal(SupplierLegalName, supplierEvidence.Value);
        Assert.Equal(1, supplierEvidence.SourcePage);
        Assert.Equal(0.95, supplierEvidence.Confidence);
        Assert.Equal(SupplierLegalName, summary.AcceptedSupplierName);
        Assert.Null(contract.SupplierId);

        var currencyEvidence = Assert.Single(evidence, e => e.FieldName == "currency");
        Assert.Equal("USD", currencyEvidence.Value);
        Assert.Equal(1, currencyEvidence.SourcePage);
        Assert.Equal("Currency: USD", currencyEvidence.SourceSpan);
        Assert.Equal(0.95, currencyEvidence.Confidence);
        Assert.Equal(document.Id, currencyEvidence.SourceDocumentId);

        // AC-2: LineItems/Clauses/Obligations/Risk each carry their own evidence directly.
        var lineItem = await readDb.ContractLineItems.SingleAsync(li => li.ContractId == summary.ContractId);
        Assert.Equal("SKU-1", lineItem.Sku);
        Assert.Equal(100m, lineItem.Quantity);
        Assert.Equal(3, lineItem.SourcePage);
        Assert.Equal(0.9, lineItem.Confidence);

        var clause = await readDb.Clauses.SingleAsync(c => c.ContractId == summary.ContractId);
        Assert.Equal("termination", clause.ClauseType);
        Assert.Equal(RiskSeverity.Medium, clause.RiskLevel);
        Assert.Equal(4, clause.SourcePage);
        Assert.Equal(0.85, clause.Confidence);

        var obligation = await readDb.Obligations.SingleAsync(o => o.ContractId == summary.ContractId);
        Assert.Equal("Customer", obligation.Party);
        Assert.Equal(new DateOnly(2026, 2, 1), obligation.DueDate);
        Assert.Equal(2, obligation.SourcePage);
        Assert.Equal(0.8, obligation.Confidence);

        var risk = await readDb.Risks.SingleAsync(r => r.ContractId == summary.ContractId);
        Assert.Equal("liability", risk.RiskType);
        Assert.Equal(RiskSeverity.High, risk.Severity);
        Assert.Equal(4, risk.SourcePage);
        Assert.Equal(0.75, risk.Confidence);

        var auditEntry = Assert.Single(auditWriter.Written);
        Assert.Equal("document.extraction.completed", auditEntry.Action);
        Assert.Equal(tenantId, auditEntry.TenantId);
    }

    [Fact]
    public async Task A_low_confidence_fact_marks_its_stage_and_the_document_as_needing_review()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        await using var seedDb = CreateContext(tenantContext);
        var (_, document) = await SeedDocumentAsync(seedDb, tenantId);

        var payloads = HighConfidencePayloads();
        payloads["Metadata"] = """
            {"facts":[{"field":"currency","value":"USD","sourcePage":1,"confidence":0.2}]}
            """;

        var gateway = new ScriptedAiGateway(payloads);

        await using var runDb = CreateContext(tenantContext);
        var service = new StagedExtractionService(
            runDb, gateway, tenantContext, new FixedClock(Now), new RecordingAuditWriter());

        var pages = new[] { new DocumentPageText(1, "some contract text") };

        var result = await service.RunAsync(tenantId, document.Id, pages);

        Assert.True(result.IsSuccess);
        var metadataStage = result.Value.Stages.Single(s => s.Stage == ExtractionStage.Metadata);
        Assert.Equal(ExtractionJobStatus.NeedsReview, metadataStage.Status);
        Assert.Equal(1, metadataStage.ExtractedCount);
        Assert.Equal(DocumentProcessingStatus.NeedsReview, result.Value.DocumentProcessingStatus);
    }

    /// <summary>
    /// Task E13/F03/US01/T02 (requirements R-SUP-01, product spec §7.3): <c>supplier</c> is a
    /// <b>critical</b> field, judged at 0.8 rather than the ordinary 0.6. 0.64 is deliberately
    /// chosen to sit between the two bars — under the old, single-threshold behaviour this fact
    /// would have been trusted outright and the stage reported Completed. The evidence row is
    /// written either way, so the field reaches the review list with its page/span/confidence; only
    /// <see cref="StagedExtractionSummary.AcceptedSupplierName"/> tells the pipeline not to link it.
    /// </summary>
    [Fact]
    public async Task A_supplier_fact_below_the_critical_threshold_needs_review_but_keeps_its_evidence()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        await using var seedDb = CreateContext(tenantContext);
        var (_, document) = await SeedDocumentAsync(seedDb, tenantId);

        var payloads = HighConfidencePayloads();
        payloads["Metadata"] = $$"""
            {"facts":[
                {"field":"supplier","value":"{{SupplierLegalName}}","sourcePage":1,"sourceSpan":"between Salesforce, Inc. and Contoso Ltd","confidence":0.64},
                {"field":"currency","value":"USD","sourcePage":1,"confidence":0.95}
            ]}
            """;

        await using var runDb = CreateContext(tenantContext);
        var service = new StagedExtractionService(
            runDb, new ScriptedAiGateway(payloads), tenantContext, new FixedClock(Now), new RecordingAuditWriter());

        var result = await service.RunAsync(tenantId, document.Id, [new DocumentPageText(1, "some contract text")]);

        Assert.True(result.IsSuccess);
        var summary = result.Value;

        var metadataStage = summary.Stages.Single(s => s.Stage == ExtractionStage.Metadata);
        Assert.Equal(ExtractionJobStatus.NeedsReview, metadataStage.Status);
        Assert.Equal(2, metadataStage.ExtractedCount);
        Assert.Equal(DocumentProcessingStatus.NeedsReview, summary.DocumentProcessingStatus);

        // Not accepted: nothing downstream may link a supplier off this fact.
        Assert.Null(summary.AcceptedSupplierName);

        await using var readDb = CreateContext(tenantContext);
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var supplierEvidence = await readDb.ExtractionEvidences
            .SingleAsync(e => e.ContractId == summary.ContractId && e.FieldName == "supplier");
        Assert.Equal(SupplierLegalName, supplierEvidence.Value);
        Assert.Equal("between Salesforce, Inc. and Contoso Ltd", supplierEvidence.SourceSpan);
        Assert.Equal(0.64, supplierEvidence.Confidence);

        // The sibling `currency` fact sits at the same 0.64-vs-0.8 relationship the other way
        // round: an ordinary field at 0.64 would have been fine, which is exactly why the two
        // thresholds cannot be one number.
        Assert.Equal("USD", (await readDb.Contracts.SingleAsync(c => c.Id == summary.ContractId)).Currency);
    }

    /// <summary>Same shape as the test above, one notch higher: an ordinary field is unaffected by
    /// the critical bar. 0.64 on <c>currency</c> alone keeps the stage Completed — proof that
    /// <c>CriticalFields</c> narrows the stricter threshold to the fields §7.3 names, rather than
    /// raising it for everything.</summary>
    [Fact]
    public async Task A_non_critical_fact_between_the_two_thresholds_is_still_trusted()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        await using var seedDb = CreateContext(tenantContext);
        var (_, document) = await SeedDocumentAsync(seedDb, tenantId);

        var payloads = HighConfidencePayloads();
        payloads["Metadata"] = """
            {"facts":[{"field":"currency","value":"USD","sourcePage":1,"confidence":0.64}]}
            """;

        await using var runDb = CreateContext(tenantContext);
        var service = new StagedExtractionService(
            runDb, new ScriptedAiGateway(payloads), tenantContext, new FixedClock(Now), new RecordingAuditWriter());

        var result = await service.RunAsync(tenantId, document.Id, [new DocumentPageText(1, "some contract text")]);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ExtractionJobStatus.Completed,
            result.Value.Stages.Single(s => s.Stage == ExtractionStage.Metadata).Status);
        Assert.Null(result.Value.AcceptedSupplierName);
    }

    /// <summary>ADR-004/ADR-017 amendments (2026-09-09): the live `extract` role sends each stage's
    /// schema as an Azure structured-output schema in strict mode, which rejects open objects,
    /// partial <c>required</c> lists and numeric bounds. The pipeline's own schemas are proven here
    /// through the same validator the Foundry client applies before sending.</summary>
    [Fact]
    public async Task Every_stage_schema_the_pipeline_sends_is_strict_mode_compliant()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        await using var seedDb = CreateContext(tenantContext);
        var (_, document) = await SeedDocumentAsync(seedDb, tenantId);

        var gateway = new ScriptedAiGateway(HighConfidencePayloads());

        await using var runDb = CreateContext(tenantContext);
        var service = new StagedExtractionService(
            runDb, gateway, tenantContext, new FixedClock(Now), new RecordingAuditWriter());

        var result = await service.RunAsync(tenantId, document.Id, [new DocumentPageText(1, "some contract text")]);

        Assert.True(result.IsSuccess);
        Assert.Equal(result.Value.Stages.Count, gateway.SentSchemas.Count);
        Assert.All(gateway.SentSchemas, schema =>
        {
            var verdict = StrictJsonSchemaValidator.Validate(schema);
            Assert.True(verdict.IsSuccess, verdict.IsFailure ? verdict.Error : null);
        });
    }

    /// <summary>The live extract prompt asks for <c>null</c> when the document does not state a
    /// field (every property is required under strict mode, so "absent" has to be spelled out). A
    /// null-valued fact is neither an extraction nor evidence: nothing is written for it and no
    /// contract column is overwritten with an empty value.</summary>
    [Fact]
    public async Task A_null_valued_fact_is_absent_not_an_empty_extraction()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        await using var seedDb = CreateContext(tenantContext);
        var (_, document) = await SeedDocumentAsync(seedDb, tenantId);

        var payloads = HighConfidencePayloads();
        payloads["Metadata"] = $$"""
            {"facts":[
                {"field":"supplier","value":"{{SupplierLegalName}}","sourcePage":1,"sourceSpan":"between Salesforce, Inc. and Contoso Ltd","confidence":0.95},
                {"field":"currency","value":null,"sourcePage":1,"sourceSpan":null,"confidence":0}
            ]}
            """;

        await using var runDb = CreateContext(tenantContext);
        var service = new StagedExtractionService(
            runDb, new ScriptedAiGateway(payloads), tenantContext, new FixedClock(Now), new RecordingAuditWriter());

        var result = await service.RunAsync(tenantId, document.Id, [new DocumentPageText(1, "some contract text")]);

        Assert.True(result.IsSuccess);
        var metadataStage = result.Value.Stages.Single(s => s.Stage == ExtractionStage.Metadata);
        Assert.Equal(ExtractionJobStatus.Completed, metadataStage.Status);
        Assert.Equal(1, metadataStage.ExtractedCount);
        Assert.Equal(SupplierLegalName, result.Value.AcceptedSupplierName);

        await using var readDb = CreateContext(tenantContext);
        using var tenantScope = tenantContext.BeginScope(tenantId);
        var evidenceFields = await readDb.ExtractionEvidences
            .Where(e => e.ContractId == result.Value.ContractId)
            .Select(e => e.FieldName)
            .ToListAsync();
        Assert.Contains("supplier", evidenceFields);
        Assert.DoesNotContain("currency", evidenceFields);
    }

    [Fact]
    public async Task A_gateway_failure_on_one_stage_is_recorded_and_does_not_abort_the_others()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        await using var seedDb = CreateContext(tenantContext);
        var (_, document) = await SeedDocumentAsync(seedDb, tenantId);

        var gateway = new ScriptedAiGateway(HighConfidencePayloads(), failStages: new HashSet<string> { "LegalClauses" });

        await using var runDb = CreateContext(tenantContext);
        var service = new StagedExtractionService(
            runDb, gateway, tenantContext, new FixedClock(Now), new RecordingAuditWriter());

        var pages = new[] { new DocumentPageText(1, "some contract text") };

        var result = await service.RunAsync(tenantId, document.Id, pages);

        Assert.True(result.IsSuccess);
        var stages = result.Value.Stages.ToDictionary(s => s.Stage);

        Assert.Equal(ExtractionJobStatus.Failed, stages[ExtractionStage.LegalClauses].Status);
        Assert.NotNull(stages[ExtractionStage.LegalClauses].ErrorDetail);

        // The other six stages still ran and completed — one stage's gateway failure did not
        // abort the pipeline (ADR-017's "fail visibly, never silently truncate", per-stage).
        Assert.Equal(ExtractionJobStatus.Completed, stages[ExtractionStage.Metadata].Status);
        Assert.Equal(ExtractionJobStatus.Completed, stages[ExtractionStage.Risk].Status);

        Assert.Equal(DocumentProcessingStatus.NeedsReview, result.Value.DocumentProcessingStatus);
    }

    [Fact]
    public async Task Fixture_ai_gateway_finding_nothing_in_a_text_routes_only_the_scalar_stages_to_review()
    {
        // FixtureAiGateway.ExtractAsync reads facts deterministically from the text
        // (FixtureContractFactExtractor); a text with no contract cues at all yields no fact for any
        // scalar stage and an empty list for every list stage. This is the exact gateway
        // AddAiGatewayModule registers, so the pipeline must run cleanly against it: the three
        // scalar stages ("a contract with no supplier, fee or date is not a trusted extraction") go
        // to review, the four list stages complete — an empty list of line items/clauses is a
        // legitimate answer, not something a reviewer can resolve.
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        await using var seedDb = CreateContext(tenantContext);
        var (_, document) = await SeedDocumentAsync(seedDb, tenantId);

        var gateway = new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now));

        await using var runDb = CreateContext(tenantContext);
        var service = new StagedExtractionService(
            runDb, gateway, tenantContext, new FixedClock(Now), new RecordingAuditWriter());

        var pages = new[] { new DocumentPageText(1, "some contract text") };

        var result = await service.RunAsync(tenantId, document.Id, pages);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value.Stages.Count);
        Assert.All(result.Value.Stages, s => Assert.Equal(0, s.ExtractedCount));
        var stages = result.Value.Stages.ToDictionary(s => s.Stage);
        Assert.Equal(ExtractionJobStatus.NeedsReview, stages[ExtractionStage.Metadata].Status);
        Assert.Equal(ExtractionJobStatus.NeedsReview, stages[ExtractionStage.CommercialTerms].Status);
        Assert.Equal(ExtractionJobStatus.NeedsReview, stages[ExtractionStage.DatesAndRenewalTerms].Status);
        Assert.Equal(ExtractionJobStatus.Completed, stages[ExtractionStage.LineItems].Status);
        Assert.Equal(ExtractionJobStatus.Completed, stages[ExtractionStage.LegalClauses].Status);
        Assert.Equal(ExtractionJobStatus.Completed, stages[ExtractionStage.Obligations].Status);
        Assert.Equal(ExtractionJobStatus.Completed, stages[ExtractionStage.Risk].Status);
        Assert.Equal(DocumentProcessingStatus.NeedsReview, result.Value.DocumentProcessingStatus);
    }

    [Fact]
    public async Task Fixture_ai_gateway_completes_a_clean_sample_contract_and_routes_an_ambiguous_one_to_review()
    {
        // The two sample contracts the web's "Sample MSA" buttons upload, through the real fixture
        // gateway and the real pipeline: the clean one lands in Completed with every scalar fact
        // evidenced, the ambiguous one in NeedsReview with exactly the weak facts its text leaves
        // open (an unlabelled supplier, two annual amounts, a self-contradicting renewal clause).
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        await using var seedDb = CreateContext(tenantContext);
        var (_, cleanDocument) = await SeedDocumentAsync(seedDb, tenantId);
        var (_, ambiguousDocument) = await SeedDocumentAsync(seedDb, tenantId);

        var gateway = new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now));

        await using var runDb = CreateContext(tenantContext);
        var service = new StagedExtractionService(
            runDb, gateway, tenantContext, new FixedClock(Now), new RecordingAuditWriter());

        var clean = await service.RunAsync(
            tenantId,
            cleanDocument.Id,
            [
                new DocumentPageText(1,
                    "MASTER SERVICES AGREEMENT. This Master Services Agreement is entered into between Contigo Demo AG " +
                    "(\"Customer\") and Northwind Traders SA (\"Supplier\"), effective 2026-01-01. The annual subscription fee " +
                    "is EUR 48,000, invoiced yearly in advance. All invoices are payable within thirty (30) days of receipt."),
                new DocumentPageText(2,
                    "The initial term is thirty-six (36) months from the effective date. Thereafter this Agreement renews " +
                    "automatically for successive twelve (12) month terms unless either party gives ninety (90) days written " +
                    "notice before the end of the then-current term. This Agreement is governed by the laws of Switzerland."),
            ],
            classificationConfidence: 0.99);

        Assert.True(clean.IsSuccess);
        Assert.All(clean.Value.Stages, s => Assert.Equal(ExtractionJobStatus.Completed, s.Status));
        Assert.Equal(DocumentProcessingStatus.Completed, clean.Value.DocumentProcessingStatus);
        Assert.Equal("Northwind Traders SA", clean.Value.AcceptedSupplierName);

        var ambiguous = await service.RunAsync(
            tenantId,
            ambiguousDocument.Id,
            [
                new DocumentPageText(1,
                    "MASTER SERVICES AGREEMENT. This Master Services Agreement is made between Contigo Demo AG and Fabrikam " +
                    "Software GmbH, effective 1 February 2026. The annual subscription fee is EUR 36,000, invoiced quarterly in " +
                    "arrears; Schedule 1, however, lists an annual fee of EUR 39,600 after the agreed uplift. Invoices are " +
                    "payable within forty-five (45) days."),
                new DocumentPageText(2,
                    "The initial term is twenty-four (24) months. This Agreement renews automatically for successive twelve (12) " +
                    "month periods; notwithstanding the foregoing, the Customer may elect in writing that this Agreement shall " +
                    "not automatically renew. Either party may give sixty (60) days written notice before the end of the current " +
                    "term. This Agreement is governed by the laws of Germany."),
            ],
            classificationConfidence: 0.99);

        Assert.True(ambiguous.IsSuccess);
        Assert.Equal(DocumentProcessingStatus.NeedsReview, ambiguous.Value.DocumentProcessingStatus);
        Assert.Null(ambiguous.Value.AcceptedSupplierName); // proposed below the critical bar, never linked

        await using var readDb = CreateContext(tenantContext);
        using var tenantScope = tenantContext.BeginScope(tenantId);
        var weakFields = await readDb.ExtractionEvidences
            .Where(e => e.ContractId == ambiguous.Value.ContractId && (e.Confidence == null || e.Confidence < 0.6))
            .Select(e => e.FieldName)
            .OrderBy(f => f)
            .ToListAsync();
        Assert.Equal(["annualSpend", "autoRenewal", "supplier"], weakFields);

        var cleanContract = await readDb.Contracts.SingleAsync(c => c.Id == clean.Value.ContractId);
        Assert.Equal("EUR", cleanContract.Currency);
        Assert.Equal(48000m, cleanContract.AnnualSpend);
        Assert.Equal(new DateOnly(2026, 1, 1), cleanContract.EffectiveDate);
        Assert.Equal(new DateOnly(2028, 12, 31), cleanContract.EndDate);
        Assert.True(cleanContract.AutoRenewal);
        Assert.Equal(12, cleanContract.RenewalTermMonths);
        Assert.Equal("Switzerland", cleanContract.GoverningLaw);
        Assert.Equal("Net 30", cleanContract.PaymentTerms);
        Assert.Equal("active", cleanContract.Status);
    }

    [Fact]
    public async Task The_classification_verdict_is_recorded_as_type_evidence_and_a_weak_one_needs_review()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        await using var seedDb = CreateContext(tenantContext);
        var (_, document) = await SeedDocumentAsync(seedDb, tenantId);
        document.DocumentType = ContractDocumentType.Sow;
        var classificationJob = new ExtractionJob
        {
            TenantId = tenantId,
            DocumentId = document.Id,
            Stage = ExtractionStage.Classification,
            Status = ExtractionJobStatus.Completed,
            QueuedAt = Now,
            ModelId = "test-classify-model",
        };
        seedDb.ExtractionJobs.Add(classificationJob);
        await seedDb.SaveChangesAsync();

        await using var runDb = CreateContext(tenantContext);
        var service = new StagedExtractionService(
            runDb, new ScriptedAiGateway(HighConfidencePayloads()), tenantContext, new FixedClock(Now), new RecordingAuditWriter());

        // Every staged fact is high-confidence, so only the weak classification can send this
        // document to review.
        var result = await service.RunAsync(
            tenantId, document.Id, [new DocumentPageText(1, "text")], classificationConfidence: 0.5);

        Assert.True(result.IsSuccess);
        Assert.All(result.Value.Stages, s => Assert.Equal(ExtractionJobStatus.Completed, s.Status));
        Assert.Equal(DocumentProcessingStatus.NeedsReview, result.Value.DocumentProcessingStatus);

        await using var readDb = CreateContext(tenantContext);
        using var tenantScope = tenantContext.BeginScope(tenantId);
        var typeEvidence = Assert.Single(
            await readDb.ExtractionEvidences.Where(e => e.ContractId == result.Value.ContractId && e.FieldName == "type").ToListAsync());
        Assert.Equal("Sow", typeEvidence.Value);
        Assert.Equal(0.5, typeEvidence.Confidence);
        Assert.Equal(document.Id, typeEvidence.SourceDocumentId);
        Assert.Equal(classificationJob.Id, typeEvidence.ExtractionJobId);
        Assert.Null(typeEvidence.SourcePage);
        Assert.Null(typeEvidence.SourceSpan);

        // 11 staged facts + the type row: the overload without a verdict writes no type row at all.
        Assert.Equal(12, await readDb.ExtractionEvidences.CountAsync(e => e.ContractId == result.Value.ContractId));
    }

    [Fact]
    public async Task Unknown_document_fails_without_running_any_stage()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        await using var db = CreateContext(tenantContext);
        var service = new StagedExtractionService(
            db, new ScriptedAiGateway(HighConfidencePayloads()), tenantContext, new FixedClock(Now), new RecordingAuditWriter());

        var result = await service.RunAsync(tenantId, EntityId.New(), [new DocumentPageText(1, "text")]);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Empty_page_list_fails_fast_instead_of_running_an_empty_pipeline()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        await using var db = CreateContext(tenantContext);
        var service = new StagedExtractionService(
            db, new ScriptedAiGateway(HighConfidencePayloads()), tenantContext, new FixedClock(Now), new RecordingAuditWriter());

        var result = await service.RunAsync(tenantId, EntityId.New(), []);

        Assert.True(result.IsFailure);
    }
}
