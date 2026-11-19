using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiGateway.Telemetry;
using Raffa.Chat.Application.Reply;
using Raffa.Api.Infrastructure;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Api;

/// <summary>
/// The Super Admin agent playground (docs/architecture/admin-agent-playground-plan.md, PR 1): run an Ask turn on
/// one of three synthetic sandbox workspaces and get back the reply together with the full trace of what the
/// agent did (every model call, tool call and guard). Guarded exactly as <c>/api/admin/*</c>: 401 anonymous,
/// 404 for anyone who is not a platform admin. The run executes under the sandbox tenant's own RLS scope; the
/// platform admin is never a member of it and no customer workspace can be selected.
/// </summary>
public static class PlaygroundEndpointExtensions
{
    private const int MaxHistoryTurns = 20;
    private const int MaxQuestionChars = 4000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapPlaygroundEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/playground/sandboxes", (ICallerIdentity id, PlatformAdminGuard guard, PlaygroundSandboxSeeder seeder, CancellationToken ct) =>
            RunAsync(id, guard, () => SandboxesAsync(seeder, ct)));
        endpoints.MapPost("/api/admin/playground/runs", (PlaygroundRunRequest? body, ICallerIdentity id, PlatformAdminGuard guard, PlaygroundSandboxSeeder seeder,
                PlaygroundStore store, AskCopilotService ask, ITenantContext tenantContext, IClock clock, IOptions<PlaygroundPricingOptions> pricing, CancellationToken ct) =>
            RunAsync(id, guard, () => StartRunAsync(body, id.Resolve()!, seeder, store, ask, tenantContext, clock, pricing.Value, ct)));
        endpoints.MapGet("/api/admin/playground/runs", (int? limit, ICallerIdentity id, PlatformAdminGuard guard, PlaygroundStore store, CancellationToken ct) =>
            RunAsync(id, guard, () => ListRunsAsync(store, limit ?? 50, ct)));
        endpoints.MapGet("/api/admin/playground/runs/{runId:guid}", (Guid runId, ICallerIdentity id, PlatformAdminGuard guard, PlaygroundStore store, CancellationToken ct) =>
            RunAsync(id, guard, () => GetRunAsync(store, runId, ct)));
        return endpoints;
    }

    public sealed record PlaygroundHistoryTurn(string? Role, string? Markdown);

    public sealed record PlaygroundRunRequest(string? Sandbox, string? Question, IReadOnlyList<PlaygroundHistoryTurn>? History);

    private static async Task<IResult> RunAsync(ICallerIdentity callerIdentity, PlatformAdminGuard guard, Func<Task<IResult>> action)
    {
        if (callerIdentity.Resolve() is null)
        {
            return Results.Unauthorized();
        }

        return guard.IsPlatformAdmin() ? await action().ConfigureAwait(false) : Results.NotFound();
    }

    private static async Task<IResult> SandboxesAsync(PlaygroundSandboxSeeder seeder, CancellationToken ct)
    {
        var sandboxes = new List<object>();
        foreach (var kind in PlaygroundSandboxKinds.All)
        {
            await seeder.EnsureAsync(kind, ct).ConfigureAwait(false);
            sandboxes.Add(new
            {
                kind,
                tenantId = PlaygroundSandboxKinds.TenantOf(kind)!.Value.Value,
                description = PlaygroundSandboxKinds.Describe(kind),
                contracts = await seeder.CountContractsAsync(kind, ct).ConfigureAwait(false),
            });
        }

        return Results.Ok(new { sandboxes });
    }

    private static async Task<IResult> StartRunAsync(
        PlaygroundRunRequest? body, string actor, PlaygroundSandboxSeeder seeder, PlaygroundStore store, AskCopilotService ask,
        ITenantContext tenantContext, IClock clock, PlaygroundPricingOptions pricing, CancellationToken ct)
    {
        if (PlaygroundSandboxKinds.TenantOf(body?.Sandbox) is not { } tenantId)
        {
            return Results.BadRequest($"'sandbox' must be one of: {string.Join(", ", PlaygroundSandboxKinds.All)}.");
        }

        var question = body?.Question?.Trim();
        if (string.IsNullOrEmpty(question) || question.Length > MaxQuestionChars)
        {
            return Results.BadRequest($"'question' is required and at most {MaxQuestionChars} characters.");
        }

        // Defence in depth: whatever the kind maps to, the tenant must be registered as a sandbox.
        await seeder.EnsureAsync(body!.Sandbox!, ct).ConfigureAwait(false);
        if (!await store.IsSandboxAsync(tenantId.Value, ct).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        var history = (body.History ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t.Markdown))
            .TakeLast(MaxHistoryTurns)
            .Select(t => (Role: string.Equals(t.Role, "raffa", StringComparison.OrdinalIgnoreCase) ? "raffa" : "you", Markdown: t.Markdown!))
            .ToList();

        var runId = Guid.NewGuid();
        var startedAt = clock.UtcNow;
        var clock2 = Stopwatch.StartNew();
        CopilotReply? copilotReply = null;
        string status = "ok";
        string? error = null;
        PlaygroundTraceCollector collector;
        using (var tenantScope = tenantContext.BeginScope(tenantId))
        using (var traceScope = PlaygroundTrace.Begin(out collector))
        {
            try
            {
                copilotReply = await ask.AskAsync(tenantId, question, history, actor, cancellationToken: ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                status = "error";
                error = ex.GetType().Name + ": " + ex.Message;
                collector.Add("note", "run-failed", 0, ok: false, note: error);
            }
        }

        clock2.Stop();
        var reply = copilotReply is null ? null : ToReplyJson(copilotReply);
        var events = collector.Events;
        var calls = events.Where(e => e.Kind == "model_call" && e.Ok).ToList();
        var prompt = calls.Sum(e => e.PromptTokens ?? 0);
        var completion = calls.Sum(e => e.CompletionTokens ?? 0);
        var cached = calls.Sum(e => e.CachedTokens ?? 0);
        var priced = calls.Select(e => (Cost: pricing.CostUsd(e.Model, e.PromptTokens ?? 0, e.CompletionTokens ?? 0, e.CachedTokens ?? 0), e.Model)).ToList();
        decimal? cost = priced.Count > 0 && priced.All(p => p.Cost is not null) ? priced.Sum(p => p.Cost!.Value) : (priced.Count == 0 ? 0m : null);
        var unpriced = priced.Where(p => p.Cost is null).Select(p => p.Model ?? "?").Distinct().ToList();

        var usage = new
        {
            promptTokens = prompt,
            completionTokens = completion,
            cachedTokens = cached,
            estimatedCostUsd = cost,
            unpricedModels = unpriced,
            modelCalls = calls.Count,
            toolCalls = events.Count(e => e.Kind == "tool_call"),
        };

        await store.SaveRunAsync(new PlaygroundRunRow
        {
            Id = runId,
            Workflow = "ask",
            SandboxKind = body.Sandbox!,
            CreatedBy = actor,
            CreatedAt = startedAt,
            DurationMs = (int)Math.Min(int.MaxValue, clock2.ElapsedMilliseconds),
            Status = status,
            PromptTokens = prompt,
            CompletionTokens = completion,
            EstimatedCostUsd = cost,
            Input = JsonSerializer.Serialize(new { question, history = history.Select(h => new { role = h.Role, markdown = h.Markdown }) }, Json),
            Output = JsonSerializer.Serialize(new { reply, error, usage }, Json),
            Trace = JsonSerializer.Serialize(events, Json),
        }, ct).ConfigureAwait(false);

        return Results.Ok(new { runId, sandbox = body.Sandbox, status, error, durationMs = clock2.ElapsedMilliseconds, reply, usage, trace = events });
    }

    /// <summary>The reply as the Ask endpoint puts it on the wire (kind as its API literal, same citation/action shapes).</summary>
    private static object ToReplyJson(CopilotReply reply) => new
    {
        kind = reply.Kind.ToApiValue(),
        answerMarkdown = reply.AnswerMarkdown,
        citations = reply.Citations.Select(ConversationsEndpointExtensions.ToCitationJson),
        actions = reply.Actions.Select(ConversationsEndpointExtensions.ToActionJson),
        provenance = new
        {
            sources = reply.Provenance.Sources,
            modelId = reply.Provenance.ModelId,
            promptVersion = reply.Provenance.PromptVersion,
            inputHash = reply.Provenance.InputHash,
            unverified = reply.Provenance.Unverified,
        },
        followUps = reply.FollowUps,
        payload = reply.Payload is null ? (JsonElement?)null : ReplyPayloadJson.ToJsonElement(reply.Payload),
    };

    private static async Task<IResult> ListRunsAsync(PlaygroundStore store, int limit, CancellationToken ct)
    {
        var rows = await store.ListRunsAsync(limit, ct).ConfigureAwait(false);
        return Results.Ok(new
        {
            runs = rows.Select(r => new
            {
                id = r.Id, workflow = r.Workflow, sandbox = r.SandboxKind, createdBy = r.CreatedBy, createdAt = r.CreatedAt,
                durationMs = r.DurationMs, status = r.Status, promptTokens = r.PromptTokens, completionTokens = r.CompletionTokens,
                estimatedCostUsd = r.EstimatedCostUsd, input = JsonSerializer.Deserialize<JsonElement>(r.Input),
            }),
        });
    }

    private static async Task<IResult> GetRunAsync(PlaygroundStore store, Guid runId, CancellationToken ct)
    {
        var row = await store.GetRunAsync(runId, ct).ConfigureAwait(false);
        return row is null
            ? Results.NotFound()
            : Results.Ok(new
            {
                id = row.Id, workflow = row.Workflow, sandbox = row.SandboxKind, createdBy = row.CreatedBy, createdAt = row.CreatedAt,
                durationMs = row.DurationMs, status = row.Status, estimatedCostUsd = row.EstimatedCostUsd,
                input = JsonSerializer.Deserialize<JsonElement>(row.Input),
                output = JsonSerializer.Deserialize<JsonElement>(row.Output),
                trace = JsonSerializer.Deserialize<JsonElement>(row.Trace),
            });
    }
}
