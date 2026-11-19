using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Raffa.AiFlows.Playground;
using Raffa.AiGateway.Telemetry;
using Raffa.Api.Infrastructure;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.Extensions.Options;

namespace Raffa.Api;

/// <summary>
/// The LLM judge of the Super Admin playground (docs/architecture/admin-agent-playground-plan.md, PR 4): judging
/// rubrics (built-in per workflow, plus custom ones the admin edits) and judgements over the stored runs of a
/// comparison. Guarded exactly as the rest of <c>/api/admin/*</c>: 401 anonymous, 404 for anyone who is not a
/// platform admin. A judgement keeps a snapshot of the rubric it used, the judge's raw calls (its trace) and its cost.
/// </summary>
public static partial class PlaygroundJudgeEndpointExtensions
{
    private const int MaxCriteria = 8;
    private const int MaxRuns = 3;
    private const int MaxContentChars = 12_000;
    private const int MaxEvidenceChars = 12_000;
    private const int MaxExtractionEvidenceChars = 30_000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapPlaygroundJudgeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/playground/rubrics", (ICallerIdentity id, PlatformAdminGuard guard, PlaygroundStore store, CancellationToken ct) =>
            PlaygroundEndpointExtensions.RunAsync(id, guard, () => ListRubricsAsync(store, ct)));
        endpoints.MapPost("/api/admin/playground/rubrics", (PlaygroundRubricRequest? body, ICallerIdentity id, PlatformAdminGuard guard, PlaygroundStore store, IClock clock, CancellationToken ct) =>
            PlaygroundEndpointExtensions.RunAsync(id, guard, () => SaveRubricAsync(null, body, id.Resolve()!, store, clock, ct)));
        endpoints.MapPut("/api/admin/playground/rubrics/{rubricId:guid}", (Guid rubricId, PlaygroundRubricRequest? body, ICallerIdentity id, PlatformAdminGuard guard, PlaygroundStore store, IClock clock, CancellationToken ct) =>
            PlaygroundEndpointExtensions.RunAsync(id, guard, () => SaveRubricAsync(rubricId, body, id.Resolve()!, store, clock, ct)));
        endpoints.MapDelete("/api/admin/playground/rubrics/{rubricId:guid}", (Guid rubricId, ICallerIdentity id, PlatformAdminGuard guard, PlaygroundStore store, CancellationToken ct) =>
            PlaygroundEndpointExtensions.RunAsync(id, guard, async () => await store.ArchiveRubricAsync(rubricId, ct).ConfigureAwait(false) ? Results.NoContent() : Results.NotFound()));
        endpoints.MapPost("/api/admin/playground/judgements", (PlaygroundJudgeRequest? body, ICallerIdentity id, PlatformAdminGuard guard, PlaygroundStore store, PlaygroundJudge judge,
                PlaygroundCatalog catalog, IOptions<PlaygroundPricingOptions> pricing, IClock clock, CancellationToken ct) =>
            PlaygroundEndpointExtensions.RunAsync(id, guard, () => JudgeAsync(body, id.Resolve()!, store, judge, catalog, pricing.Value, clock, ct)));
        endpoints.MapGet("/api/admin/playground/judgements", (Guid? comparisonId, Guid? runId, ICallerIdentity id, PlatformAdminGuard guard, PlaygroundStore store, CancellationToken ct) =>
            PlaygroundEndpointExtensions.RunAsync(id, guard, () => ListJudgementsAsync(comparisonId, runId, store, ct)));
        return endpoints;
    }

    public sealed record PlaygroundCriterionRequest(string? Id, string? Label, string? Description, int? Weight);

    public sealed record PlaygroundRubricRequest(string? Name, string? Workflow, IReadOnlyList<PlaygroundCriterionRequest>? Criteria);

    /// <param name="ComparisonId">Judge every run of this comparison, or</param>
    /// <param name="RunIds">these runs (1 to 3, same workflow and question).</param>
    /// <param name="RubricId"><c>default:&lt;workflow&gt;</c> or the id of a custom rubric.</param>
    /// <param name="JudgeModelId">A deployment configured in this environment; absent = the analyst role's own.</param>
    /// <param name="Passes">1, or 2 to also show the answers in reverse order and see whether the verdict holds.</param>
    public sealed record PlaygroundJudgeRequest(Guid? ComparisonId, IReadOnlyList<Guid>? RunIds, string? RubricId, string? JudgeModelId, int? Passes);

    // ----- rubrics -----

    private static object RubricJson(JudgeRubric r) => new
    {
        id = r.Id, name = r.Name, workflow = r.Workflow, isDefault = r.IsDefault,
        criteria = r.Criteria.Select(c => new { id = c.Id, label = c.Label, description = c.Description, weight = c.Weight }),
    };

    private static JudgeRubric ToRubric(PlaygroundRubricRow row) =>
        new(row.Id.ToString(), row.Name, row.Workflow, ParseCriteria(row.Criteria), false);

    private static IReadOnlyList<JudgeCriterion> ParseCriteria(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<JudgeCriterion>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static async Task<IResult> ListRubricsAsync(PlaygroundStore store, CancellationToken ct)
    {
        var custom = (await store.ListRubricsAsync(ct).ConfigureAwait(false)).Select(ToRubric);
        return Results.Ok(new { rubrics = PlaygroundRubrics.Defaults.Concat(custom).Select(RubricJson) });
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlug();

    private static string? ValidateRubric(PlaygroundRubricRequest? body, out List<JudgeCriterion> criteria)
    {
        criteria = [];
        if (string.IsNullOrWhiteSpace(body?.Name) || body.Name.Trim().Length > 120)
        {
            return "'name' is required and at most 120 characters.";
        }

        if (body.Workflow is null || !PlaygroundRubrics.Workflows.Contains(body.Workflow, StringComparer.Ordinal))
        {
            return $"'workflow' must be one of: {string.Join(", ", PlaygroundRubrics.Workflows)}.";
        }

        if (body.Criteria is null || body.Criteria.Count is 0 or > MaxCriteria)
        {
            return $"A rubric needs 1 to {MaxCriteria} criteria.";
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in body.Criteria)
        {
            var label = c.Label?.Trim();
            if (string.IsNullOrEmpty(label) || label.Length > 80)
            {
                return "Every criterion needs a label of at most 80 characters.";
            }

            if (c.Description is { Length: > 400 })
            {
                return "A criterion description is at most 400 characters.";
            }

            if (c.Weight is not (>= 1 and <= 5))
            {
                return "A criterion weight is a whole number from 1 to 5.";
            }

            var slug = NonSlug().Replace(label.ToLowerInvariant(), "_").Trim('_');
            slug = slug.Length == 0 ? "criterion" : slug;
            var id = slug;
            for (var n = 2; !used.Add(id); n++)
            {
                id = $"{slug}_{n}";
            }

            criteria.Add(new JudgeCriterion(id, label, c.Description?.Trim() ?? string.Empty, c.Weight.Value));
        }

        return null;
    }

    private static async Task<IResult> SaveRubricAsync(
        Guid? existingId, PlaygroundRubricRequest? body, string actor, PlaygroundStore store, IClock clock, CancellationToken ct)
    {
        if (ValidateRubric(body, out var criteria) is { } problem)
        {
            return Results.BadRequest(problem);
        }

        var now = clock.UtcNow;
        var row = new PlaygroundRubricRow
        {
            Id = existingId ?? Guid.NewGuid(), Workflow = body!.Workflow!, Name = body.Name!.Trim(),
            Criteria = JsonSerializer.Serialize(criteria, Json), CreatedBy = actor, CreatedAt = now, UpdatedAt = now,
        };
        if (!await store.SaveRubricAsync(row, create: existingId is null, ct).ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        var saved = RubricJson(ToRubric(row));
        return existingId is null ? Results.Created($"/api/admin/playground/rubrics/{row.Id}", saved) : Results.Ok(saved);
    }

    // ----- judgements -----

    private static async Task<IResult> JudgeAsync(
        PlaygroundJudgeRequest? body, string actor, PlaygroundStore store, PlaygroundJudge judge, PlaygroundCatalog catalog,
        PlaygroundPricingOptions pricing, IClock clock, CancellationToken ct)
    {
        var ids = body?.RunIds ?? [];
        if (body?.ComparisonId is null && ids.Count == 0)
        {
            return Results.BadRequest("Give 'comparisonId' or 'runIds'.");
        }

        var runs = (await store.GetRunsAsync(ids, body?.ComparisonId, ct).ConfigureAwait(false)).Where(r => r.Status == "ok").ToList();
        if (runs.Count == 0)
        {
            return Results.NotFound();
        }

        if (runs.Count > MaxRuns)
        {
            return Results.BadRequest($"At most {MaxRuns} runs can be judged together.");
        }

        var workflow = runs[0].Workflow;
        if (runs.Any(r => r.Workflow != workflow) || runs.Select(r => QuestionOf(r)).Distinct(StringComparer.Ordinal).Count() > 1)
        {
            return Results.BadRequest("The runs must be of the same workflow and answer the same question.");
        }

        var rubric = await ResolveRubricAsync(body?.RubricId, workflow, store, ct).ConfigureAwait(false);
        if (rubric is null)
        {
            return Results.BadRequest($"'rubricId' must be 'default:{workflow}' or a custom rubric of the '{workflow}' workflow.");
        }

        var judgeModel = string.IsNullOrWhiteSpace(body?.JudgeModelId) ? null : body.JudgeModelId.Trim();
        if (judgeModel is not null && !catalog.AllowedModelIds().Contains(judgeModel, StringComparer.Ordinal))
        {
            return Results.BadRequest($"Deployment '{judgeModel}' is not configured in this environment. Available: {string.Join(", ", catalog.AllowedModelIds())}.");
        }

        var passes = body?.Passes is 2 ? 2 : 1;
        var request = new JudgeRequest(workflow, TaskOf(runs[0]), rubric.Criteria, [.. runs.Select(AnswerOf)], passes);

        JudgeResult result;
        PlaygroundTraceCollector collector;
        // The judge reads the analyst role: the admin's choice of deployment applies to this call only.
        var overrides = new PlaygroundOverrides(Models: new Dictionary<string, PlaygroundModelOverride> { ["analyst"] = new(judgeModel, null, 8192, null) });
        using (var traceScope = PlaygroundTrace.Begin(out collector))
        using (var scope = PlaygroundScope.Begin(overrides))
        {
            result = await judge.JudgeAsync(request, ct).ConfigureAwait(false);
        }

        var events = collector.Events;
        var calls = events.Where(e => e.Kind == "model_call" && e.Ok).ToList();
        var prompt = calls.Sum(e => e.PromptTokens ?? 0);
        var completion = calls.Sum(e => e.CompletionTokens ?? 0);
        var priced = calls.Select(e => pricing.CostUsd(e.Model, e.PromptTokens ?? 0, e.CompletionTokens ?? 0, e.CachedTokens ?? 0)).ToList();
        decimal? cost = priced.Count == 0 ? 0m : priced.All(c => c is not null) ? priced.Sum(c => c!.Value) : null;
        var model = calls.Select(e => e.Model).FirstOrDefault(m => m is not null) ?? judgeModel;

        var byRun = runs.ToDictionary(r => r.Id.ToString(), r => r);
        string NameOf(string runId) => byRun[runId].VariantName ?? "Baseline";
        Guid? comparisonId = body?.ComparisonId ?? runs.Select(r => r.ComparisonId).FirstOrDefault(c => c is not null);
        var judgementId = Guid.NewGuid();
        var view = new
        {
            judgementId,
            workflow,
            comparisonId,
            createdAt = clock.UtcNow,
            rubric = RubricJson(rubric),
            judgeModel = model,
            passes,
            runs = runs.Select(r => new { runId = r.Id, variantId = r.VariantId, variantName = NameOf(r.Id.ToString()) }),
            answers = result.Answers.Select(a => new
            {
                runId = a.AnswerId,
                variantName = NameOf(a.AnswerId),
                weighted = a.Weighted,
                summary = a.Summary,
                scores = a.Scores.Select(s => new { criterionId = s.CriterionId, score = s.Score, rationale = s.Rationale }),
            }),
            preferredRunId = result.PreferredAnswerId,
            tie = result.Tie,
            passesAgreed = result.PassesAgreed,
            passDetails = result.Passes.Select(p => new { order = p.Order, preferredRunId = p.PreferredAnswerId, tie = p.Tie, reasoning = p.Reasoning }),
            failures = result.Failures,
            usage = new { promptTokens = prompt, completionTokens = completion, estimatedCostUsd = cost },
        };

        await store.SaveJudgementAsync(new PlaygroundJudgementRow
        {
            Id = judgementId,
            ComparisonId = comparisonId,
            RunIds = JsonSerializer.Serialize(runs.Select(r => r.Id), Json),
            Workflow = workflow,
            Rubric = JsonSerializer.Serialize(RubricJson(rubric), Json),
            JudgeModel = model,
            Passes = passes,
            CreatedBy = actor,
            CreatedAt = clock.UtcNow,
            PromptTokens = prompt,
            CompletionTokens = completion,
            EstimatedCostUsd = cost,
            Result = JsonSerializer.Serialize(view, Json),
            Trace = JsonSerializer.Serialize(events, Json),
        }, ct).ConfigureAwait(false);

        return Results.Ok(new { view.judgementId, view.workflow, view.comparisonId, view.createdAt, view.rubric, view.judgeModel, view.passes, view.runs, view.answers,
            view.preferredRunId, view.tie, view.passesAgreed, view.passDetails, view.failures, view.usage, trace = events });
    }

    private static async Task<IResult> ListJudgementsAsync(Guid? comparisonId, Guid? runId, PlaygroundStore store, CancellationToken ct)
    {
        if (comparisonId is null && runId is null)
        {
            return Results.BadRequest("Give 'comparisonId' or 'runId'.");
        }

        var rows = await store.ListJudgementsAsync(comparisonId, runId, ct).ConfigureAwait(false);
        return Results.Ok(new { judgements = rows.Select(r => JsonSerializer.Deserialize<JsonElement>(r.Result)) });
    }

    private static async Task<JudgeRubric?> ResolveRubricAsync(string? rubricId, string workflow, PlaygroundStore store, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rubricId))
        {
            return PlaygroundRubrics.DefaultFor(workflow);
        }

        if (PlaygroundRubrics.Default(rubricId) is { } builtIn)
        {
            return builtIn.Workflow == workflow ? builtIn : null;
        }

        if (!Guid.TryParse(rubricId, out var id) || await store.GetRubricAsync(id, ct).ConfigureAwait(false) is not { } row || row.Workflow != workflow)
        {
            return null;
        }

        var rubric = ToRubric(row);
        return rubric.Criteria.Count == 0 ? null : rubric;
    }

    // ----- what the judge reads -----

    private static JsonElement Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static JsonElement Prop(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    private static string Str(JsonElement element, string name) => Prop(element, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() ?? string.Empty : string.Empty;

    private static string QuestionOf(PlaygroundRunRow run) => Str(Parse(run.Input), "question");

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max] + "…[cut]";

    /// <summary>The task the judge is told: the question, with the conversation so far when there was one.</summary>
    private static string TaskOf(PlaygroundRunRow run)
    {
        var input = Parse(run.Input);
        var sb = new StringBuilder();
        var history = Prop(input, "history");
        if (history.ValueKind == JsonValueKind.Array && history.GetArrayLength() > 0)
        {
            sb.AppendLine("Conversation so far:");
            foreach (var turn in history.EnumerateArray())
            {
                sb.AppendLine($"- {Str(turn, "role")}: {Cut(Str(turn, "markdown"), 1500)}");
            }

            sb.AppendLine();
        }

        sb.Append(run.Workflow == "ask" ? "Question: " : "Task: ").Append(Str(input, "question"));
        return Cut(sb.ToString(), 6000);
    }

    /// <summary>One run as the judge sees it: the content, the evidence it was built from and how it was produced. Never the variant.</summary>
    private static JudgeAnswer AnswerOf(PlaygroundRunRow run)
    {
        var output = Parse(run.Output);
        var trace = Parse(run.Trace);
        var reply = Prop(output, "reply");
        var result = Prop(output, "result");
        var signals = new Dictionary<string, string> { ["durationMs"] = run.DurationMs.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        string content;
        string? evidence;

        switch (run.Workflow)
        {
            case "ask":
            {
                content = Str(reply, "answerMarkdown");
                signals["replyKind"] = Str(reply, "kind");
                signals["unverified"] = Prop(Prop(reply, "provenance"), "unverified").ValueKind == JsonValueKind.True ? "true" : "false";
                var tools = trace.ValueKind == JsonValueKind.Array
                    ? trace.EnumerateArray().Where(e => Str(e, "kind") == "tool_call").ToList()
                    : [];
                signals["toolsCalled"] = string.Join(", ", tools.Select(t => Str(t, "name")));
                evidence = Cut(string.Join("\n", tools.Select(t => $"[{Str(t, "name")}] {Cut(Str(t, "output"), 3000)}")), MaxEvidenceChars);
                break;
            }

            case "council":
                content = string.Join("\n\n", Prop(result, "plays").ValueKind == JsonValueKind.Array
                    ? Prop(result, "plays").EnumerateArray().Select(p => $"{Str(p, "title")}: {Str(p, "snippet")}")
                    : []);
                evidence = Cut(Prop(result, "evidence").ToString() ?? string.Empty, MaxEvidenceChars);
                break;

            case "drafting":
                content = $"Subject: {Str(result, "subject")}\n\n{Str(result, "body")}";
                signals["writtenBy"] = Str(result, "source");
                evidence = Cut(Prop(result, "evidence").ToString() ?? string.Empty, MaxEvidenceChars);
                break;

            case "research":
                content = Str(result, "markdown");
                signals["sentQuery"] = Str(result, "sentQuery");
                signals["kind"] = Str(result, "kind");
                evidence = Cut(Prop(result, "citations").ToString() ?? string.Empty, MaxEvidenceChars);
                break;

            default: // extraction
                content = Cut(Prop(result, "stages").ToString() ?? string.Empty, MaxContentChars);
                evidence = Cut(Str(result, "sourceText"), MaxExtractionEvidenceChars);
                break;
        }

        return new JudgeAnswer(run.Id.ToString(), Cut(content, MaxContentChars), string.IsNullOrWhiteSpace(evidence) ? null : evidence, signals);
    }
}
