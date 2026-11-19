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
}
