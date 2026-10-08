using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// F5-T02 / F5-D03: which stages of a document's staged extraction currently stand failed. A
/// document with at least one such stage is <em>partial</em>: it holds the facts the other stages
/// produced, is never <c>Completed</c> (not by the pipeline, not by
/// <see cref="NothingToReviewAutoValidator"/>), and the failed stage alone is what a retry runs again.
///
/// <para>
/// A stage "stands failed" when its newest job is <see cref="ExtractionJobStatus.Failed"/>. Newest is
/// by <see cref="ExtractionJob.QueuedAt"/>; a finished job wins a tie, because a retry only ever
/// follows a failure. A stage that has no job at all, or whose newest job is still running, is not
/// reported: nothing has failed there.
/// </para>
/// </summary>
public static class ExtractionPartialState
{
    /// <summary>Stage jobs a document's extraction creates (everything except classification).</summary>
    private static readonly HashSet<ExtractionStage> PipelineStages = [.. StagedExtractionService.Stages];

    /// <summary>One failed stage, with what a retry policy needs to know about it.</summary>
    /// <param name="FailedAttempts">How many jobs of this stage have failed so far, over every run of
    /// the document: the bound on automatic retries.</param>
    /// <param name="LastFailedAt">When the newest failed job ended (or was queued, if it never ended).</param>
    public sealed record FailedStage(
        ExtractionStage Stage,
        ExtractionStageFailureKind? Kind,
        string? ErrorDetail,
        int FailedAttempts,
        DateTimeOffset LastFailedAt);

    public sealed record StageJobFacts(
        ExtractionStage Stage,
        ExtractionJobStatus Status,
        ExtractionStageFailureKind? FailureKind,
        string? ErrorDetail,
        DateTimeOffset QueuedAt,
        DateTimeOffset? CompletedAt);

    /// <summary>The failed stages among <paramref name="jobs"/> (one document's stage jobs; any other
    /// stage is ignored), in pipeline order.</summary>
    public static IReadOnlyList<FailedStage> FailedStages(IEnumerable<StageJobFacts> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        var result = new List<FailedStage>();

        foreach (var stage in StagedExtractionService.Stages)
        {
            var ofStage = jobs.Where(j => j.Stage == stage).ToList();
            if (ofStage.Count == 0)
            {
                continue;
            }

            var newest = ofStage
                .OrderByDescending(j => j.QueuedAt)
                .ThenBy(j => j.Status == ExtractionJobStatus.Failed ? 1 : 0)
                .First();
            if (newest.Status != ExtractionJobStatus.Failed)
            {
                continue;
            }

            result.Add(new FailedStage(
                stage,
                newest.FailureKind,
                newest.ErrorDetail,
                ofStage.Count(j => j.Status == ExtractionJobStatus.Failed),
                newest.CompletedAt ?? newest.QueuedAt));
        }

        return result;
    }

    /// <summary>The failed stages of each of <paramref name="documentIds"/> (documents with none are
    /// absent from the result). One query for the lot; tenant-scoped.</summary>
    public static async Task<Dictionary<EntityId, IReadOnlyList<FailedStage>>> LoadAsync(
        DocumentsContractsDbContext dbContext,
        TenantId tenantId,
        IReadOnlyCollection<EntityId> documentIds,
        CancellationToken cancellationToken)
    {
        if (documentIds.Count == 0)
        {
            return [];
        }

        var ids = documentIds.ToList();
        var rows = await dbContext.ExtractionJobs
            .AsNoTracking()
            .Where(j => j.TenantId == tenantId && ids.Contains(j.DocumentId) && j.Stage != ExtractionStage.Classification)
            .Select(j => new
            {
                j.DocumentId,
                j.Stage,
                j.Status,
                j.FailureKind,
                j.ErrorDetail,
                j.QueuedAt,
                j.CompletedAt,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = new Dictionary<EntityId, IReadOnlyList<FailedStage>>();
        foreach (var group in rows.Where(r => PipelineStages.Contains(r.Stage)).GroupBy(r => r.DocumentId))
        {
            var failed = FailedStages(group.Select(r => new StageJobFacts(
                r.Stage, r.Status, r.FailureKind, r.ErrorDetail, r.QueuedAt, r.CompletedAt)));
            if (failed.Count > 0)
            {
                result[group.Key] = failed;
            }
        }

        return result;
    }
}
