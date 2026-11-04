using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Raffa.Documents.Contracts.Application.Facts;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Documents.Contracts.Application.V4;

/// <summary>
/// Fixture mode only: recognises the scripted sample document ("Salesforce_MSA_2024.pdf") whose
/// reading advances on a timer (prototype <c>upload()</c>: one stage every 600 ms, then review)
/// and whose flagged fields are the ones its contract facts carry. Never registered in production,
/// where every document follows the real pipeline.
/// </summary>
public interface ISampleDocumentReading
{
    bool IsSample(Document document);

    /// <summary>How long each of the five stages lasts.</summary>
    TimeSpan StageDuration { get; }
}

/// <summary>
/// AskRaffa 4.0 Revisione: the reading of one document (stages → the fields to verify, one at a
/// time → validated). Real documents map the pipeline status and the weak
/// <c>extraction_evidence</c> rows (with <c>flagReason</c> and <c>critical</c>); accepting or
/// correcting the last field validates the document and writes the contract's facts, so
/// <see cref="IContractFactsReader"/> lists it.
/// </summary>
public sealed class DocumentReadingService(
    DocumentsContractsDbContext dbContext,
    ITenantContext tenantContext,
    IClock clock,
    ContractCorrectionService correctionService,
    DocumentValidationService validationService,
    IEnumerable<ISampleDocumentReading> samples,
    ISupplierNameLookup? supplierNameLookup = null)
{
    private readonly ISampleDocumentReading? _sample = samples.FirstOrDefault();

    public async Task<DocumentReadingView?> GetAsync(
        TenantId tenantId, EntityId documentId, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);
        var document = await dbContext.Documents
            .SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == documentId, cancellationToken)
            .ConfigureAwait(false);
        return document is null ? null : await ViewAsync(tenantId, document, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The most recent document still being read or waiting for review (Contratti banner).</summary>
    public async Task<DocumentAttention?> GetAttentionAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);
        var pending = await dbContext.Documents
            .Where(d => d.TenantId == tenantId && (d.ProcessingStatus == DocumentProcessingStatus.Uploaded
                || d.ProcessingStatus == DocumentProcessingStatus.Processing
                || d.ProcessingStatus == DocumentProcessingStatus.NeedsReview))
            .GroupBy(d => d.ProcessingStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var reading = pending.Where(p => p.Status != DocumentProcessingStatus.NeedsReview).Sum(p => p.Count);
        var toReview = pending.Where(p => p.Status == DocumentProcessingStatus.NeedsReview).Sum(p => p.Count);

        // The banner leads to what the user can act on first: the oldest document waiting for review,
        // else the newest one being read.
        var open = await dbContext.Documents
            .Where(d => d.TenantId == tenantId && d.ProcessingStatus == DocumentProcessingStatus.NeedsReview)
            .OrderBy(d => d.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? await dbContext.Documents
            .Where(d => d.TenantId == tenantId && (d.ProcessingStatus == DocumentProcessingStatus.Uploaded
                || d.ProcessingStatus == DocumentProcessingStatus.Processing))
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (open is null)
        {
            return null;
        }

        var view = await ViewAsync(tenantId, open, cancellationToken).ConfigureAwait(false);
        if (view.Status is not (ReadingStatus.Processing or ReadingStatus.Review))
        {
            return null;
        }

        return new DocumentAttention(view.DocumentId, view.File, view.Status, view.Fields.Count(f => f.Decision is null), reading, toReview);
    }

    /// <summary>
    /// Records the decision on one field; when it was the last undecided field the document is
    /// validated. Null when the document does not exist; a failure for an unknown field, a document
    /// that is not in review, or a correction the contract does not accept.
    /// </summary>
    public async Task<Result<DocumentReadingView>?> DecideAsync(
        TenantId tenantId,
        EntityId documentId,
        string fieldKey,
        string decision,
        string? value,
        string actor,
        string decidedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(decidedBy);
        if (decision is not (FieldDecisions.Accept or FieldDecisions.Correct))
        {
            return Result<DocumentReadingView>.Failure("decision must be accept or correct.");
        }

        if (decision == FieldDecisions.Correct && string.IsNullOrWhiteSpace(value))
        {
            return Result<DocumentReadingView>.Failure("A correction needs the corrected value.");
        }

        using var _ = tenantContext.BeginScope(tenantId);
        var document = await dbContext.Documents
            .SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == documentId, cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        var view = await ViewAsync(tenantId, document, cancellationToken).ConfigureAwait(false);
        if (view.Status != ReadingStatus.Review)
        {
            return Result<DocumentReadingView>.Failure($"The document is not waiting for review (status {view.Status}).");
        }

        var field = view.Fields.FirstOrDefault(f => string.Equals(f.Key, fieldKey, StringComparison.Ordinal));
        if (field is null)
        {
            return Result<DocumentReadingView>.Failure($"Unknown field '{fieldKey}'.");
        }

        var isSample = _sample?.IsSample(document) == true;
        if (!isSample && decision == FieldDecisions.Correct && document.ContractId is { } contractId)
        {
            var corrected = await correctionService.CorrectAsync(
                    tenantId, contractId, new Dictionary<string, string?> { [fieldKey] = value!.Trim() },
                    "Revisione", actor, cancellationToken)
                .ConfigureAwait(false);
            if (corrected.IsFailure)
            {
                return Result<DocumentReadingView>.Failure(corrected.Error);
            }
        }

        var row = await dbContext.DocumentFieldDecisions
            .SingleOrDefaultAsync(d => d.TenantId == tenantId && d.DocumentId == documentId && d.FieldKey == fieldKey, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            dbContext.DocumentFieldDecisions.Add(new DocumentFieldDecision
            {
                TenantId = tenantId,
                DocumentId = documentId,
                FieldKey = fieldKey,
                Decision = decision,
                Value = decision == FieldDecisions.Correct ? value!.Trim() : null,
                DecidedBy = decidedBy,
                DecidedAt = clock.UtcNow,
            });
        }
        else
        {
            row.Decision = decision;
            row.Value = decision == FieldDecisions.Correct ? value!.Trim() : null;
            row.DecidedBy = decidedBy;
            row.DecidedAt = clock.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var remaining = view.Fields.Count(f => f.Decision is null && f.Key != fieldKey);
        if (remaining == 0)
        {
            await ValidateAsync(tenantId, document, view, isSample, actor, cancellationToken).ConfigureAwait(false);
        }

        return Result<DocumentReadingView>.Success(await ViewAsync(tenantId, document, cancellationToken).ConfigureAwait(false));
    }

    private async Task ValidateAsync(
        TenantId tenantId, Document document, DocumentReadingView view, bool isSample, string actor, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var decisions = await DecisionsAsync(tenantId, document.Id, cancellationToken).ConfigureAwait(false);
        if (isSample)
        {
            document.ProcessingStatus = DocumentProcessingStatus.Completed;
        }
        else
        {
            var accepted = view.Fields.Select(f => f.Key).ToList();
            var validated = await validationService.ValidateAsync(tenantId, document.Id, accepted, actor, cancellationToken)
                .ConfigureAwait(false);
            if (validated is { IsFailure: true })
            {
                throw new InvalidOperationException(validated.Error);
            }
        }

        if (document.ContractId is not { } contractId)
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var record = await dbContext.ContractFacts
            .SingleOrDefaultAsync(f => f.TenantId == tenantId && f.ContractId == contractId, cancellationToken)
            .ConfigureAwait(false);
        ContractFacts facts;
        if (record is not null && isSample)
        {
            facts = System.Text.Json.JsonSerializer.Deserialize<ContractFacts>(record.Facts, ContractFactsReader.Json) ?? new ContractFacts();
            facts = facts with
            {
                ReviewFields = facts.ReviewFields
                    .Select(f => decisions.TryGetValue(f.Ev, out var d) && d.Decision == FieldDecisions.Correct && d.Value is not null
                        ? f with { Value = d.Value }
                        : f)
                    .ToList(),
            };
        }
        else
        {
            facts = await BuildFactsAsync(tenantId, contractId, document, cancellationToken).ConfigureAwait(false);
        }

        facts = facts with { ValidatedAt = now };
        if (record is null)
        {
            dbContext.ContractFacts.Add(new ContractFactsRecord
            {
                TenantId = tenantId,
                ContractId = contractId,
                Facts = ContractFactsReader.Serialize(facts),
                ValidatedAt = now,
                UpdatedAt = now,
            });
        }
        else
        {
            record.Facts = ContractFactsReader.Serialize(facts);
            record.ValidatedAt = now;
            record.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task<ContractFacts> BuildFactsAsync(
        TenantId tenantId, EntityId contractId, Document document, CancellationToken cancellationToken) =>
        new ContractFactsProjector(dbContext, clock, supplierNameLookup).BuildAsync(tenantId, contractId, document, cancellationToken);

    private async Task<DocumentReadingView> ViewAsync(TenantId tenantId, Document document, CancellationToken cancellationToken)
    {
        if (_sample?.IsSample(document) == true)
        {
            return await SampleViewAsync(tenantId, document, cancellationToken).ConfigureAwait(false);
        }

        var supplier = await SupplierAsync(tenantId, document, cancellationToken).ConfigureAwait(false);
        switch (document.ProcessingStatus)
        {
            case DocumentProcessingStatus.Uploaded:
            case DocumentProcessingStatus.Processing:
            {
                var jobs = await dbContext.ExtractionJobs.AsNoTracking()
                    .Where(j => j.TenantId == tenantId && j.DocumentId == document.Id)
                    .Select(j => new ExtractionJobSnapshot(j.Stage, j.Status, j.StartedAt))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                var stage = ReadingStages.FromPipeline(DocumentProcessingStageMap.Resolve(jobs));
                return new DocumentReadingView(document.Id.Value, document.ContractId?.Value, document.FileName, supplier,
                    ReadingStatus.Processing, stage, ReadingStages.Label(stage), null, []);
            }

            case DocumentProcessingStatus.Failed:
            case DocumentProcessingStatus.Rejected:
                return new DocumentReadingView(document.Id.Value, document.ContractId?.Value, document.FileName, supplier,
                    ReadingStatus.Failed, 0, ReadingStages.Label(1), ExtractionFlagReason.FailureFor(document), []);
        }

        var fields = await RealFieldsAsync(tenantId, document, cancellationToken).ConfigureAwait(false);
        var status = document.ProcessingStatus == DocumentProcessingStatus.Completed ? ReadingStatus.Done : ReadingStatus.Review;
        return new DocumentReadingView(document.Id.Value, document.ContractId?.Value, document.FileName, supplier,
            status, ReadingStages.Labels.Count, ReadingStages.Label(ReadingStages.Labels.Count), null, fields);
    }

    private async Task<IReadOnlyList<ReadingField>> RealFieldsAsync(
        TenantId tenantId, Document document, CancellationToken cancellationToken)
    {
        if (document.ContractId is not { } contractId)
        {
            return [];
        }

        var decisions = await DecisionsAsync(tenantId, document.Id, cancellationToken).ConfigureAwait(false);
        var rows = await dbContext.ExtractionEvidences.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.ContractId == contractId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var latest = rows
            .GroupBy(e => e.FieldName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id.Value).First())
            // A field the user already decided here stays in the list (as decided), even though
            // the decision made it no longer weak.
            .Where(e => ExtractionConfidencePolicy.IsWeakEvidence(e.Confidence, e.Decision) || decisions.ContainsKey(e.FieldName))
            .Where(e => !ExtractionConfidencePolicy.IsStatusField(e.FieldName))
            .OrderByDescending(e => ExtractionFlagReason.IsCritical(e.FieldName))
            .ThenBy(e => e.SourcePage ?? int.MaxValue)
            .ThenBy(e => e.FieldName, StringComparer.Ordinal)
            .ToList();

        var passages = await PassagesAsync(tenantId, contractId, cancellationToken).ConfigureAwait(false);
        return latest.Select(e =>
        {
            decisions.TryGetValue(e.FieldName, out var decision);
            passages.TryGetValue(e.FieldName, out var passage);
            var reason = ExtractionFlagReason.For(e.FieldName, e.Confidence);
            return new ReadingField(
                e.FieldName,
                ReadingFieldLabels.For(e.FieldName),
                e.OverrideValue ?? e.Value ?? "",
                e.SourcePage,
                e.SourcePage is { } page ? $"p.{page}" : "",
                reason,
                reason,
                passage ?? ExtractionFlagReason.Quote(null, null, null, e.SourceSpan),
                ExtractionFlagReason.IsCritical(e.FieldName),
                decision?.Decision,
                decision?.Value);
        }).ToList();
    }

    /// <summary>The quoted passages the evidence query already computes (page text around the span).</summary>
    private async Task<Dictionary<string, ReadingQuote?>> PassagesAsync(
        TenantId tenantId, EntityId contractId, CancellationToken cancellationToken)
    {
        var evidence = await new ContractEvidenceQueryService(dbContext, tenantContext)
            .GetLatestAsync(tenantId, contractId, cancellationToken)
            .ConfigureAwait(false);
        return (evidence ?? [])
            .GroupBy(e => e.FieldName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => ExtractionFlagReason.Quote(g.First().Passage, g.First().HighlightStart, g.First().HighlightLength, g.First().SourceSpan),
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task<DocumentReadingView> SampleViewAsync(TenantId tenantId, Document document, CancellationToken cancellationToken)
    {
        var record = document.ContractId is { } contractId
            ? await dbContext.ContractFacts.AsNoTracking()
                .SingleOrDefaultAsync(f => f.TenantId == tenantId && f.ContractId == contractId, cancellationToken)
                .ConfigureAwait(false)
            : null;
        var facts = record is null
            ? new ContractFacts()
            : System.Text.Json.JsonSerializer.Deserialize<ContractFacts>(record.Facts, ContractFactsReader.Json) ?? new ContractFacts();

        if (document.ProcessingStatus is DocumentProcessingStatus.Uploaded or DocumentProcessingStatus.Processing)
        {
            var stageDuration = _sample!.StageDuration;
            var elapsed = clock.UtcNow - document.CreatedAt;
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            var ticks = (int)(elapsed.Ticks / Math.Max(1, stageDuration.Ticks));
            if (ticks <= ReadingStages.Labels.Count)
            {
                // Prototype: the label shown is the last stage reached (Caricamento until the second tick).
                var stage = Math.Clamp(ticks, 1, ReadingStages.Labels.Count);
                return new DocumentReadingView(document.Id.Value, document.ContractId?.Value, document.FileName, facts.Supplier,
                    ReadingStatus.Processing, stage, ReadingStages.Label(stage), null, []);
            }

            document.ProcessingStatus = DocumentProcessingStatus.NeedsReview;
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var decisions = await DecisionsAsync(tenantId, document.Id, cancellationToken).ConfigureAwait(false);
        var fields = facts.ReviewFields.Select(f =>
        {
            facts.Evidence.TryGetValue(f.Ev, out var ev);
            decisions.TryGetValue(f.Ev, out var decision);
            return new ReadingField(
                f.Ev,
                f.Label,
                f.Value,
                ev?.Page,
                f.Src,
                f.Why ?? "",
                ContractMessage.Of(string.IsNullOrEmpty(f.Critical) ? "Low reading confidence" : "Critical field"),
                ev is null ? null : new ReadingQuote(ev.Before, ev.Quote, ev.After),
                !string.IsNullOrEmpty(f.Critical),
                decision?.Decision,
                decision?.Value);
        }).ToList();

        var status = document.ProcessingStatus == DocumentProcessingStatus.Completed ? ReadingStatus.Done : ReadingStatus.Review;
        return new DocumentReadingView(document.Id.Value, document.ContractId?.Value, document.FileName, facts.Supplier,
            status, ReadingStages.Labels.Count, ReadingStages.Label(ReadingStages.Labels.Count), null, fields);
    }

    private async Task<Dictionary<string, DocumentFieldDecision>> DecisionsAsync(
        TenantId tenantId, EntityId documentId, CancellationToken cancellationToken)
    {
        var rows = await dbContext.DocumentFieldDecisions.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.DocumentId == documentId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(r => r.FieldKey, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<string?> SupplierAsync(TenantId tenantId, Document document, CancellationToken cancellationToken)
    {
        if (document.ContractId is not { } contractId)
        {
            return null;
        }

        var facts = await dbContext.ContractFacts.AsNoTracking()
            .Where(f => f.TenantId == tenantId && f.ContractId == contractId)
            .Select(f => f.Facts)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (facts is not null)
        {
            var supplier = System.Text.Json.JsonSerializer.Deserialize<ContractFacts>(facts, ContractFactsReader.Json)?.Supplier;
            if (!string.IsNullOrEmpty(supplier))
            {
                return supplier;
            }
        }

        if (supplierNameLookup is null)
        {
            return null;
        }

        var supplierId = await dbContext.Contracts.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Id == contractId)
            .Select(c => c.SupplierId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (supplierId is not { } id)
        {
            return null;
        }

        var names = await supplierNameLookup.GetNamesAsync(tenantId, [id], cancellationToken).ConfigureAwait(false);
        return names.GetValueOrDefault(id);
    }
}
