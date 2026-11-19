using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.Ask.Agentic;
using Raffa.AiFlows.Playground;
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
                PlaygroundCatalog catalog, PlaygroundWorkflowRunner runner, CancellationToken ct) =>
            RunAsync(id, guard, () => StartRunAsync(body, id.Resolve()!, seeder, store, ask, tenantContext, clock, pricing.Value, catalog, runner, ct)));
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

    /// <param name="Workflow"><c>ask</c> (default), <c>council</c>, <c>drafting</c>, <c>research</c> or <c>extraction</c>.</param>
    /// <param name="Input">The workflow's own input (supplier and goal, a web query, a document); <c>ask</c> uses Question and History.</param>
    public sealed record PlaygroundRunRequest(
        string? Sandbox, string? Question, IReadOnlyList<PlaygroundHistoryTurn>? History, Guid? VariantId = null, Guid? ComparisonId = null,
        string? Workflow = null, JsonElement? Input = null);

    public sealed record PlaygroundVariantRequest(string? Name, string? Description, PlaygroundOverrides? Overrides);

    internal static async Task<IResult> RunAsync(ICallerIdentity callerIdentity, PlatformAdminGuard guard, Func<Task<IResult>> action)
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
        ITenantContext tenantContext, IClock clock, PlaygroundPricingOptions pricing, PlaygroundCatalog catalog, PlaygroundWorkflowRunner runner,
        CancellationToken ct)
    {
        if (PlaygroundSandboxKinds.TenantOf(body?.Sandbox) is not { } tenantId)
        {
            return Results.BadRequest($"'sandbox' must be one of: {string.Join(", ", PlaygroundSandboxKinds.All)}.");
        }

        var workflow = string.IsNullOrWhiteSpace(body?.Workflow) ? "ask" : body.Workflow.Trim().ToLowerInvariant();
        if (!Workflows.Contains(workflow))
        {
            return Results.BadRequest($"'workflow' must be one of: {string.Join(", ", Workflows)}.");
        }

        var plan = Plan(workflow, body!);
        if (plan.Problem is not null)
        {
            return Results.BadRequest(plan.Problem);
        }

        var question = plan.Summary;

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
        object? result = null;
        string status = "ok";
        string? error = null;
        PlaygroundTraceCollector collector;
        using (var tenantScope = tenantContext.BeginScope(tenantId))
        using (var traceScope = PlaygroundTrace.Begin(out collector))
        using (var variantScope = PlaygroundScope.Begin(overrides))
        {
            try
            {
                if (workflow == "ask")
                {
                    copilotReply = await ask.AskAsync(tenantId, question, history, actor, cancellationToken: ct).ConfigureAwait(false);
                }
                else
                {
                    var outcome = await RunWorkflowAsync(workflow, plan, tenantId, actor, runner, ct).ConfigureAwait(false);
                    result = outcome.Output;
                    if (outcome.Error is not null)
                    {
                        status = "error";
                        error = outcome.Error;
                        collector.Add("note", "workflow-failed", 0, ok: false, note: error);
                    }
                }
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
            Workflow = workflow,
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
                workflow,
                question,
                input = plan.StoredInput,
                history = history.Select(h => new { role = h.Role, markdown = h.Markdown }),
                variant = variant is null ? null : new { id = variant.Id, name = variant.Name, overrides = ParseOverrides(variant.Overrides) },
            }, Json),
            Output = JsonSerializer.Serialize(new { reply, result, error, usage }, Json),
            Trace = JsonSerializer.Serialize(events, Json),
        }, ct).ConfigureAwait(false);

        return Results.Ok(new { runId, workflow, sandbox = body.Sandbox, variantId = variant?.Id, variantName = variant?.Name, comparisonId = body.ComparisonId, status, error, durationMs = clock2.ElapsedMilliseconds, reply, result, usage, trace = events });
    }

    private static readonly string[] Workflows = ["ask", "council", "drafting", "research", "extraction"];

    private const long MaxUploadBytes = 8 * 1024 * 1024;

    private static readonly string[] UploadMimeTypes = ["application/pdf", "image/png", "image/jpeg", "image/tiff"];

    /// <summary>One run's validated input: <see cref="Summary"/> is the line the history shows; <see cref="StoredInput"/> is what is
    /// kept (a file is kept as its name and size, never its bytes).</summary>
    private sealed record WorkflowPlan(
        string? Problem, string Summary, object? StoredInput, string? Supplier = null, string? Goal = null, string? Question = null,
        string Language = "en", string? Query = null, string Purpose = "Open", string? Text = null, string? FileName = null,
        string? MimeType = null, byte[]? File = null, bool DocumentFirst = true);

    private static string? Text(JsonElement? input, string name) =>
        input is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()?.Trim()
            : null;

    private static WorkflowPlan Bad(string problem) => new(problem, string.Empty, null);

    private static WorkflowPlan Plan(string workflow, PlaygroundRunRequest body)
    {
        var language = Text(body.Input, "language") is "it" ? "it" : "en";
        switch (workflow)
        {
            case "ask":
            {
                var question = body.Question?.Trim();
                return string.IsNullOrEmpty(question) || question.Length > MaxQuestionChars
                    ? Bad($"'question' is required and at most {MaxQuestionChars} characters.")
                    : new WorkflowPlan(null, question, null);
            }

            case "council":
            case "drafting":
            {
                var supplier = Text(body.Input, "supplier");
                if (string.IsNullOrEmpty(supplier) || supplier.Length > 200)
                {
                    return Bad("'input.supplier' is required (a supplier of the sandbox).");
                }

                var goal = Text(body.Input, "goal");
                var question = Text(body.Input, "question");
                if (goal is { Length: > 500 } || question is { Length: > MaxQuestionChars })
                {
                    return Bad("'input.goal' is at most 500 characters and 'input.question' at most " + MaxQuestionChars + ".");
                }

                question = string.IsNullOrEmpty(question)
                    ? (workflow == "council" ? $"Negotiation strategy for {supplier}" : $"Draft the renewal negotiation email to {supplier}")
                    : question;
                var summary = $"[{workflow}] {supplier}" + (string.IsNullOrEmpty(goal) ? string.Empty : $" · {goal}");
                return new WorkflowPlan(null, summary, new { supplier, goal, question, language }, supplier, goal, question, language);
            }

            case "research":
            {
                var query = Text(body.Input, "query");
                if (string.IsNullOrEmpty(query) || query.Length > 500)
                {
                    return Bad("'input.query' is required and at most 500 characters.");
                }

                var purpose = Text(body.Input, "purpose") ?? "Open";
                return PlaygroundWorkflowRunner.WebPurposes.Contains(purpose, StringComparer.Ordinal)
                    ? new WorkflowPlan(null, $"[research] {query}", new { query, purpose, language }, Query: query, Purpose: purpose, Language: language)
                    : Bad($"'input.purpose' must be one of: {string.Join(", ", PlaygroundWorkflowRunner.WebPurposes)}.");
            }

            default: // extraction
            {
                var documentFirst = body.Input is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty("documentFirst", out var df)
                    ? df.ValueKind != JsonValueKind.False
                    : true;
                var base64 = Text(body.Input, "fileBase64");
                if (!string.IsNullOrEmpty(base64))
                {
                    var fileName = Text(body.Input, "fileName") ?? "document";
                    var mime = Text(body.Input, "mimeType") ?? string.Empty;
                    if (!UploadMimeTypes.Contains(mime, StringComparer.OrdinalIgnoreCase))
                    {
                        return Bad($"'input.mimeType' must be one of: {string.Join(", ", UploadMimeTypes)}.");
                    }

                    byte[] bytes;
                    try
                    {
                        bytes = Convert.FromBase64String(base64);
                    }
                    catch (FormatException)
                    {
                        return Bad("'input.fileBase64' is not valid base64.");
                    }

                    return bytes.Length == 0 || bytes.Length > MaxUploadBytes
                        ? Bad($"The file must be between 1 byte and {(int)(MaxUploadBytes / (1024 * 1024))} MB.")
                        : new WorkflowPlan(null, $"[extraction] {fileName}", new { fileName, mimeType = mime, bytes = bytes.Length, documentFirst },
                            FileName: fileName, MimeType: mime, File: bytes, DocumentFirst: documentFirst);
                }

                var text = body.Input is { ValueKind: JsonValueKind.Object } t && t.TryGetProperty("text", out var tv) && tv.ValueKind == JsonValueKind.String
                    ? tv.GetString()
                    : null;
                return string.IsNullOrWhiteSpace(text)
                    ? Bad("'input.text' or 'input.fileBase64' is required.")
                    : new WorkflowPlan(null, $"[extraction] {text.Trim()[..Math.Min(60, text.Trim().Length)]}…", new { characters = text.Length, documentFirst },
                        Text: text, DocumentFirst: documentFirst);
            }
        }
    }

    private static async Task<PlaygroundWorkflowResult> RunWorkflowAsync(
        string workflow, WorkflowPlan plan, Raffa.SharedKernel.TenantId tenantId, string actor, PlaygroundWorkflowRunner runner, CancellationToken ct)
    {
        switch (workflow)
        {
            case "council":
                return await runner.RunCouncilAsync(tenantId, plan.Supplier!, plan.Goal, plan.Question!, actor, ct).ConfigureAwait(false);
            case "drafting":
                return await runner.RunDraftingAsync(tenantId, plan.Supplier!, plan.Goal, plan.Question!, plan.Language, actor, ct).ConfigureAwait(false);
            case "research":
                return await runner.RunWebResearchAsync(plan.Query!, plan.Purpose, plan.Language, ct).ConfigureAwait(false);
            default:
            {
                IReadOnlyList<Raffa.Documents.Contracts.Application.Extraction.DocumentPageText> pages;
                if (plan.File is not null)
                {
                    var (read, problem) = await runner.ReadFileAsync(plan.FileName!, plan.MimeType!, plan.File, ct).ConfigureAwait(false);
                    if (read is null)
                    {
                        return PlaygroundWorkflowResult.Fail("The file could not be read: " + problem);
                    }

                    pages = read;
                }
                else
                {
                    // Plain text: a form feed starts a new page, otherwise it is one page.
                    pages = [.. plan.Text!.Split('\f').Select((page, i) => new Raffa.Documents.Contracts.Application.Extraction.DocumentPageText(i + 1, page))];
                }

                return await runner.RunExtractionAsync(pages, plan.DocumentFirst, ct).ConfigureAwait(false);
            }
        }
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
        prompts = catalog.PromptCatalog().Select(p => new { agent = p.Agent, flow = p.Flow, version = p.Version, text = p.Text }),
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
