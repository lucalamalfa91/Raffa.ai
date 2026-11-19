using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.Ask.Agentic;
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
                PlaygroundStore store, AskCopilotService ask, ITenantContext tenantContext, IClock clock, IOptions<PlaygroundPricingOptions> pricing,
                PlaygroundCatalog catalog, CancellationToken ct) =>
            RunAsync(id, guard, () => StartRunAsync(body, id.Resolve()!, seeder, store, ask, tenantContext, clock, pricing.Value, catalog, ct)));
        endpoints.MapGet("/api/admin/playground/baseline", (ICallerIdentity id, PlatformAdminGuard guard, PlaygroundCatalog catalog, AskTurnOptions turnOptions) =>
            RunAsync(id, guard, () => Task.FromResult(Baseline(catalog, turnOptions))));
        endpoints.MapGet("/api/admin/playground/variants", (ICallerIdentity id, PlatformAdminGuard guard, PlaygroundStore store, CancellationToken ct) =>
            RunAsync(id, guard, () => ListVariantsAsync(store, ct)));
        endpoints.MapPost("/api/admin/playground/variants", (PlaygroundVariantRequest? body, ICallerIdentity id, PlatformAdminGuard guard, PlaygroundStore store,
                PlaygroundCatalog catalog, IClock clock, CancellationToken ct) =>
            RunAsync(id, guard, () => SaveVariantAsync(null, body, id.Resolve()!, store, catalog, clock, ct)));
        endpoints.MapPut("/api/admin/playground/variants/{variantId:guid}", (Guid variantId, PlaygroundVariantRequest? body, ICallerIdentity id, PlatformAdminGuard guard,
                PlaygroundStore store, PlaygroundCatalog catalog, IClock clock, CancellationToken ct) =>
            RunAsync(id, guard, () => SaveVariantAsync(variantId, body, id.Resolve()!, store, catalog, clock, ct)));
        endpoints.MapDelete("/api/admin/playground/variants/{variantId:guid}", (Guid variantId, ICallerIdentity id, PlatformAdminGuard guard, PlaygroundStore store, CancellationToken ct) =>
            RunAsync(id, guard, async () => await store.ArchiveVariantAsync(variantId, ct).ConfigureAwait(false) ? Results.NoContent() : Results.NotFound()));
        endpoints.MapGet("/api/admin/playground/runs", (int? limit, ICallerIdentity id, PlatformAdminGuard guard, PlaygroundStore store, CancellationToken ct) =>
            RunAsync(id, guard, () => ListRunsAsync(store, limit ?? 50, ct)));
        endpoints.MapGet("/api/admin/playground/runs/{runId:guid}", (Guid runId, ICallerIdentity id, PlatformAdminGuard guard, PlaygroundStore store, CancellationToken ct) =>
            RunAsync(id, guard, () => GetRunAsync(store, runId, ct)));
        return endpoints;
    }

    public sealed record PlaygroundHistoryTurn(string? Role, string? Markdown);

    public sealed record PlaygroundRunRequest(
        string? Sandbox, string? Question, IReadOnlyList<PlaygroundHistoryTurn>? History, Guid? VariantId = null, Guid? ComparisonId = null);

    public sealed record PlaygroundVariantRequest(string? Name, string? Description, PlaygroundOverrides? Overrides);

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
        ITenantContext tenantContext, IClock clock, PlaygroundPricingOptions pricing, PlaygroundCatalog catalog, CancellationToken ct)
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

        // The variant (none = what production runs). Its overrides are re-validated against this environment's
        // catalog: a variant saved elsewhere can never name a deployment or tool that does not exist here.
        PlaygroundVariantRow? variant = null;
        var overrides = new PlaygroundOverrides();
        if (body.VariantId is { } variantId)
        {
            variant = await store.GetVariantAsync(variantId, ct).ConfigureAwait(false);
            if (variant is null)
            {
                return Results.NotFound();
            }

            var (problem, clean) = catalog.Validate(variant.Name, variant.Description, ParseOverrides(variant.Overrides));
            if (problem is not null)
            {
                return Results.BadRequest($"Variant '{variant.Name}' is not valid here: {problem}");
            }

            overrides = clean;
        }

        var runId = Guid.NewGuid();
        var startedAt = clock.UtcNow;
        var clock2 = Stopwatch.StartNew();
        CopilotReply? copilotReply = null;
        string status = "ok";
        string? error = null;
        PlaygroundTraceCollector collector;
        using (var tenantScope = tenantContext.BeginScope(tenantId))
        using (var traceScope = PlaygroundTrace.Begin(out collector))
        using (var variantScope = PlaygroundScope.Begin(overrides))
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
            VariantId = variant?.Id,
            VariantName = variant?.Name,
            ComparisonId = body.ComparisonId,
            PromptTokens = prompt,
            CompletionTokens = completion,
            EstimatedCostUsd = cost,
            Input = JsonSerializer.Serialize(new
            {
                question,
                history = history.Select(h => new { role = h.Role, markdown = h.Markdown }),
                variant = variant is null ? null : new { id = variant.Id, name = variant.Name, overrides = ParseOverrides(variant.Overrides) },
            }, Json),
            Output = JsonSerializer.Serialize(new { reply, error, usage }, Json),
            Trace = JsonSerializer.Serialize(events, Json),
        }, ct).ConfigureAwait(false);

        return Results.Ok(new { runId, sandbox = body.Sandbox, variantId = variant?.Id, variantName = variant?.Name, comparisonId = body.ComparisonId, status, error, durationMs = clock2.ElapsedMilliseconds, reply, usage, trace = events });
    }

    private static PlaygroundOverrides? ParseOverrides(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<PlaygroundOverrides>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IResult Baseline(PlaygroundCatalog catalog, AskTurnOptions turnOptions) => Results.Ok(new
    {
        prompts = new[] { new { agent = "agent", version = AgentPrompt.Version, text = AgentPrompt.SystemPrompt } },
        models = catalog.RoleSelections().Select(r => new
        {
            role = r.Role,
            configured = r.Selection is not null,
            fallbackOf = r.FallbackOf,
            modelId = r.Selection?.ModelId,
            modelVersion = r.Selection?.ModelVersion,
            reasoningEffort = r.Selection?.ReasoningEffort,
            maxCompletionTokens = r.Selection?.MaxCompletionTokens,
            temperature = r.Selection?.Temperature,
        }),
        allowedModelIds = catalog.AllowedModelIds(),
        reasoningEfforts = PlaygroundCatalog.ReasoningEfforts,
        maxTemperature = PlaygroundCatalog.MaxTemperature,
        tools = catalog.Tools.Select(t => new { name = t.Name, description = t.Description }),
        maxToolCalls = turnOptions.AgenticMaxToolCalls,
        agentic = turnOptions.Agentic,
    });

    private static object VariantJson(PlaygroundVariantRow v) => new
    {
        id = v.Id, name = v.Name, description = v.Description, overrides = ParseOverrides(v.Overrides) ?? new PlaygroundOverrides(),
        createdBy = v.CreatedBy, createdAt = v.CreatedAt, updatedAt = v.UpdatedAt,
    };

    private static async Task<IResult> ListVariantsAsync(PlaygroundStore store, CancellationToken ct) =>
        Results.Ok(new { variants = (await store.ListVariantsAsync(ct).ConfigureAwait(false)).Select(VariantJson) });

    private static async Task<IResult> SaveVariantAsync(
        Guid? existingId, PlaygroundVariantRequest? body, string actor, PlaygroundStore store, PlaygroundCatalog catalog, IClock clock, CancellationToken ct)
    {
        var (problem, clean) = catalog.Validate(body?.Name, body?.Description, body?.Overrides);
        if (problem is not null)
        {
            return Results.BadRequest(problem);
        }

        var now = clock.UtcNow;
        var row = new PlaygroundVariantRow
        {
            Id = existingId ?? Guid.NewGuid(),
            Name = body!.Name!.Trim(),
            Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim(),
            Overrides = JsonSerializer.Serialize(clean, Json),
            CreatedBy = actor,
            CreatedAt = now,
            UpdatedAt = now,
        };
        if (!await store.SaveVariantAsync(row, create: existingId is null, ct).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        return existingId is null ? Results.Created($"/api/admin/playground/variants/{row.Id}", VariantJson(row)) : Results.Ok(VariantJson(row));
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
                variantId = r.VariantId, variantName = r.VariantName, comparisonId = r.ComparisonId,
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
                variantId = row.VariantId, variantName = row.VariantName, comparisonId = row.ComparisonId,
                durationMs = row.DurationMs, status = row.Status, estimatedCostUsd = row.EstimatedCostUsd,
                input = JsonSerializer.Deserialize<JsonElement>(row.Input),
                output = JsonSerializer.Deserialize<JsonElement>(row.Output),
                trace = JsonSerializer.Deserialize<JsonElement>(row.Trace),
            });
    }
}
