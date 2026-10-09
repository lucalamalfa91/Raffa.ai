using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// F5-T02 / F5-D03: which stages of a document's staged extraction currently stand failed. A document
/// with at least one such stage is <em>partial</em>: it holds the facts the other stages produced, is
/// never <c>Completed</c> (not by the pipeline, not by <see cref="NothingToReviewAutoValidator"/>),
/// and the failed stage alone is what a retry runs again.
/// A stage "stands failed" when its newest job (by <see cref="ExtractionJob.QueuedAt"/>; a finished job
/// wins a tie, because a retry only ever follows a failure) is <see cref="ExtractionJobStatus.Failed"/>.
/// A stage with no job, or whose newest job is still running, is not reported.
/// </summary>
public static class ExtractionPartialState
{
    /// <summary>One failed stage, with what a retry policy needs to know about it.</summary>
    /// <param name="FailedAttempts">How many jobs of this stage have failed, over every run of the
    /// document: the bound on automatic retries.</param>
    /// <param name="LastFailedAt">When the newest failed job ended (or was queued, if it never ended).</param>
    public sealed record FailedStage(
        ExtractionStage Stage,
        ExtractionStageFailureKind? Kind,
        int FailedAttempts,
        DateTimeOffset LastFailedAt);

    /// <summary>The failed stages of each of <paramref name="documentIds"/>, in pipeline order
    /// (documents with none are absent). One query for the lot; tenant-scoped.</summary>
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
        var jobs = await dbContext.ExtractionJobs
            .AsNoTracking()
            .Where(j => j.TenantId == tenantId && ids.Contains(j.DocumentId) && j.Stage != ExtractionStage.Classification)
            .Select(j => new { j.DocumentId, j.Stage, j.Status, j.FailureKind, j.QueuedAt, j.CompletedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = new Dictionary<EntityId, IReadOnlyList<FailedStage>>();
        foreach (var document in jobs.GroupBy(j => j.DocumentId))
        {
            var failed = new List<FailedStage>();
            foreach (var stage in StagedExtractionService.Stages)
            {
                var ofStage = document.Where(j => j.Stage == stage).ToList();
                var newest = ofStage
                    .OrderByDescending(j => j.QueuedAt)
                    .ThenBy(j => j.Status == ExtractionJobStatus.Failed ? 1 : 0)
                    .FirstOrDefault();
                if (newest?.Status == ExtractionJobStatus.Failed)
                {
                    failed.Add(new FailedStage(
                        stage,
                        newest.FailureKind,
                        ofStage.Count(j => j.Status == ExtractionJobStatus.Failed),
                        newest.CompletedAt ?? newest.QueuedAt));
                }
            }

            if (failed.Count > 0)
            {
                result[document.Key] = failed;
            }
        }

        return result;
    }
}
