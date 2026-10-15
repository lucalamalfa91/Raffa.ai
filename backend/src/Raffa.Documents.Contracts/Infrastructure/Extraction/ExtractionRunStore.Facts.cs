using System.Globalization;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Infrastructure.Extraction;

/// <summary>Applying parsed stage facts onto the contract and the list tables (moved out of the former ExtractionOrchestrator).</summary>
public sealed partial class ExtractionRunStore
{
    /// <summary>
    /// Finds the <see cref="Contract"/> this document already links to, or creates a minimal
    /// shell and links it. Nothing in this wave consumes the <see cref="ExtractionStage.Classification"/>
    /// job <c>DocumentUploadService</c> queues at upload (that consumer is a later, not-yet-built
    /// task — <c>Raffa.Worker.Queue.QueueConsumerHostedService</c>'s own doc comment says
    /// dispatching a received message to a domain handler "is a later task once that handler
    /// exists"), so without this method every document would arrive here with
    /// <see cref="Document.ContractId"/> still null and this pipeline would have nothing to
    /// stage facts into. Seeding <see cref="Contract.Type"/> from <see cref="Document.DocumentType"/>
    /// (defaulting to <see cref="ContractDocumentType.Other"/> pre-classification) and a
    /// placeholder status/currency (<see cref="BootstrapContractStatus"/>/
    /// <see cref="BootstrapContractCurrency"/>) is honest about "extraction ran before
    /// classification finished", not a silent guess presented as a real extracted fact — the
    /// `metadata` stage overwrites status/currency the moment it finds a real value.
    ///
    /// <para>
    /// When an existing bootstrap contract is found (created by <c>DocumentUploadService</c> at
    /// upload time with <see cref="ContractDocumentType.Other"/> as a placeholder), its
    /// <see cref="Contract.Type"/> is promoted to <see cref="Document.DocumentType"/> the moment
    /// classification has resolved a specific type. <c>DocumentProcessingOrchestrator</c> flushes
    /// <see cref="Document.DocumentType"/> via <c>SaveChangesAsync</c> <em>before</em> calling
    /// <c>ExtractionOrchestrator.RunAsync</c>,
    /// so by the time this method runs the in-memory document already carries the real classified
    /// type and the promotion is safe. The promotion is guarded to only fire while the contract is
    /// still at the bootstrap placeholder (<c>Other</c>) and the document has been classified as a
    /// more specific type — this preserves any human correction a reviewer may already have applied.
    /// </para>
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
                // Promote the upload-time bootstrap placeholder (Other) to the now-classified
                // type. document.DocumentType has been set by the pipeline's ClassifyAsync and
                // flushed before RunAsync was called, so it already reflects the real classified
                // type for this run. Only fires while the contract is still at the bootstrap
                // default — preserves any human correction a reviewer may already have applied.
                if (existing.Type == ContractDocumentType.Other
                    && document.DocumentType != ContractDocumentType.Other)
                {
                    existing.Type = document.DocumentType;
                }

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

    /// <summary>What one stage's `Apply...` call produced: the counts
    /// <see cref="StagedExtractionStageResult"/> reports, and whether any fact fell below its own
    /// confidence bar.</summary>
    private readonly record struct StageApplyResult(int Extracted, int Skipped, bool AnyBelowThreshold);

