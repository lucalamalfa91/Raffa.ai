using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Identity.Workspace.Infrastructure;

/// <summary>Unmapped row of <c>playground_run</c> without its heavy JSON columns (snake_case aliases).</summary>
public sealed class PlaygroundRunSummaryRow
{
    public Guid Id { get; set; }
    public string Workflow { get; set; } = string.Empty;
    public string SandboxKind { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public int DurationMs { get; set; }
    public string Status { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
    public Guid? VariantId { get; set; }
    public string? VariantName { get; set; }
    public Guid? ComparisonId { get; set; }
    public string Input { get; set; } = "{}";
}

/// <summary>A stored run: the summary plus the reply (<see cref="Output"/>) and the full trace, both JSON text.</summary>
public sealed class PlaygroundRunRow
{
    public Guid Id { get; set; }
    public string Workflow { get; set; } = string.Empty;
    public string SandboxKind { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public int DurationMs { get; set; }
    public string Status { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
    public Guid? VariantId { get; set; }
    public string? VariantName { get; set; }
    public Guid? ComparisonId { get; set; }
    public string Input { get; set; } = "{}";
    public string Output { get; set; } = "{}";
    public string Trace { get; set; } = "[]";
}

/// <summary>A saved playground variant: the overrides (JSON text) a run applies on top of the production configuration.</summary>
public sealed class PlaygroundVariantRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Overrides { get; set; } = "{}";
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool Archived { get; set; }
}

/// <summary>A custom judging rubric: the criteria (JSON text) the judge scores each answer on, for one workflow.</summary>
public sealed class PlaygroundRubricRow
{
    public Guid Id { get; set; }
    public string Workflow { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Criteria { get; set; } = "[]";
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool Archived { get; set; }
}

/// <summary>One judgement: the judge's scores over the runs of a comparison, with the rubric snapshot it used.</summary>
public sealed class PlaygroundJudgementRow
{
    public Guid Id { get; set; }
    public Guid? ComparisonId { get; set; }
    public string RunIds { get; set; } = "[]";
    public string Workflow { get; set; } = string.Empty;
    public string Rubric { get; set; } = "{}";
    public string? JudgeModel { get; set; }
    public int Passes { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
    public string Result { get; set; } = "{}";
    public string Trace { get; set; } = "[]";
}

/// <summary>One export of a variant: a draft pull request on the repository, or a downloaded bundle.</summary>
public sealed class PlaygroundExportRow
{
    public Guid Id { get; set; }
    public Guid VariantId { get; set; }
    public string VariantName { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Branch { get; set; }
    public int? PrNumber { get; set; }
    public string? PrUrl { get; set; }
    public string? Error { get; set; }
    public string Files { get; set; } = "[]";
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Platform-level storage of the Super Admin playground: the sandbox flag on <c>platform_workspace</c> and
/// the <c>playground_run</c> history (see <c>AddPlaygroundTables</c>). No tenant content, no RLS. On a
/// non-Postgres provider (the in-memory test host, where these tables do not exist) a process-local copy
/// stands in so the endpoints stay testable.
/// </summary>
public sealed class PlaygroundStore(IdentityWorkspaceDbContext db)
{
    private static readonly ConcurrentDictionary<Guid, bool> LocalSandboxes = new();
    private static readonly ConcurrentDictionary<Guid, PlaygroundRunRow> LocalRuns = new();
    private static readonly ConcurrentDictionary<Guid, PlaygroundVariantRow> LocalVariants = new();
    private static readonly ConcurrentDictionary<Guid, PlaygroundRubricRow> LocalRubrics = new();
    private static readonly ConcurrentDictionary<Guid, PlaygroundJudgementRow> LocalJudgements = new();
    private static readonly ConcurrentDictionary<Guid, PlaygroundExportRow> LocalExports = new();

    private bool IsPostgres => db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Registers (or re-flags) a tenant as a playground sandbox. Idempotent.</summary>
    public async Task RegisterSandboxAsync(Guid tenantId, string name, DateTimeOffset now, CancellationToken ct = default)
    {
        if (!IsPostgres)
        {
            LocalSandboxes[tenantId] = true;
            return;
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO platform_workspace (tenant_id, name, admin_email, created_at, status, is_sandbox)
            VALUES ({tenantId}, {name}, NULL, {now.ToUniversalTime()}, 'active', true)
            ON CONFLICT (tenant_id) DO UPDATE SET is_sandbox = true
            """, ct).ConfigureAwait(false);
    }

    public async Task<bool> IsSandboxAsync(Guid tenantId, CancellationToken ct = default) =>
        !IsPostgres
            ? LocalSandboxes.ContainsKey(tenantId)
            : (await db.Database.SqlQuery<int>(
                $"SELECT 1 AS \"Value\" FROM platform_workspace WHERE tenant_id = {tenantId} AND is_sandbox")
                .ToListAsync(ct).ConfigureAwait(false)).Count > 0;

    public async Task SaveRunAsync(PlaygroundRunRow run, CancellationToken ct = default)
    {
        if (!IsPostgres)
        {
            LocalRuns[run.Id] = run;
            return;
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO playground_run (id, workflow, sandbox_kind, created_by, created_at, duration_ms, status,
                                        prompt_tokens, completion_tokens, estimated_cost_usd, variant_id, variant_name, comparison_id,
                                        input, output, trace)
            VALUES ({run.Id}, {run.Workflow}, {run.SandboxKind}, {run.CreatedBy}, {run.CreatedAt.ToUniversalTime()},
                    {run.DurationMs}, {run.Status}, {run.PromptTokens}, {run.CompletionTokens}, {run.EstimatedCostUsd},
                    {run.VariantId}, {run.VariantName}, {run.ComparisonId},
                    CAST({run.Input} AS jsonb), CAST({run.Output} AS jsonb), CAST({run.Trace} AS jsonb))
            """, ct).ConfigureAwait(false);
    }

    public async Task<List<PlaygroundRunSummaryRow>> ListRunsAsync(int limit, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 200);
        if (!IsPostgres)
        {
            return [.. LocalRuns.Values.OrderByDescending(r => r.CreatedAt).Take(limit).Select(r => new PlaygroundRunSummaryRow
            {
                Id = r.Id, Workflow = r.Workflow, SandboxKind = r.SandboxKind, CreatedBy = r.CreatedBy, CreatedAt = r.CreatedAt,
                DurationMs = r.DurationMs, Status = r.Status, PromptTokens = r.PromptTokens, CompletionTokens = r.CompletionTokens,
                EstimatedCostUsd = r.EstimatedCostUsd, VariantId = r.VariantId, VariantName = r.VariantName,
                ComparisonId = r.ComparisonId, Input = r.Input,
            })];
        }

        return await db.Database.SqlQuery<PlaygroundRunSummaryRow>(
            $"""
            SELECT id AS "id", workflow AS "workflow", sandbox_kind AS "sandbox_kind", created_by AS "created_by",
                   created_at AS "created_at", duration_ms AS "duration_ms", status AS "status",
                   prompt_tokens AS "prompt_tokens", completion_tokens AS "completion_tokens",
                   estimated_cost_usd AS "estimated_cost_usd", variant_id AS "variant_id", variant_name AS "variant_name",
                   comparison_id AS "comparison_id", input::text AS "input"
            FROM playground_run ORDER BY created_at DESC LIMIT {limit}
            """).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<PlaygroundRunRow?> GetRunAsync(Guid id, CancellationToken ct = default)
    {
        if (!IsPostgres)
        {
            return LocalRuns.GetValueOrDefault(id);
        }

        return (await db.Database.SqlQuery<PlaygroundRunRow>(
            $"""
            SELECT id AS "id", workflow AS "workflow", sandbox_kind AS "sandbox_kind", created_by AS "created_by",
                   created_at AS "created_at", duration_ms AS "duration_ms", status AS "status",
                   prompt_tokens AS "prompt_tokens", completion_tokens AS "completion_tokens",
                   estimated_cost_usd AS "estimated_cost_usd", variant_id AS "variant_id", variant_name AS "variant_name",
                   comparison_id AS "comparison_id", input::text AS "input", output::text AS "output",
                   trace::text AS "trace"
            FROM playground_run WHERE id = {id}
            """).ToListAsync(ct).ConfigureAwait(false)).FirstOrDefault();
    }

    // ----- variants -----

    public async Task<List<PlaygroundVariantRow>> ListVariantsAsync(CancellationToken ct = default)
    {
        if (!IsPostgres)
        {
            return [.. LocalVariants.Values.Where(v => !v.Archived).OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)];
        }

        return await db.Database.SqlQuery<PlaygroundVariantRow>(
            $"""
            SELECT id AS "id", name AS "name", description AS "description", overrides::text AS "overrides",
                   created_by AS "created_by", created_at AS "created_at", updated_at AS "updated_at", archived AS "archived"
            FROM playground_variant WHERE NOT archived ORDER BY lower(name)
            """).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<PlaygroundVariantRow?> GetVariantAsync(Guid id, CancellationToken ct = default)
    {
        if (!IsPostgres)
        {
            return LocalVariants.GetValueOrDefault(id) is { Archived: false } v ? v : null;
        }

        return (await db.Database.SqlQuery<PlaygroundVariantRow>(
            $"""
            SELECT id AS "id", name AS "name", description AS "description", overrides::text AS "overrides",
                   created_by AS "created_by", created_at AS "created_at", updated_at AS "updated_at", archived AS "archived"
            FROM playground_variant WHERE id = {id} AND NOT archived
            """).ToListAsync(ct).ConfigureAwait(false)).FirstOrDefault();
    }

    /// <summary>Inserts the variant, or updates name, description and overrides when it exists (not archived).</summary>
    public async Task<bool> SaveVariantAsync(PlaygroundVariantRow variant, bool create, CancellationToken ct = default)
    {
        if (!IsPostgres)
        {
            if (create)
            {
                LocalVariants[variant.Id] = variant;
                return true;
            }

            if (LocalVariants.TryGetValue(variant.Id, out var existing) && !existing.Archived)
            {
                existing.Name = variant.Name;
                existing.Description = variant.Description;
                existing.Overrides = variant.Overrides;
                existing.UpdatedAt = variant.UpdatedAt;
                return true;
            }

            return false;
        }

        if (create)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO playground_variant (id, name, description, overrides, created_by, created_at, updated_at, archived)
                VALUES ({variant.Id}, {variant.Name}, {variant.Description}, CAST({variant.Overrides} AS jsonb), {variant.CreatedBy},
                        {variant.CreatedAt.ToUniversalTime()}, {variant.UpdatedAt.ToUniversalTime()}, false)
                """, ct).ConfigureAwait(false);
            return true;
        }

        return await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE playground_variant
            SET name = {variant.Name}, description = {variant.Description}, overrides = CAST({variant.Overrides} AS jsonb),
                updated_at = {variant.UpdatedAt.ToUniversalTime()}
            WHERE id = {variant.Id} AND NOT archived
            """, ct).ConfigureAwait(false) == 1;
    }

    /// <summary>Archives (hides) a variant; runs that used it keep their own copy of its overrides.</summary>
    public async Task<bool> ArchiveVariantAsync(Guid id, CancellationToken ct = default)
    {
        if (!IsPostgres)
        {
            if (LocalVariants.TryGetValue(id, out var v) && !v.Archived)
            {
                v.Archived = true;
                return true;
            }

            return false;
        }

        return await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE playground_variant SET archived = true WHERE id = {id} AND NOT archived", ct).ConfigureAwait(false) == 1;
    }

    // ----- runs of a comparison -----

    /// <summary>The stored runs with these ids (any order), or all the runs of <paramref name="comparisonId"/>.</summary>
    public async Task<List<PlaygroundRunRow>> GetRunsAsync(IReadOnlyCollection<Guid> ids, Guid? comparisonId, CancellationToken ct = default)
    {
        if (!IsPostgres)
        {
            return [.. LocalRuns.Values.Where(r => ids.Contains(r.Id) || (comparisonId is not null && r.ComparisonId == comparisonId)).OrderBy(r => r.CreatedAt)];
        }

        // Two plain queries instead of one with optional parameters: no untyped NULL parameter reaches Postgres.
        if (comparisonId is { } comparison)
        {
            return await db.Database.SqlQuery<PlaygroundRunRow>(
                $"""
                SELECT id AS "id", workflow AS "workflow", sandbox_kind AS "sandbox_kind", created_by AS "created_by",
                       created_at AS "created_at", duration_ms AS "duration_ms", status AS "status",
                       prompt_tokens AS "prompt_tokens", completion_tokens AS "completion_tokens",
                       estimated_cost_usd AS "estimated_cost_usd", variant_id AS "variant_id", variant_name AS "variant_name",
                       comparison_id AS "comparison_id", input::text AS "input", output::text AS "output", trace::text AS "trace"
                FROM playground_run WHERE comparison_id = {comparison} ORDER BY created_at
                """).ToListAsync(ct).ConfigureAwait(false);
        }

        var idArray = ids.ToArray();
        return await db.Database.SqlQuery<PlaygroundRunRow>(
            $"""
            SELECT id AS "id", workflow AS "workflow", sandbox_kind AS "sandbox_kind", created_by AS "created_by",
                   created_at AS "created_at", duration_ms AS "duration_ms", status AS "status",
                   prompt_tokens AS "prompt_tokens", completion_tokens AS "completion_tokens",
                   estimated_cost_usd AS "estimated_cost_usd", variant_id AS "variant_id", variant_name AS "variant_name",
                   comparison_id AS "comparison_id", input::text AS "input", output::text AS "output", trace::text AS "trace"
            FROM playground_run WHERE id = ANY({idArray}) ORDER BY created_at
            """).ToListAsync(ct).ConfigureAwait(false);
    }

    // ----- rubrics -----

    public async Task<List<PlaygroundRubricRow>> ListRubricsAsync(CancellationToken ct = default)
    {
        if (!IsPostgres)
        {
            return [.. LocalRubrics.Values.Where(r => !r.Archived).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];
        }

        return await db.Database.SqlQuery<PlaygroundRubricRow>(
            $"""
            SELECT id AS "id", workflow AS "workflow", name AS "name", criteria::text AS "criteria", created_by AS "created_by",
                   created_at AS "created_at", updated_at AS "updated_at", archived AS "archived"
            FROM playground_rubric WHERE NOT archived ORDER BY lower(name)
            """).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<PlaygroundRubricRow?> GetRubricAsync(Guid id, CancellationToken ct = default)
    {
        if (!IsPostgres)
        {
            return LocalRubrics.GetValueOrDefault(id) is { Archived: false } r ? r : null;
        }

        return (await db.Database.SqlQuery<PlaygroundRubricRow>(
            $"""
            SELECT id AS "id", workflow AS "workflow", name AS "name", criteria::text AS "criteria", created_by AS "created_by",
                   created_at AS "created_at", updated_at AS "updated_at", archived AS "archived"
            FROM playground_rubric WHERE id = {id} AND NOT archived
            """).ToListAsync(ct).ConfigureAwait(false)).FirstOrDefault();
    }

    public async Task<bool> SaveRubricAsync(PlaygroundRubricRow rubric, bool create, CancellationToken ct = default)
    {
        if (!IsPostgres)
        {
            if (create)
            {
                LocalRubrics[rubric.Id] = rubric;
                return true;
            }

            if (LocalRubrics.TryGetValue(rubric.Id, out var existing) && !existing.Archived)
            {
                existing.Workflow = rubric.Workflow;
                existing.Name = rubric.Name;
                existing.Criteria = rubric.Criteria;
                existing.UpdatedAt = rubric.UpdatedAt;
                return true;
            }

            return false;
        }

        if (create)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO playground_rubric (id, workflow, name, criteria, created_by, created_at, updated_at, archived)
                VALUES ({rubric.Id}, {rubric.Workflow}, {rubric.Name}, CAST({rubric.Criteria} AS jsonb), {rubric.CreatedBy},
                        {rubric.CreatedAt.ToUniversalTime()}, {rubric.UpdatedAt.ToUniversalTime()}, false)
                """, ct).ConfigureAwait(false);
            return true;
        }

        return await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE playground_rubric
            SET workflow = {rubric.Workflow}, name = {rubric.Name}, criteria = CAST({rubric.Criteria} AS jsonb),
                updated_at = {rubric.UpdatedAt.ToUniversalTime()}
            WHERE id = {rubric.Id} AND NOT archived
            """, ct).ConfigureAwait(false) == 1;
    }

    public async Task<bool> ArchiveRubricAsync(Guid id, CancellationToken ct = default)
    {
        if (!IsPostgres)
        {
            if (LocalRubrics.TryGetValue(id, out var r) && !r.Archived)
            {
                r.Archived = true;
                return true;
            }

            return false;
        }

        return await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE playground_rubric SET archived = true WHERE id = {id} AND NOT archived", ct).ConfigureAwait(false) == 1;
    }

    // ----- judgements -----

    public async Task SaveJudgementAsync(PlaygroundJudgementRow j, CancellationToken ct = default)
    {
        if (!IsPostgres)
        {
            LocalJudgements[j.Id] = j;
            return;
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO playground_judgement (id, comparison_id, run_ids, workflow, rubric, judge_model, passes, created_by, created_at,
                                              prompt_tokens, completion_tokens, estimated_cost_usd, result, trace)
            VALUES ({j.Id}, {j.ComparisonId}, CAST({j.RunIds} AS jsonb), {j.Workflow}, CAST({j.Rubric} AS jsonb), {j.JudgeModel}, {j.Passes},
                    {j.CreatedBy}, {j.CreatedAt.ToUniversalTime()}, {j.PromptTokens}, {j.CompletionTokens}, {j.EstimatedCostUsd},
                    CAST({j.Result} AS jsonb), CAST({j.Trace} AS jsonb))
            """, ct).ConfigureAwait(false);
    }

    /// <summary>Judgements of a comparison, or those whose runs include <paramref name="runId"/>, newest first.</summary>
    public async Task<List<PlaygroundJudgementRow>> ListJudgementsAsync(Guid? comparisonId, Guid? runId, CancellationToken ct = default)
    {
        if (!IsPostgres)
        {
            return [.. LocalJudgements.Values
                .Where(j => (comparisonId is not null && j.ComparisonId == comparisonId) || (runId is not null && j.RunIds.Contains(runId.Value.ToString(), StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(j => j.CreatedAt)];
        }

        const int Limit = 50;
        if (comparisonId is { } comparison)
        {
            return await db.Database.SqlQuery<PlaygroundJudgementRow>(
                $"""
                SELECT id AS "id", comparison_id AS "comparison_id", run_ids::text AS "run_ids", workflow AS "workflow", rubric::text AS "rubric",
                       judge_model AS "judge_model", passes AS "passes", created_by AS "created_by", created_at AS "created_at",
                       prompt_tokens AS "prompt_tokens", completion_tokens AS "completion_tokens", estimated_cost_usd AS "estimated_cost_usd",
                       result::text AS "result", trace::text AS "trace"
                FROM playground_judgement WHERE comparison_id = {comparison} ORDER BY created_at DESC LIMIT {Limit}
                """).ToListAsync(ct).ConfigureAwait(false);
        }

        var runText = runId?.ToString() ?? string.Empty;
        return await db.Database.SqlQuery<PlaygroundJudgementRow>(
            $"""
            SELECT id AS "id", comparison_id AS "comparison_id", run_ids::text AS "run_ids", workflow AS "workflow", rubric::text AS "rubric",
                   judge_model AS "judge_model", passes AS "passes", created_by AS "created_by", created_at AS "created_at",
                   prompt_tokens AS "prompt_tokens", completion_tokens AS "completion_tokens", estimated_cost_usd AS "estimated_cost_usd",
                   result::text AS "result", trace::text AS "trace"
            FROM playground_judgement WHERE run_ids @> to_jsonb({runText}::text) ORDER BY created_at DESC LIMIT {Limit}
            """).ToListAsync(ct).ConfigureAwait(false);
    }

    // ----- exports -----

    public async Task SaveExportAsync(PlaygroundExportRow e, CancellationToken ct = default)
    {
        if (!IsPostgres)
        {
            LocalExports[e.Id] = e;
            return;
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO playground_export (id, variant_id, variant_name, target, status, branch, pr_number, pr_url, error, files, created_by, created_at)
            VALUES ({e.Id}, {e.VariantId}, {e.VariantName}, {e.Target}, {e.Status}, {e.Branch}, {e.PrNumber}, {e.PrUrl}, {e.Error},
                    CAST({e.Files} AS jsonb), {e.CreatedBy}, {e.CreatedAt.ToUniversalTime()})
            """, ct).ConfigureAwait(false);
    }

    public async Task<List<PlaygroundExportRow>> ListExportsAsync(Guid variantId, CancellationToken ct = default)
    {
        if (!IsPostgres)
        {
            return [.. LocalExports.Values.Where(e => e.VariantId == variantId).OrderByDescending(e => e.CreatedAt)];
        }

        return await db.Database.SqlQuery<PlaygroundExportRow>(
            $"""
            SELECT id AS "id", variant_id AS "variant_id", variant_name AS "variant_name", target AS "target", status AS "status",
                   branch AS "branch", pr_number AS "pr_number", pr_url AS "pr_url", error AS "error", files::text AS "files",
                   created_by AS "created_by", created_at AS "created_at"
            FROM playground_export WHERE variant_id = {variantId} ORDER BY created_at DESC LIMIT 50
            """).ToListAsync(ct).ConfigureAwait(false);
    }
}
