using Raffa.Renewals.Domain;
using Raffa.Renewals.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Renewals.Application;

/// <summary>
/// Implements task E29/F01/US01/T01 (todo-entity-api; parent story us-01-todo-entity-api; wave w19
/// NW-85; ADR-028/ADR-009/ADR-011/ADR-003 w19): the idempotent upsert
/// <c>Raffa.Api.AskCopilotService</c> calls in-process after ranking and before the answer
/// (epic-29/feature-02), plus the read and the tick `GET`/`PUT /api/renewals/{id}/negotiation-todos`
/// map onto (`Raffa.Api.RenewalsEndpointExtensions`).
///
/// <para>
/// Same shape as <c>RenewalActionService</c>: owns its own tenant scope
/// (<see cref="ITenantContext.BeginScope"/>) rather than trusting one is already active, validates
/// every field before writing anything, and writes one append-only <see cref="IAuditWriter"/> entry
/// per successful write call (spec §14.1; Appendix C rule 9; parent story AC-3) with
/// <see cref="AuditEntry.Actor"/> the caller's resolved token subject — never a default, never the
/// model (ADR-011 w16 §15 "an audit row that cannot name its actor is not written").
/// <see cref="IAuditWriter"/> lives in <c>Raffa.SharedKernel</c>, so depending on it does not cross
/// the ADR-002 module boundary (this module's allow-list is <c>[SharedKernel, Benchmark]</c>).
/// </para>
///
/// <para>
/// <b>The idempotent-upsert rule, precisely (task's own words: "same point_key updates
/// current/target/rationale/rank; never un-ticks Done; vanished → Superseded")</b>:
/// <see cref="UpsertAsync"/> is a full-set reconciliation, called once per Ask turn with every
/// point the ranker currently surfaces for one contract. For each incoming point:
/// <list type="bullet">
/// <item>no existing row for that <see cref="RenewalNegotiationTodo.PointKey"/> → a new row,
/// <see cref="RenewalNegotiationTodoStatus.Open"/>;</item>
/// <item>an existing, non-<see cref="RenewalNegotiationTodoStatus.Done"/> row → its
/// <see cref="RenewalNegotiationTodo.Rank"/>/<see cref="RenewalNegotiationTodo.Current"/>/
/// <see cref="RenewalNegotiationTodo.Target"/>/<see cref="RenewalNegotiationTodo.Rationale"/>/
/// <see cref="RenewalNegotiationTodo.CitationKeys"/> are refreshed and its status becomes (or
/// stays) <see cref="RenewalNegotiationTodoStatus.Open"/> — this is how a point that had gone
/// <see cref="RenewalNegotiationTodoStatus.Superseded"/> comes back if the ranker surfaces it
/// again. <see cref="RenewalNegotiationTodo.Topic"/> is <b>not</b> rewritten (see that field's own
/// doc comment: the task's own field list names only current/target/rationale/rank; a point's
/// category-derived label does not need to track a changing market fact);</item>
/// <item>an existing <see cref="RenewalNegotiationTodoStatus.Done"/> row → left completely
/// untouched, content and status both. This is a deliberately stronger guarantee than the literal
/// "never un-ticks Done": freezing the whole row (not just its status) means a completed
/// negotiation point is never silently rewritten under a user who already closed it out.</item>
/// </list>
/// Any existing row whose <see cref="RenewalNegotiationTodo.PointKey"/> is <b>not</b> in this call's
/// point set becomes <see cref="RenewalNegotiationTodoStatus.Superseded"/> — unless it is already
/// <see cref="RenewalNegotiationTodoStatus.Done"/> (frozen, per above) or already
/// <see cref="RenewalNegotiationTodoStatus.Superseded"/> (no-op). "Vanished" is evaluated per
/// contract, against this one call's point set — <see cref="UpsertAsync"/> is always given the
/// <em>whole</em> ranked set for the contract it is called with (never a partial page), so a point
/// legitimately absent from that set is a point the ranker no longer grounds.
/// </para>
/// </summary>
public sealed class RenewalNegotiationTodoService(
    RenewalsDbContext dbContext, ITenantContext tenantContext, IClock clock, IAuditWriter auditWriter)
{
    /// <summary><see cref="RenewalNegotiationTodo.Source"/> for every row this service creates —
    /// see that field's own doc comment.</summary>
    public const string AskSource = "ask";

    /// <summary><see cref="AuditEntry.Action"/> for every successful write (upsert or tick) — one
    /// action name for "this table changed", matching the parent story AC-3 wording verbatim
    /// ("the write is audited renewal.negotiation_todos_written").</summary>
    private const string AuditWrittenAction = "renewal.negotiation_todos_written";

    /// <summary><see cref="AuditEntry.ResourceType"/> — lowercase, matching
    /// <c>RenewalActionService</c>'s own <c>"renewal"</c> convention (this table's rows describe
    /// facts about a renewal, the same as <c>RenewalAction</c>'s).</summary>
    private const string AuditResourceType = "renewal";

    public static string PointsRequiredError { get; } = "At least one negotiation point is required.";

    public static string DuplicatePointKeyError { get; } =
        "'points' contains more than one entry for the same pointKey.";

    public static string PointKeyRequiredError { get; } = "'pointKey' is required.";

    public static string TopicRequiredError { get; } = "'topic' is required.";

    public static string CurrentRequiredError { get; } = "'current' is required.";

    public static string TargetRequiredError { get; } = "'target' is required.";

    public static string RationaleRequiredError { get; } = "'rationale' is required.";

    /// <summary>
    /// Reconciles the whole ranked point set for (<paramref name="tenantId"/>,
    /// <paramref name="contractId"/>) — see this type's own doc comment for the exact per-point
    /// rule. Validation runs before any query or write (same "phase 1: validate everything, phase 2:
    /// mutate" discipline <c>ContractCorrectionService.CorrectAsync</c>/<c>RenewalActionService
    /// .SetActionAsync</c> already follow), so an invalid call leaves the database untouched.
    /// </summary>
    /// <param name="points">The contract's whole ranked set for this call — never a partial page
    /// (see this type's own doc comment on why "vanished" is evaluated against this exact
    /// collection). Must be non-empty with no repeated <see cref="RenewalNegotiationTodoPoint.PointKey"/>.</param>
    /// <param name="actor">The caller's resolved token subject (ADR-011 w16 §15) — required, no
    /// default, so a placeholder can never return by omission. Recorded on the
    /// <c>renewal.negotiation_todos_written</c> audit entry.</param>
    /// <returns>The contract's complete, current set of TODO rows after reconciliation (every row
    /// this call touched, left untouched, or left frozen) — ordered by <c>Rank</c> then
    /// <c>PointKey</c>, the same order <see cref="GetAsync"/> returns.</returns>
    public async Task<Result<IReadOnlyList<RenewalNegotiationTodoResult>>> UpsertAsync(
        TenantId tenantId,
        EntityId contractId,
        IReadOnlyCollection<RenewalNegotiationTodoPoint> points,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count == 0)
        {
            return Result<IReadOnlyList<RenewalNegotiationTodoResult>>.Failure(PointsRequiredError);
        }

        foreach (var point in points)
        {
            if (string.IsNullOrWhiteSpace(point.PointKey))
            {
                return Result<IReadOnlyList<RenewalNegotiationTodoResult>>.Failure(PointKeyRequiredError);
            }

            if (string.IsNullOrWhiteSpace(point.Topic))
            {
                return Result<IReadOnlyList<RenewalNegotiationTodoResult>>.Failure(TopicRequiredError);
            }

            if (string.IsNullOrWhiteSpace(point.Current))
            {
                return Result<IReadOnlyList<RenewalNegotiationTodoResult>>.Failure(CurrentRequiredError);
            }

            if (string.IsNullOrWhiteSpace(point.Target))
            {
                return Result<IReadOnlyList<RenewalNegotiationTodoResult>>.Failure(TargetRequiredError);
            }

            if (string.IsNullOrWhiteSpace(point.Rationale))
            {
                return Result<IReadOnlyList<RenewalNegotiationTodoResult>>.Failure(RationaleRequiredError);
            }
        }

        if (points.Select(p => p.PointKey).Distinct(StringComparer.Ordinal).Count() != points.Count)
        {
            return Result<IReadOnlyList<RenewalNegotiationTodoResult>>.Failure(DuplicatePointKeyError);
        }

        // Entry point: open this call's own tenant scope before any query below, since the RLS
        // connection interceptor reads ITenantContext.Current only when the connection opens, which
        // EF Core does lazily on first use (same placement as RenewalActionService.SetActionAsync).
        using var _ = tenantContext.BeginScope(tenantId);

        var existingRows = await dbContext.RenewalNegotiationTodos
            .Where(t => t.TenantId == tenantId && t.ContractId == contractId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingByKey = existingRows.ToDictionary(t => t.PointKey, StringComparer.Ordinal);
        var incomingKeys = new HashSet<string>(points.Select(p => p.PointKey), StringComparer.Ordinal);
        var now = clock.UtcNow;
        var newlyCreated = new List<RenewalNegotiationTodo>();
        var upsertedCount = 0;
        var supersededCount = 0;

        foreach (var point in points)
        {
            if (existingByKey.TryGetValue(point.PointKey, out var existing))
            {
                if (existing.Status == RenewalNegotiationTodoStatus.Done)
                {
                    continue; // frozen -- see this type's own doc comment.
                }

                existing.Rank = point.Rank;
                existing.Current = point.Current;
                existing.Target = point.Target;
                existing.Rationale = point.Rationale;
                existing.CitationKeys = point.CitationKeys;
                existing.Status = RenewalNegotiationTodoStatus.Open;
                existing.UpdatedAt = now;
                upsertedCount++;
            }
            else
            {
                var created = new RenewalNegotiationTodo
                {
                    TenantId = tenantId,
                    ContractId = contractId,
                    PointKey = point.PointKey,
                    Topic = point.Topic,
                    Rank = point.Rank,
                    Current = point.Current,
                    Target = point.Target,
                    Rationale = point.Rationale,
                    CitationKeys = point.CitationKeys,
                    Source = AskSource,
                    Status = RenewalNegotiationTodoStatus.Open,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                dbContext.RenewalNegotiationTodos.Add(created);
                newlyCreated.Add(created);
                upsertedCount++;
            }
        }

        foreach (var existing in existingRows)
        {
            if (existing.Status == RenewalNegotiationTodoStatus.Done
                || existing.Status == RenewalNegotiationTodoStatus.Superseded
                || incomingKeys.Contains(existing.PointKey))
            {
                continue;
            }

            existing.Status = RenewalNegotiationTodoStatus.Superseded;
            existing.UpdatedAt = now;
            supersededCount++;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Recorded only once the reconciliation itself is durable, still inside this call's own
        // tenant scope (same placement as RenewalActionService.SetActionAsync's own "write then
        // audit entry" ordering). One entry per call, not per row -- same "one audit entry per
        // successful call" convention RenewalActionService already establishes.
        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                actor,
                AuditWrittenAction,
                AuditResourceType,
                contractId.Value.ToString(),
                now,
                $"upserted={upsertedCount} superseded={supersededCount}"),
            cancellationToken).ConfigureAwait(false);

        var all = existingRows
            .Concat(newlyCreated)
            .OrderBy(t => t.Rank)
            .ThenBy(t => t.PointKey, StringComparer.Ordinal)
            .Select(ToResult)
            .ToList();

        return Result<IReadOnlyList<RenewalNegotiationTodoResult>>.Success(all);
    }

    /// <summary>
    /// `GET /api/renewals/{id}/negotiation-todos` (parent story AC-2). Every row for this contract —
    /// <see cref="RenewalNegotiationTodoStatus.Open"/>, <see cref="RenewalNegotiationTodoStatus.Done"/>
    /// and <see cref="RenewalNegotiationTodoStatus.Superseded"/> alike (client-architect: read-back;
    /// the client decides what to render, this service does not filter by status) — ordered by
    /// <see cref="RenewalNegotiationTodo.Rank"/> then <see cref="RenewalNegotiationTodo.PointKey"/>
    /// (a stable deterministic tie-break, same reasoning <see cref="EntityId.CompareTo"/>'s own doc
    /// comment gives). An empty list when nothing was ever upserted for this contract — never a 404
    /// at this layer (the endpoint owns that decision for a malformed/cross-tenant id; this service
    /// cannot and does not distinguish "no rows yet" from "wrong id", same as
    /// <c>RenewalActionService.GetActionAsync</c>'s own doc comment for the identical gap).
    /// </summary>
    public async Task<IReadOnlyList<RenewalNegotiationTodoResult>> GetAsync(
        TenantId tenantId, EntityId contractId, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);

        var rows = await dbContext.RenewalNegotiationTodos
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.ContractId == contractId)
            .OrderBy(t => t.Rank)
            .ThenBy(t => t.PointKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(ToResult).ToList();
    }

    /// <summary>
    /// `PUT /api/renewals/{id}/negotiation-todos` (parent story AC-2, the tick) — marks one existing
    /// row <see cref="RenewalNegotiationTodoStatus.Done"/>, unconditionally (an already-
    /// <see cref="RenewalNegotiationTodoStatus.Done"/> or previously
    /// <see cref="RenewalNegotiationTodoStatus.Superseded"/> row both become/stay
    /// <see cref="RenewalNegotiationTodoStatus.Done"/> — there is no "un-tick" route, matching the
    /// task's own "a tick").
    /// </summary>
    /// <param name="pointKey">Which row to tick. Validated non-blank before any query.</param>
    /// <param name="actor">The caller's resolved token subject — same required-parameter contract as
    /// <see cref="UpsertAsync"/>'s own <c>actor</c>.</param>
    /// <returns>
    /// <see cref="Result{T}.Failure"/> for a blank <paramref name="pointKey"/> (a malformed request —
    /// the endpoint maps this to 400); on success, <see langword="null"/> when no row exists for
    /// (<paramref name="tenantId"/>, <paramref name="contractId"/>, <paramref name="pointKey"/>) —
    /// this service never creates one to satisfy a tick (client-architect's "never invent a point";
    /// the endpoint maps this to 404) — otherwise the ticked row. Same "<see cref="Result{T}"/> whose
    /// success value can itself be null" shape <c>Raffa.AiGateway.Foundry.FoundryOcrClient.AnalyzeAsync</c>
    /// already uses for an analogous "valid request, nothing found" outcome.
    /// </returns>
    public async Task<Result<RenewalNegotiationTodoResult?>> SetDoneAsync(
        TenantId tenantId,
        EntityId contractId,
        string? pointKey,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        if (string.IsNullOrWhiteSpace(pointKey))
        {
            return Result<RenewalNegotiationTodoResult?>.Failure(PointKeyRequiredError);
        }

        using var _ = tenantContext.BeginScope(tenantId);

        var existing = await dbContext.RenewalNegotiationTodos
            .SingleOrDefaultAsync(
                t => t.TenantId == tenantId && t.ContractId == contractId && t.PointKey == pointKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return Result<RenewalNegotiationTodoResult?>.Success(null);
        }

        var now = clock.UtcNow;
        existing.Status = RenewalNegotiationTodoStatus.Done;
        existing.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                actor,
                AuditWrittenAction,
                AuditResourceType,
                contractId.Value.ToString(),
                now,
                $"pointKey={pointKey} status=Done"),
            cancellationToken).ConfigureAwait(false);

        return Result<RenewalNegotiationTodoResult?>.Success(ToResult(existing));
    }

    private static RenewalNegotiationTodoResult ToResult(RenewalNegotiationTodo todo) => new(
        todo.ContractId,
        todo.PointKey,
        todo.Topic,
        todo.Rank,
        todo.Current,
        todo.Target,
        todo.Rationale,
        todo.CitationKeys,
        todo.Source,
        todo.Status,
        todo.CreatedAt,
        todo.UpdatedAt);
}