    /// <summary>Shared handling for the three scalar-field stages: parses the generic
    /// <see cref="ExtractedFactsPayload"/>, applies each recognized field onto the contract via
    /// <paramref name="applyToContract"/> and records one <see cref="ExtractionEvidence"/> row per fact
    /// (AC-2), accepted or not, so a rejected fact still reaches the review list with its page, span,
    /// confidence and decision. A fact naming a field outside <paramref name="allowedFields"/> is
    /// skipped: the schema's <c>enum</c> constrains a well-behaved model, but this does not trust that.
    /// Every field is judged against <see cref="ExtractionConfidencePolicy"/>, no second bar.
    /// </summary>
    private StageApplyResult ApplyFacts(
        ExtractionRun context,
        EntityId extractionJobId,
        IReadOnlyList<ExtractedFact> facts,
        IReadOnlyList<string> allowedFields,
        DateTimeOffset now,
        Action<Contract, string, string?> applyToContract)
    {
        var contract = context.Contract;

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

            if (string.IsNullOrWhiteSpace(fact.Value))
            {
                // Strict structured outputs cannot omit a property, so "the document does not state
                // this" arrives as value: null. That is an absent fact: not extracted, not skipped, no
                // evidence row, and never a null overwrite of a field a previous stage or a human set.
                continue;
            }

            var page = ClampPage(fact.SourcePage, context.PageCount);

            // F5-T01: a field a human corrected or confirmed is theirs. Extraction does not write it;
            // a different reading is offered on the evidence list as a proposal to review, a reading
            // that adds nothing (same as the contract, or as the last proposal) is dropped.
            if (context.Protection.IsProtected(fact.Field))
            {
                extracted++;

                if (!ExtractionConfidencePolicy.IsStatusField(fact.Field)
                    && !ProposalAddsNothing(contract, context.Protection, fact.Field, fact.Value))
                {
                    anyBelowThreshold = true;
                    context.Protection.NoteLatestEvidence(fact.Field, fact.Value);
                    AddEvidence(
                        context, fact.Field, fact.Value, fact.Confidence, ExtractionConfidencePolicy.ReviewRequired,
                        now, extractionJobId, page, fact.SourceSpan);
                }

                continue;
            }

            applyToContract(contract, fact.Field, fact.Value);
            extracted++;

            // Status is derived from start/end after every stage has run (OfficializeDerivedStatus): a
            // fuzzy LLM "active" must not park the document in review, so it gets no evidence here.
            if (ExtractionConfidencePolicy.IsStatusField(fact.Field))
            {
                continue;
            }

            var confidence = fact.Confidence;
            string decision;
            if (fact.SpanVerified == false || fact.ReviewReason is not null)
            {
                // F5-D19 (Gate mode only) sets ReviewReason: a cross-stage rule involves this field.
                // F5-T07 (Gate mode only): the evidence span could not be found where the model said it is.
                // Never auto-accepted, never dropped: the value stays and the field goes to review.
                decision = ExtractionConfidencePolicy.ReviewRequired;
            }
            else if (ExtractionConfidencePolicy.IsExtractedStartDate(fact.Field) && TryParseDate(fact.Value, out _))
            {
                confidence = ExtractionConfidencePolicy.OfficialConfidence;
                decision = ExtractionConfidencePolicy.AutoAccepted;
            }
            else
            {
                decision = ExtractionConfidencePolicy.Decide(fact.Confidence);
            }

            if (decision != ExtractionConfidencePolicy.AutoAccepted)
            {
                anyBelowThreshold = true;
            }
            else if (fact.Field == ExtractionFieldNames.Supplier)
            {
                acceptedSupplierName = fact.Value.Trim();
            }

            AddEvidence(context, fact.Field, fact.Value, confidence, decision, now, extractionJobId, page, fact.SourceSpan);
        }

        // Only `metadata` produces a supplier, so this never silently picks between competing stages.
        context.AcceptedSupplierName ??= acceptedSupplierName;

