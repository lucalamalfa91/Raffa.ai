using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Contigo.AiGateway;
using Contigo.AiGateway.Contracts;
using Contigo.Documents.Contracts.Domain;
using Contigo.Documents.Contracts.Infrastructure;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Contigo.Documents.Contracts.Application.Extraction;

/// <summary>
/// Implements task E02/F01/US02/T01 (us-02-staged-extraction): a staged, schema-constrained
/// extraction pipeline over an already-parsed document.
///
/// <b>AC-1</b> ("staged: metadata -&gt; commercial terms -&gt; dates -&gt; price/SKU -&gt; clauses
/// -&gt; obligations -&gt; risk"): <see cref="RunAsync"/> runs exactly those seven
/// <see cref="ExtractionStage"/> values, in that order, each as its own <see cref="IAiGateway.ExtractAsync"/>
/// call against its own schema (spec §7.2 "avoid one giant prompt; split extraction into
/// bounded, schema-constrained tasks") and its own <see cref="ExtractionJob"/> row — one stage's
/// failure is recorded on that stage's job and does not abort the remaining stages (mirrors
/// ADR-017's "fail visibly, never silently truncate", generalized from OCR page-budget to any
/// stage of this pipeline).
///
/// <b>AC-2</b> ("every extracted fact carries source span + confidence"): every persisted fact
/// carries <c>SourceSpan</c>/<c>SourcePage</c>/<c>Confidence</c> — directly on
/// <see cref="ContractLineItem"/>/<see cref="Clause"/>/<see cref="Obligation"/>/<see cref="Risk"/>
/// rows (one row = one fact), or via a new <see cref="ExtractionEvidence"/> row per field for the
/// scalar-field stages that mutate <see cref="Contract"/> directly (Metadata, CommercialTerms,
/// DatesAndRenewalTerms) — see <see cref="ExtractionEvidence"/>'s own doc comment for why those
/// three stages need a side table and the other four do not.
///
/// <b>AC-3</b> ("hybrid parse: native text... OCR...") is satisfied jointly with task
/// E02/F01/US02/T02 (hybrid-ocr): this service does not read document bytes at all — it takes
/// already-parsed, page-mapped text (<see cref="DocumentPageText"/>) as input, so it does not
/// know or care whether a given page's text came from native parsing or OCR. That parsing step
/// is task T02's own coding objective ("hybrid OCR pre-pass behind gateway"); this task's own
/// architecture decisions in force (ADR-004, ADR-002) do not name ADR-017, unlike T02's.
///
/// Ensures a <see cref="Contract"/> exists for the document before staging into it
/// (<see cref="EnsureContractAsync"/>) — nothing else in this wave consumes the
/// <see cref="ExtractionStage.Classification"/> job <c>DocumentUploadService</c> queues at
/// upload, so without this the pipeline would have nothing to extract into. This is a
/// deliberate, documented scope decision by this task (not silently absorbed): see
/// <see cref="EnsureContractAsync"/>'s own doc comment for why it is not promoted to
/// reports/open-questions.md (same reasoning <c>Contigo.Api/Program.cs</c>'s X-Tenant-Id comment
/// already gives for a mid-wave append risking the phase-barrier merge).
/// </summary>
public sealed class StagedExtractionService(
    DocumentsContractsDbContext dbContext,
    IAiGateway aiGateway,
    ITenantContext tenantContext,
    IClock clock,
    IAuditWriter auditWriter)
{
    /// <summary>AC-1's seven stages, in pipeline order. <see cref="ExtractionStage.Classification"/>
    /// is deliberately excluded — it is queued and (eventually) consumed elsewhere, before this
    /// pipeline ever runs (see the type doc comment).</summary>
    private static readonly ExtractionStage[] PipelineStages =
    [
        ExtractionStage.Metadata,
        ExtractionStage.CommercialTerms,
        ExtractionStage.DatesAndRenewalTerms,
        ExtractionStage.LineItems,
        ExtractionStage.LegalClauses,
        ExtractionStage.Obligations,
        ExtractionStage.Risk,
    ];

    /// <summary>Below this, a fact is treated as needing human review rather than trusted
    /// outright (product principle: "Human-in-the-loop for consequential decisions... low-
    /// confidence extraction... must be reviewable"). The brief does not name an exact number;
    /// this is this task's own documented choice, not a locked decision — a config knob, not a
    /// hard-coded business rule, if a later task needs to tune it per-field.</summary>
    private const double LowConfidenceThreshold = 0.6;

    /// <summary>Product spec §7.3's own, stricter bar for a <b>critical</b> field
    /// (<see cref="CriticalFields"/>): below this the document lands in
    /// <see cref="DocumentProcessingStatus.NeedsReview"/> and the field shows up in the review
    /// list with its <see cref="ExtractionEvidence"/> row, even though the same confidence would
    /// have been trusted outright for an ordinary field. Deliberately a second, higher number
    /// rather than a re-tuned <see cref="LowConfidenceThreshold"/>: a weak `currency` fact is a
    /// nuisance, a weak `supplier` fact mis-attributes an entire contract to the wrong company
    /// (requirements R-SUP-01/R-SUP-03), so the two cannot share one bar.</summary>
    private const double CriticalConfidenceThreshold = 0.8;

    /// <summary>Field name of the `supplier` fact (requirements R-SUP-01): the supplier's legal
    /// name exactly as written in the document. Public because
    /// <see cref="DocumentProcessingPipeline"/> and the review surfaces address the resulting
    /// <see cref="ExtractionEvidence.FieldName"/> by this literal, and
    /// <c>ContractCorrectionService</c> accepts a human correction under the same name.</summary>
    public const string SupplierFieldName = "supplier";

    /// <summary><see cref="Contract"/> fields the `metadata` stage may propose (allow-listed
    /// both here and in the JSON Schema's <c>enum</c> — see <see cref="StagedExtractionJsonSchemas.Facts"/>).
    /// Deliberately excludes <see cref="Contract.Type"/>: that is Classification's field
    /// (<see cref="Document.DocumentType"/>), not this pipeline's.
    /// <see cref="SupplierFieldName"/> is here even though it maps to no <see cref="Contract"/>
    /// scalar — see <see cref="ApplyMetadataFact"/>.</summary>
    private static readonly string[] MetadataFields = [SupplierFieldName, "currency", "governingLaw", "status"];

    /// <summary>The fields product spec §7.3 calls <b>critical</b>: judged against
    /// <see cref="CriticalConfidenceThreshold"/> instead of <see cref="LowConfidenceThreshold"/>.
    /// A set (not a single constant) because §7.3 names a category, not one field — later tasks
    /// promote further fields into it without touching <see cref="ApplyFacts"/>.</summary>
    private static readonly HashSet<string> CriticalFields = new(StringComparer.Ordinal) { SupplierFieldName };

    private static readonly string[] CommercialTermsFields =
        ["annualSpend", "totalContractValue", "paymentTerms"];

    private static readonly string[] DatesFields =
        ["startDate", "endDate", "effectiveDate", "cancellationDeadline", "autoRenewal", "renewalTermMonths"];

    private static readonly JsonSerializerOptions PayloadSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>Recorded actor for the audit entry this pipeline writes (Appendix C rule 9).
    /// Distinct from <c>DocumentUploadService.UnattributedActor</c>: that placeholder means "an
    /// HTTP caller with no validated identity yet" (ADR-010 not wired); this one means "no human
    /// caller at all" — the pipeline runs as automation, honestly labelled as such rather than
    /// borrowing the HTTP-request placeholder for a non-HTTP-triggered action.</summary>
    private const string SystemActor = "system:staged-extraction";

    /// <summary>Bootstrap-only placeholder <see cref="Contract.Status"/> for a contract shell
    /// this pipeline had to create (see <see cref="EnsureContractAsync"/>). Overwritten by the
    /// `metadata` stage's own "status" fact when the model reports one, or by a human correction
    /// — never presented as a real extracted value.</summary>
    private const string BootstrapContractStatus = "processing";

    /// <summary>Bootstrap-only placeholder <see cref="Contract.Currency"/> — see
    /// <see cref="BootstrapContractStatus"/>. <see cref="Contract.Currency"/> is a required
    /// (NOT NULL) column (contract-schema task, ADR-003); this pipeline cannot leave it unset
    /// even before the `metadata` stage has run.</summary>
    private const string BootstrapContractCurrency = "USD";

    public async Task<Result<StagedExtractionSummary>> RunAsync(
        TenantId tenantId,
        EntityId documentId,
        IReadOnlyList<DocumentPageText> pages,
        CancellationToken cancellationToken = default)
    {
        if (pages.Count == 0)
        {
            // ADR-017: "over-budget jobs fail visibly... they are not silently truncated" —
            // generalized here to "no readable text at all" for the same reason: an empty
            // pipeline run that reports success would look like "processed, nothing found"
            // rather than the true "could not read this document" (spec principle: source
            // evidence is mandatory).
            return Result<StagedExtractionSummary>.Failure(
                "Staged extraction requires at least one page of document text.");
        }

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var document = await dbContext.Documents
            .SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == documentId, cancellationToken)
            .ConfigureAwait(false);

        if (document is null)
        {
            return Result<StagedExtractionSummary>.Failure($"Document {documentId} was not found for this tenant.");
        }

        var now = clock.UtcNow;
        var contract = await EnsureContractAsync(tenantId, document, now, cancellationToken).ConfigureAwait(false);

        var documentText = BuildPageMarkedText(pages);
        var pageCount = pages.Count;

        document.ProcessingStatus = DocumentProcessingStatus.Processing;

        var stageResults = new List<StagedExtractionStageResult>(PipelineStages.Length);
        string? acceptedSupplierName = null;

        foreach (var stage in PipelineStages)
        {
            var (stageResult, stageSupplierName) = await RunStageAsync(
                    tenantId, document.Id, contract, stage, documentText, pageCount, cancellationToken)
                .ConfigureAwait(false);
            stageResults.Add(stageResult);

            // First accepted `supplier` fact wins — only the `metadata` stage can produce one
            // (MetadataFields), so this never silently picks between competing stages.
            acceptedSupplierName ??= stageSupplierName;
        }

        document.ProcessingStatus = DetermineDocumentStatus(stageResults);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditWriter.WriteAsync(
                new AuditEntry(
                    tenantId,
                    SystemActor,
                    $"document.extraction.{document.ProcessingStatus.ToString().ToLowerInvariant()}",
                    "document",
                    documentId.Value.ToString(),
                    now,
                    $"contractId={contract.Id.Value}"),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<StagedExtractionSummary>.Success(
            new StagedExtractionSummary(contract.Id, document.ProcessingStatus, stageResults, acceptedSupplierName));
    }

    /// <summary>
    /// Finds the <see cref="Contract"/> this document already links to, or creates a minimal
    /// shell and links it. Nothing in this wave consumes the <see cref="ExtractionStage.Classification"/>
    /// job <c>DocumentUploadService</c> queues at upload (that consumer is a later, not-yet-built
    /// task — <c>Contigo.Worker.Queue.QueueConsumerHostedService</c>'s own doc comment says
    /// dispatching a received message to a domain handler "is a later task once that handler
    /// exists"), so without this method every document would arrive here with
    /// <see cref="Document.ContractId"/> still null and this pipeline would have nothing to
    /// stage facts into. Seeding <see cref="Contract.Type"/> from <see cref="Document.DocumentType"/>
    /// (defaulting to <see cref="ContractDocumentType.Other"/> pre-classification) and a
    /// placeholder status/currency (<see cref="BootstrapContractStatus"/>/
    /// <see cref="BootstrapContractCurrency"/>) is honest about "extraction ran before
    /// classification finished", not a silent guess presented as a real extracted fact — the
    /// `metadata` stage overwrites status/currency the moment it finds a real value.
    /// </summary>
    private async Task<Contract> EnsureContractAsync(
        TenantId tenantId, Document document, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (document.ContractId is { } existingContractId)
        {
            var existing = await dbContext.Contracts
                .SingleOrDefaultAsync(c => c.TenantId == tenantId && c.Id == existingContractId, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                return existing;
            }

            // A dangling ContractId is unexpected (Contract rows are never deleted by this
            // module) but must not crash the pipeline — fall through and create a fresh shell
            // rather than leave the document permanently stuck with nothing to extract into.
        }

        var contract = new Contract
        {
            TenantId = tenantId,
            Type = document.DocumentType,
            Status = BootstrapContractStatus,
            Currency = BootstrapContractCurrency,
            CreatedAt = now,
        };

        dbContext.Contracts.Add(contract);
        document.ContractId = contract.Id;

        return contract;
    }

    /// <summary>Concatenates every page's text with an explicit <c>[[PAGE n]]</c> marker (ADR-017
    /// "Implications for the decomposition": "must persist a page map so evidence source.page /
    /// section still resolve") so a structured-output model can report which page a fact came
    /// from by reading these markers, without this pipeline needing to run a separate call per
    /// page.</summary>
    private static string BuildPageMarkedText(IReadOnlyList<DocumentPageText> pages)
    {
        var builder = new StringBuilder();

        foreach (var page in pages)
        {
            builder.Append("[[PAGE ").Append(page.PageNumber).Append("]]\n");
            builder.Append(page.Text);
            builder.Append("\n\n");
        }

        return builder.ToString();
    }

    /// <summary>Runs one stage and reports both its <see cref="StagedExtractionStageResult"/> and
    /// the `supplier` legal name that stage accepted, if any (see
    /// <see cref="StagedExtractionSummary.AcceptedSupplierName"/>) — the two travel together
    /// because whether the fact was accepted at all is decided here, against
    /// <see cref="CriticalConfidenceThreshold"/>, not by the caller re-reading evidence rows.</summary>
    private async Task<(StagedExtractionStageResult Result, string? AcceptedSupplierName)> RunStageAsync(
        TenantId tenantId,
        EntityId documentId,
        Contract contract,
        ExtractionStage stage,
        string documentText,
        int pageCount,
        CancellationToken cancellationToken)
    {
        var startedAt = clock.UtcNow;

        var job = new ExtractionJob
        {
            TenantId = tenantId,
            DocumentId = documentId,
            Stage = stage,
            Status = ExtractionJobStatus.Running,
            QueuedAt = startedAt,
            StartedAt = startedAt,
        };
        dbContext.ExtractionJobs.Add(job);

        var request = new AiExtractionRequest(
            StageName: stage.ToString(),
            DocumentText: documentText,
            JsonSchema: BuildSchema(stage));

        var extractResult = await aiGateway.ExtractAsync(request, cancellationToken).ConfigureAwait(false);

        var completedAt = clock.UtcNow;

        if (extractResult.IsFailure)
        {
            job.Status = ExtractionJobStatus.Failed;
            job.ErrorDetail = Truncate(extractResult.Error);
            job.CompletedAt = completedAt;

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return (new StagedExtractionStageResult(stage, job.Status, 0, 0, job.ErrorDetail), null);
        }

        job.ModelId = extractResult.Value.Metadata.ModelId;

        StageApplyResult applied;

        try
        {
            applied = stage switch
            {
                ExtractionStage.Metadata => ApplyFacts(
                    tenantId, contract, documentId, job.Id, extractResult.Value.PayloadJson,
                    MetadataFields, pageCount, startedAt, ApplyMetadataFact),
                ExtractionStage.CommercialTerms => ApplyFacts(
                    tenantId, contract, documentId, job.Id, extractResult.Value.PayloadJson,
                    CommercialTermsFields, pageCount, startedAt, ApplyCommercialTermsFact),
                ExtractionStage.DatesAndRenewalTerms => ApplyFacts(
                    tenantId, contract, documentId, job.Id, extractResult.Value.PayloadJson,
                    DatesFields, pageCount, startedAt, ApplyDatesFact),
                ExtractionStage.LineItems => ApplyLineItems(
                    tenantId, contract, extractResult.Value.PayloadJson, pageCount, startedAt),
                ExtractionStage.LegalClauses => ApplyClauses(
                    tenantId, contract, documentId, extractResult.Value.PayloadJson, pageCount, startedAt),
                ExtractionStage.Obligations => ApplyObligations(
                    tenantId, contract, documentId, extractResult.Value.PayloadJson, pageCount, startedAt),
                ExtractionStage.Risk => ApplyRisks(
                    tenantId, contract, extractResult.Value.PayloadJson, pageCount, startedAt),
                _ => throw new InvalidOperationException($"Stage {stage} is not part of the staged extraction pipeline."),
            };
        }
        catch (JsonException ex)
        {
            // The gateway does not validate the model's output against the schema it was given
            // (IAiGateway.ExtractAsync's own doc comment) — a real (non-fixture) model can still
            // return syntactically invalid JSON. One malformed stage must not crash the other six.
            job.Status = ExtractionJobStatus.Failed;
            job.ErrorDetail = Truncate($"Malformed extraction payload: {ex.Message}");
            job.CompletedAt = completedAt;

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return (new StagedExtractionStageResult(stage, job.Status, 0, 0, job.ErrorDetail), null);
        }

        // Human-in-the-loop principle: nothing extracted, something skipped, or any fact below its
        // own confidence bar (LowConfidenceThreshold, or CriticalConfidenceThreshold for a
        // CriticalFields entry) all mean a person should look at this stage before it is trusted,
        // even though the AI Gateway call itself succeeded.
        job.Status = applied.Extracted == 0 || applied.Skipped > 0 || applied.AnyBelowThreshold
            ? ExtractionJobStatus.NeedsReview
            : ExtractionJobStatus.Completed;
        job.CompletedAt = completedAt;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return (
            new StagedExtractionStageResult(stage, job.Status, applied.Extracted, applied.Skipped, null),
            applied.AcceptedSupplierName);
    }

    /// <summary>What one stage's `Apply...` call produced: the counts
    /// <see cref="StagedExtractionStageResult"/> reports, whether any fact fell below its own
    /// confidence bar, and the `supplier` legal name (if that stage accepted one). The last member
    /// defaults to <see langword="null"/> so the four "one row = one fact" stages — which can never
    /// produce a supplier fact — construct this with the same three values they always had.</summary>
    private readonly record struct StageApplyResult(
        int Extracted, int Skipped, bool AnyBelowThreshold, string? AcceptedSupplierName = null);

    private static string BuildSchema(ExtractionStage stage) => stage switch
    {
        ExtractionStage.Metadata => StagedExtractionJsonSchemas.Facts(MetadataFields),
        ExtractionStage.CommercialTerms => StagedExtractionJsonSchemas.Facts(CommercialTermsFields),
        ExtractionStage.DatesAndRenewalTerms => StagedExtractionJsonSchemas.Facts(DatesFields),
        ExtractionStage.LineItems => StagedExtractionJsonSchemas.LineItems(),
        ExtractionStage.LegalClauses => StagedExtractionJsonSchemas.Clauses(),
        ExtractionStage.Obligations => StagedExtractionJsonSchemas.Obligations(),
        ExtractionStage.Risk => StagedExtractionJsonSchemas.Risks(),
        _ => throw new InvalidOperationException($"Stage {stage} is not part of the staged extraction pipeline."),
    };

    /// <summary>Shared handling for the three scalar-field stages: parses the generic
    /// <see cref="ExtractedFactsPayload"/> shape, applies each recognized field onto
    /// <paramref name="contract"/> via <paramref name="applyToContract"/>, and records one
    /// <see cref="ExtractionEvidence"/> row per fact (AC-2). A fact naming a field outside
    /// <paramref name="allowedFields"/> is skipped, not applied — the JSON Schema's own
    /// <c>enum</c> constrains a well-behaved model to never send one, but this method does not
    /// trust that alone.
    ///
    /// <para>
    /// A <see cref="CriticalFields"/> entry (requirements R-SUP-01, spec §7.3) is judged against
    /// <see cref="CriticalConfidenceThreshold"/> rather than <see cref="LowConfidenceThreshold"/>,
    /// but is otherwise handled identically: the <see cref="ExtractionEvidence"/> row is written
    /// either way, so a rejected critical fact still reaches the review list <em>with</em> its page,
    /// span and confidence — a reviewer needs to see what the model proposed and why it was not
    /// trusted, not an empty field. Only <see cref="StageApplyResult.AcceptedSupplierName"/>
    /// distinguishes the two outcomes downstream.
    /// </para>
    /// </summary>
    private StageApplyResult ApplyFacts(
        TenantId tenantId,
        Contract contract,
        EntityId sourceDocumentId,
        EntityId extractionJobId,
        string payloadJson,
        IReadOnlyList<string> allowedFields,
        int pageCount,
        DateTimeOffset now,
        Action<Contract, string, string?> applyToContract)
    {
        var payload = JsonSerializer.Deserialize<ExtractedFactsPayload>(payloadJson, PayloadSerializerOptions);
        var facts = payload?.Facts ?? [];

        var extracted = 0;
        var skipped = 0;
        var anyBelowThreshold = false;
        string? acceptedSupplierName = null;

        foreach (var fact in facts)
        {
            if (fact.Field is null || !allowedFields.Contains(fact.Field, StringComparer.Ordinal))
            {
                skipped++;
                continue;
            }

            applyToContract(contract, fact.Field, fact.Value);

            var isCritical = CriticalFields.Contains(fact.Field);
            var threshold = isCritical ? CriticalConfidenceThreshold : LowConfidenceThreshold;
            var accepted = fact.Confidence is { } confidence && confidence >= threshold;

            if (!accepted)
            {
                anyBelowThreshold = true;
            }
            else if (fact.Field == SupplierFieldName && !string.IsNullOrWhiteSpace(fact.Value))
            {
                acceptedSupplierName = fact.Value.Trim();
            }

            dbContext.ExtractionEvidences.Add(new ExtractionEvidence
            {
                TenantId = tenantId,
                ContractId = contract.Id,
                SourceDocumentId = sourceDocumentId,
                ExtractionJobId = extractionJobId,
                FieldName = fact.Field,
                Value = fact.Value,
                SourceSpan = fact.SourceSpan,
                SourcePage = ClampPage(fact.SourcePage, pageCount),
                Confidence = fact.Confidence,
                CreatedAt = now,
            });

            extracted++;
        }

        return new StageApplyResult(extracted, skipped, anyBelowThreshold, acceptedSupplierName);
    }

    private static void ApplyMetadataFact(Contract contract, string field, string? value)
    {
        switch (field)
        {
            case SupplierFieldName:
                // Deliberately writes nothing onto Contract. `supplier` is a legal *name*;
                // Contract.SupplierId is a cross-module reference this module may not resolve
                // itself (ADR-002 — Documents/Contracts never references Contigo.Suppliers.Products),
                // so the link is made one layer up by DocumentProcessingPipeline through
                // ISupplierResolver. The fact is still fully persisted as its own
                // ExtractionEvidence row by the caller, exactly like every other metadata fact.
                break;
            case "currency":
                if (!string.IsNullOrWhiteSpace(value))
                {
                    contract.Currency = value.Trim();
                }

                break;
            case "governingLaw":
                contract.GoverningLaw = value;
                break;
            case "status":
                if (!string.IsNullOrWhiteSpace(value))
                {
                    contract.Status = value.Trim();
                }

                break;
        }
    }

    private static void ApplyCommercialTermsFact(Contract contract, string field, string? value)
    {
        switch (field)
        {
            case "annualSpend":
                if (TryParseDecimal(value, out var annualSpend))
                {
                    contract.AnnualSpend = annualSpend;
                }

                break;
            case "totalContractValue":
                if (TryParseDecimal(value, out var totalContractValue))
                {
                    contract.TotalContractValue = totalContractValue;
                }

                break;
            case "paymentTerms":
                contract.PaymentTerms = value;
                break;
        }
    }

    private static void ApplyDatesFact(Contract contract, string field, string? value)
    {
        switch (field)
        {
            case "startDate":
                if (TryParseDate(value, out var startDate))
                {
                    contract.StartDate = startDate;
                }

                break;
            case "endDate":
                if (TryParseDate(value, out var endDate))
                {
                    contract.EndDate = endDate;
                }

                break;
            case "effectiveDate":
                if (TryParseDate(value, out var effectiveDate))
                {
                    contract.EffectiveDate = effectiveDate;
                }

                break;
            case "cancellationDeadline":
                if (TryParseDate(value, out var cancellationDeadline))
                {
                    contract.CancellationDeadline = cancellationDeadline;
                }

                break;
            case "autoRenewal":
                if (bool.TryParse(value, out var autoRenewal))
                {
                    contract.AutoRenewal = autoRenewal;
                }

                break;
            case "renewalTermMonths":
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var months))
                {
                    contract.RenewalTermMonths = months;
                }

                break;
        }
    }

    private StageApplyResult ApplyLineItems(
        TenantId tenantId, Contract contract, string payloadJson, int pageCount, DateTimeOffset now)
    {
        var payload = JsonSerializer.Deserialize<ExtractedLineItemsPayload>(payloadJson, PayloadSerializerOptions);
        var items = payload?.Items ?? [];

        var extracted = 0;
        var skipped = 0;
        var anyLowConfidence = false;

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Description))
            {
                skipped++;
                continue;
            }

            if (item.Confidence is null || item.Confidence < LowConfidenceThreshold)
            {
                anyLowConfidence = true;
            }

            dbContext.ContractLineItems.Add(new ContractLineItem
            {
                TenantId = tenantId,
                ContractId = contract.Id,
                Sku = item.Sku,
                Description = item.Description,
                Quantity = item.Quantity,
                Unit = item.Unit,
                UnitPrice = item.UnitPrice,
                ListPrice = item.ListPrice,
                Discount = item.Discount,
                BillingPeriod = item.BillingPeriod,
                AnnualCost = item.AnnualCost,
                TotalCost = item.TotalCost,
                SourceSpan = item.SourceSpan,
                SourcePage = ClampPage(item.SourcePage, pageCount),
                Confidence = item.Confidence,
                CreatedAt = now,
            });

            extracted++;
        }

        return new StageApplyResult(extracted, skipped, anyLowConfidence);
    }

    private StageApplyResult ApplyClauses(
        TenantId tenantId, Contract contract, EntityId sourceDocumentId, string payloadJson, int pageCount, DateTimeOffset now)
    {
        var payload = JsonSerializer.Deserialize<ExtractedClausesPayload>(payloadJson, PayloadSerializerOptions);
        var items = payload?.Items ?? [];

        var extracted = 0;
        var skipped = 0;
        var anyLowConfidence = false;

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.ClauseType) || string.IsNullOrWhiteSpace(item.RawText))
            {
                skipped++;
                continue;
            }

            RiskSeverity? riskLevel = null;
            if (!string.IsNullOrWhiteSpace(item.RiskLevel)
                && Enum.TryParse<RiskSeverity>(item.RiskLevel, ignoreCase: true, out var parsedRiskLevel))
            {
                riskLevel = parsedRiskLevel;
            }

            if (item.Confidence is null || item.Confidence < LowConfidenceThreshold)
            {
                anyLowConfidence = true;
            }

            dbContext.Clauses.Add(new Clause
            {
                TenantId = tenantId,
                ContractId = contract.Id,
                SourceDocumentId = sourceDocumentId,
                ClauseType = item.ClauseType,
                RawText = item.RawText,
                NormalizedValue = item.NormalizedValue,
                RiskLevel = riskLevel,
                SourceSpan = item.SourceSpan,
                SourcePage = ClampPage(item.SourcePage, pageCount),
                Confidence = item.Confidence,
                CreatedAt = now,
            });

            extracted++;
        }

        return new StageApplyResult(extracted, skipped, anyLowConfidence);
    }

    private StageApplyResult ApplyObligations(
        TenantId tenantId, Contract contract, EntityId sourceDocumentId, string payloadJson, int pageCount, DateTimeOffset now)
    {
        var payload = JsonSerializer.Deserialize<ExtractedObligationsPayload>(payloadJson, PayloadSerializerOptions);
        var items = payload?.Items ?? [];

        var extracted = 0;
        var skipped = 0;
        var anyLowConfidence = false;

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Party)
                || string.IsNullOrWhiteSpace(item.ObligationType)
                || string.IsNullOrWhiteSpace(item.Description))
            {
                skipped++;
                continue;
            }

            DateOnly? dueDate = null;
            if (TryParseDate(item.DueDate, out var parsedDueDate))
            {
                dueDate = parsedDueDate;
            }

            if (item.Confidence is null || item.Confidence < LowConfidenceThreshold)
            {
                anyLowConfidence = true;
            }

            dbContext.Obligations.Add(new Obligation
            {
                TenantId = tenantId,
                ContractId = contract.Id,
                SourceDocumentId = sourceDocumentId,
                Party = item.Party,
                ObligationType = item.ObligationType,
                Description = item.Description,
                DueDate = dueDate,
                RecurrenceRule = item.RecurrenceRule,
                Criticality = item.Criticality,
                Status = item.Status,
                Confidence = item.Confidence,
                SourceSpan = item.SourceSpan,
                SourcePage = ClampPage(item.SourcePage, pageCount),
                CreatedAt = now,
            });

            extracted++;
        }

        return new StageApplyResult(extracted, skipped, anyLowConfidence);
    }

    private StageApplyResult ApplyRisks(
        TenantId tenantId, Contract contract, string payloadJson, int pageCount, DateTimeOffset now)
    {
        var payload = JsonSerializer.Deserialize<ExtractedRisksPayload>(payloadJson, PayloadSerializerOptions);
        var items = payload?.Items ?? [];

        var extracted = 0;
        var skipped = 0;
        var anyLowConfidence = false;

        foreach (var item in items)
        {
            // Risk.Severity is a required (non-nullable) column — an item whose severity does
            // not parse cannot be persisted at all (Appendix C rule 10: return uncertainty
            // instead of fabricated precision; fabricating a default severity would be exactly
            // that).
            if (string.IsNullOrWhiteSpace(item.RiskType)
                || string.IsNullOrWhiteSpace(item.Description)
                || string.IsNullOrWhiteSpace(item.Severity)
                || !Enum.TryParse<RiskSeverity>(item.Severity, ignoreCase: true, out var severity))
            {
                skipped++;
                continue;
            }

            if (item.Confidence is null || item.Confidence < LowConfidenceThreshold)
            {
                anyLowConfidence = true;
            }

            dbContext.Risks.Add(new Risk
            {
                TenantId = tenantId,
                ContractId = contract.Id,
                RiskType = item.RiskType,
                Severity = severity,
                Description = item.Description,
                Status = item.Status,
                Confidence = item.Confidence,
                SourceSpan = item.SourceSpan,
                SourcePage = ClampPage(item.SourcePage, pageCount),
                IdentifiedAt = now,
            });

            extracted++;
        }

        return new StageApplyResult(extracted, skipped, anyLowConfidence);
    }

    /// <summary>A page number the model reports outside the document's actual page range is
    /// treated as absent rather than stored as a plausible-looking but wrong citation (spec
    /// principle: source evidence is mandatory and must be trustworthy).</summary>
    private static int? ClampPage(int? page, int pageCount) =>
        page is >= 1 && page <= pageCount ? page : null;

    private static bool TryParseDecimal(string? value, out decimal result) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    private static bool TryParseDate(string? value, out DateOnly result) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    private static DocumentProcessingStatus DetermineDocumentStatus(
        IReadOnlyList<StagedExtractionStageResult> stages)
    {
        if (stages.All(s => s.Status == ExtractionJobStatus.Failed))
        {
            return DocumentProcessingStatus.Failed;
        }

        if (stages.Any(s => s.Status is ExtractionJobStatus.Failed or ExtractionJobStatus.NeedsReview))
        {
            return DocumentProcessingStatus.NeedsReview;
        }

        return DocumentProcessingStatus.Completed;
    }

    private static string Truncate(string value, int maxLength = 1000) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