        return new StageApplyResult(extracted, skipped, anyBelowThreshold);
    }

    private static void ApplyMetadataFact(Contract contract, string field, string? value)
    {
        switch (field)
        {
            case ExtractionFieldNames.Supplier:
                // Deliberately writes nothing onto Contract. `supplier` is a legal *name*;
                // Contract.SupplierId is a cross-module reference this module may not resolve
                // itself (ADR-002 — Documents/Contracts never references Raffa.Suppliers.Products),
                // so the link is made one layer up by DocumentProcessingOrchestrator through
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
                if (TryParseInt(value, out var months))
                {
                    contract.RenewalTermMonths = months;
                }

                break;
            case ExtractionFieldNames.NoticePeriodDays:
                if (TryParseInt(value, out var noticeDays) && noticeDays >= 0)
                {
                    contract.NoticePeriodDays = noticeDays;
                }

                break;
        }
    }

    /// <summary>
    /// Builds the rows of a list stage ("one row = one fact"): an item that <paramref name="toRow"/>
    /// maps to <see langword="null"/> is skipped, and one row below the confidence bar flags the stage
    /// for review.
    /// </summary>
    private static StageApplyResult CollectRows<TItem, TRow>(
        IEnumerable<TItem>? items,
        List<TRow> rows,
        Func<TItem, TRow?> toRow,
        Func<TRow, double?> confidenceOf,
        Func<TItem, bool>? spanUnverified = null)
        where TRow : class
    {
        var skipped = 0;
        var anyLowConfidence = false;

        foreach (var item in items ?? [])
        {
            if (toRow(item) is not { } row)
            {
                skipped++;
                continue;
            }

            anyLowConfidence |= ExtractionConfidencePolicy.RequiresReview(confidenceOf(row));

            // F5-T07 (Gate mode only: the flag is never set otherwise): an unverifiable span flags the stage
            // for review exactly like a weak confidence does; the row is kept, never dropped.
            if (spanUnverified?.Invoke(item) == true)
            {
                anyLowConfidence = true;
            }

            rows.Add(row);
        }

        return new StageApplyResult(rows.Count, skipped, anyLowConfidence);
    }

    private async Task<StageApplyResult> ApplyLineItemsAsync(
        ExtractionRun context, IReadOnlyList<ExtractedLineItemFact> items, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = new List<ContractLineItem>();
        var applied = CollectRows(
            items,
            rows,
            item => string.IsNullOrWhiteSpace(item.Description)
                ? null
                : new ContractLineItem
                {
                    TenantId = context.TenantId,
                    ContractId = context.Contract.Id,
                    SourceDocumentId = context.DocumentId,
                    ExtractionRunId = context.RunId,
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
                    SourcePage = ClampPage(item.SourcePage, context.PageCount),
                    Confidence = item.Confidence,
                    CreatedAt = now,
                },
            row => row.Confidence,
            item => item.SpanVerified == false);

        await ReplaceRowsAsync(
                context,
                dbContext.ContractLineItems.Where(x => x.TenantId == context.TenantId
                    && x.ContractId == context.Contract.Id
                    && (x.SourceDocumentId == context.DocumentId
                        || (context.ReplaceUnattributedRows && x.SourceDocumentId == null))),
                dbContext.ContractLineItems,
                rows,
                x => NormalizeKeyPart(x.Sku) + "|" + NormalizeKeyPart(x.Description),
                cancellationToken,
                isLinkedElsewhere: x => x.ProductId is not null)
            .ConfigureAwait(false);

        return applied;
    }

    private async Task<StageApplyResult> ApplyClausesAsync(
        ExtractionRun context, IReadOnlyList<ExtractedClauseFact> items, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = new List<Clause>();
        var applied = CollectRows(
            items,
            rows,
            item => string.IsNullOrWhiteSpace(item.ClauseType) || string.IsNullOrWhiteSpace(item.RawText)
                ? null
                : new Clause
                {
                    TenantId = context.TenantId,
                    ContractId = context.Contract.Id,
                    SourceDocumentId = context.DocumentId,
                    ExtractionRunId = context.RunId,
                    ClauseType = item.ClauseType,
                    RawText = item.RawText,
                    NormalizedValue = item.NormalizedValue,
                    RiskLevel = Enum.TryParse<RiskSeverity>(item.RiskLevel, ignoreCase: true, out var riskLevel) ? riskLevel : null,
                    SourceSpan = item.SourceSpan,
                    SourcePage = ClampPage(item.SourcePage, context.PageCount),
                    Confidence = item.Confidence,
                    CreatedAt = now,
                },
            row => row.Confidence,
            item => item.SpanVerified == false);

        // A risk may point at a clause (Risk.ClauseId, restrict): such a clause is not ours to delete.
        var referenced = (await dbContext.Risks
                .AsNoTracking()
                .Where(r => r.TenantId == context.TenantId && r.ContractId == context.Contract.Id && r.ClauseId != null)
                .Select(r => r.ClauseId!.Value)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToHashSet();

        await ReplaceRowsAsync(
                context,
                dbContext.Clauses.Where(x => x.TenantId == context.TenantId
                    && x.ContractId == context.Contract.Id
                    && x.SourceDocumentId == context.DocumentId),
                dbContext.Clauses,
                rows,
                x => NormalizeKeyPart(x.ClauseType) + "|" + NormalizeKeyPart(x.RawText),
                cancellationToken,
                isLinkedElsewhere: x => referenced.Contains(x.Id))
            .ConfigureAwait(false);

        return applied;
    }

    private async Task<StageApplyResult> ApplyObligationsAsync(
        ExtractionRun context, IReadOnlyList<ExtractedObligationFact> items, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = new List<Obligation>();
        var applied = CollectRows(
            items,
            rows,
            item => string.IsNullOrWhiteSpace(item.Party)
                || string.IsNullOrWhiteSpace(item.ObligationType)
                || string.IsNullOrWhiteSpace(item.Description)
                ? null
                : new Obligation
                {
                    TenantId = context.TenantId,
                    ContractId = context.Contract.Id,
                    SourceDocumentId = context.DocumentId,
                    ExtractionRunId = context.RunId,
                    Party = item.Party,
                    ObligationType = item.ObligationType,
                    Description = item.Description,
                    DueDate = TryParseDate(item.DueDate, out var dueDate) ? dueDate : null,
                    RecurrenceRule = item.RecurrenceRule,
                    Criticality = item.Criticality,
                    Status = item.Status,
                    Confidence = item.Confidence,
                    SourceSpan = item.SourceSpan,
                    SourcePage = ClampPage(item.SourcePage, context.PageCount),
                    CreatedAt = now,
                },
            row => row.Confidence,
            item => item.SpanVerified == false);

        await ReplaceRowsAsync(
                context,
                dbContext.Obligations.Where(x => x.TenantId == context.TenantId
                    && x.ContractId == context.Contract.Id
                    && x.SourceDocumentId == context.DocumentId),
                dbContext.Obligations,
                rows,
                x => NormalizeKeyPart(x.ObligationType) + "|" + NormalizeKeyPart(x.Party) + "|" + NormalizeKeyPart(x.Description),
                cancellationToken)
            .ConfigureAwait(false);

        return applied;
    }

    private async Task<StageApplyResult> ApplyRisksAsync(
        ExtractionRun context, IReadOnlyList<ExtractedRiskFact> items, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = new List<Risk>();
        var applied = CollectRows(
            items,
            rows,
            // Risk.Severity is a required column: an item whose severity does not parse cannot be
            // persisted, and a fabricated default would be exactly the fake precision Appendix C rule 10
            // forbids.
            item => string.IsNullOrWhiteSpace(item.RiskType)
                || string.IsNullOrWhiteSpace(item.Description)
                || !Enum.TryParse<RiskSeverity>(item.Severity, ignoreCase: true, out var severity)
                ? null
                : new Risk
                {
                    TenantId = context.TenantId,
                    ContractId = context.Contract.Id,
                    SourceDocumentId = context.DocumentId,
                    ExtractionRunId = context.RunId,
                    RiskType = item.RiskType,
                    Severity = severity,
                    Description = item.Description,
                    Status = item.Status,
                    Confidence = item.Confidence,
                    SourceSpan = item.SourceSpan,
                    SourcePage = ClampPage(item.SourcePage, context.PageCount),
                    IdentifiedAt = now,
                },
            row => row.Confidence,
            item => item.SpanVerified == false);

        await ReplaceRowsAsync(
                context,
                dbContext.Risks.Where(x => x.TenantId == context.TenantId
                    && x.ContractId == context.Contract.Id
                    && (x.SourceDocumentId == context.DocumentId
                        || (context.ReplaceUnattributedRows && x.SourceDocumentId == null))),
                dbContext.Risks,
                rows,
                x => NormalizeKeyPart(x.RiskType) + "|" + NormalizeKeyPart(x.Description),
                cancellationToken)
            .ConfigureAwait(false);

        return applied;
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

    private static bool TryParseInt(string? value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    /// <summary>
    /// Decides the final <see cref="DocumentProcessingStatus"/> for the document once all seven
    /// stages have run. Shares <see cref="ExtractionConfidencePolicy"/> with
    /// <c>DocumentQueryService.IsWeak</c>: a document whose Documents-row badge says "needs
    /// review" has <see cref="DocumentProcessingStatus.NeedsReview"/>, and vice versa.
    ///
    /// <para>
    /// The document needs a human exactly when there is something a human can decide: a
    /// NeedsReview stage (a fact below the bar) or a classification below the same bar. A stage
    /// that found nothing does not route the document to review (ADR-024 amendment 2026-09-21):
    /// with every found fact at or above the bar the document completes on its own instead of
    /// asking the user to confirm an empty review list. Stage <em>failures</em> (model/network
    /// error, malformed payload) still require review: a low-confidence fact is a "weak signal";
    /// a failed stage is a "missing signal". Either way the caller then completes the document
    /// anyway when the contract has no weak field left to show (never "Review 0 fields").
    /// </para>
    /// </summary>
    private static DocumentProcessingStatus DetermineDocumentStatus(
        IReadOnlyList<StagedExtractionStageResult> stages, double? classificationConfidence)
    {
        if (stages.All(s => s.Status == ExtractionJobStatus.Failed))
        {
            return DocumentProcessingStatus.Failed;
        }

        if (stages.Any(s => s.Status == ExtractionJobStatus.Failed))
        {
            return DocumentProcessingStatus.NeedsReview;
        }

        if (stages.Any(s => s.Status == ExtractionJobStatus.NeedsReview))
        {
            return DocumentProcessingStatus.NeedsReview;
        }

        // The classification is a fact like any other (see ExtractionFieldNames.Type): a weak one means a
        // human should confirm the contract type before the document counts as validated.
        if (ExtractionConfidencePolicy.RequiresReview(classificationConfidence)
            && classificationConfidence is not null)
        {
            return DocumentProcessingStatus.NeedsReview;
        }

        return DocumentProcessingStatus.Completed;
    }

    /// <summary>
    /// Overwrites <see cref="Contract.Status"/> with the date-derived value and records it as
    /// <c>auto_accepted</c> at <see cref="ExtractionConfidencePolicy.OfficialConfidence"/>. A fuzzy
    /// metadata-stage guess is never left as <c>review_required</c>. When neither start nor end is
    /// known, the interim extracted status (if any, and not the bootstrap placeholder) is
    /// officialized so the field still cannot block validation.
    /// </summary>
    private void OfficializeDerivedStatus(ExtractionRun context, DateTimeOffset now)
    {
        var contract = context.Contract;

        // F5-T01: a status a human set or confirmed is not re-derived over their head.
        if (context.Protection.IsProtected(ExtractionConfidencePolicy.StatusFieldName))
        {
            return;
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var derived = ExtractionConfidencePolicy.DeriveStatus(contract.StartDate, contract.EndDate, today);
        var status = derived
            ?? (string.Equals(contract.Status, BootstrapContractStatus, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(contract.Status)
                    ? null
                    : contract.Status.Trim().ToLowerInvariant());

        if (status is null)
        {
            return;
        }

        contract.Status = status;
        AddEvidence(
            context,
            ExtractionConfidencePolicy.StatusFieldName,
            status,
            ExtractionConfidencePolicy.OfficialConfidence,
            ExtractionConfidencePolicy.AutoAccepted,
            now);
    }

    /// <summary>
    /// Writes the <see cref="ExtractionFieldNames.Type"/> evidence row for this run's classification verdict:
    /// the proposed type is the document's (what the `classify` role said), never the contract's
    /// current one (which a human may already have corrected), so the row records the proposal
    /// exactly as every other <see cref="ExtractionEvidence"/> row does. Linked to the most recent
    /// classification <see cref="ExtractionJob"/> for traceability when one exists; no page or span
    /// — the classification reads the whole document, and inventing "page 1" would be the fabricated
    /// precision Appendix C rule 10 forbids.
    /// </summary>
    private async Task RecordClassificationEvidenceAsync(
        ExtractionRun context,
        Document document,
        double confidence,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var tenantId = context.TenantId;
        var contract = context.Contract;
        var classificationJobId = await dbContext.ExtractionJobs
            .Where(j => j.TenantId == tenantId && j.DocumentId == document.Id && j.Stage == ExtractionStage.Classification)
            .OrderByDescending(j => j.QueuedAt)
            .Select(j => (EntityId?)j.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var decision = ExtractionConfidencePolicy.Decide(confidence);

        // F5-T01: a contract type a human set or confirmed is theirs. A verdict that agrees with it (or
        // repeats what was proposed last time) adds nothing; one that disagrees is a proposal to review.
        if (context.Protection.IsProtected(ExtractionFieldNames.Type))
        {
            if (ProposalAddsNothing(contract, context.Protection, ExtractionFieldNames.Type, document.DocumentType.ToString()))
            {
                return;
            }

            decision = ExtractionConfidencePolicy.ReviewRequired;
        }

        AddEvidence(
            context, ExtractionFieldNames.Type, document.DocumentType.ToString(), confidence, decision, now, classificationJobId);
    }

    /// <summary>
    /// Display of the already-compared stored double. Not a rounding step in the decision
    /// (ADR-024 w17 clause A1): <see cref="ExtractionConfidencePolicy.Decide"/> still sees the
    /// raw value. Default general format so the trail shows <c>0.95</c>, not a G17 round-trip.
    /// </summary>
    private static string FormatConfidence(double? confidence) =>
        confidence is { } value ? value.ToString(CultureInfo.InvariantCulture) : "null";

    private static string Truncate(string value, int maxLength = 1000) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
