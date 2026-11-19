using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Raffa.AiFlows.Ask.Agentic;
using Raffa.AiGateway.Telemetry;
using Raffa.Api.Infrastructure;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;

namespace Raffa.Api;

/// <summary>
/// Export of a playground variant to the repository (docs/architecture/admin-agent-playground-plan.md, PR 5): a <b>draft pull
/// request</b> with a report, the proposed prompts and diffs, and for the orchestrator prompt the version bump the drift test
/// guards; or a downloadable bundle of the same files when GitHub is not available. The pull request uses the connection the
/// feedback publisher already has (repository and token), is always a draft on a new branch, and is never merged by Raffa. Guarded
/// as the rest of <c>/api/admin/*</c>: 401 anonymous, 404 for anyone who is not a platform admin.
/// </summary>
public static class PlaygroundExportEndpointExtensions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapPlaygroundExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/playground/export", (ICallerIdentity id, PlatformAdminGuard guard, IServiceProvider services) =>
            PlaygroundEndpointExtensions.RunAsync(id, guard, () => Task.FromResult(Capabilities(services))));
        endpoints.MapPost("/api/admin/playground/variants/{variantId:guid}/export", (Guid variantId, PlaygroundExportRequest? body, ICallerIdentity id, PlatformAdminGuard guard,
                PlaygroundStore store, PlaygroundCatalog catalog, IClock clock, IServiceProvider services, CancellationToken ct) =>
            PlaygroundEndpointExtensions.RunAsync(id, guard, () => ExportAsync(variantId, body, id.Resolve()!, store, catalog, clock, services, ct)));
        endpoints.MapGet("/api/admin/playground/variants/{variantId:guid}/exports", (Guid variantId, ICallerIdentity id, PlatformAdminGuard guard, PlaygroundStore store, CancellationToken ct) =>
            PlaygroundEndpointExtensions.RunAsync(id, guard, () => ListAsync(variantId, store, ct)));
        return endpoints;
    }

    /// <param name="Target"><c>pull_request</c> (a draft PR on the repository) or <c>bundle</c> (a zip of the same files).</param>
    public sealed record PlaygroundExportRequest(string? Target);

    private static IResult Capabilities(IServiceProvider services)
    {
        var publisher = services.GetService<GitHubPullRequestPublisher>();
        return Results.Ok(new { pullRequest = publisher?.IsConfigured == true, repository = publisher?.IsConfigured == true ? publisher.Repository : null });
    }

    private static async Task<IResult> ListAsync(Guid variantId, PlaygroundStore store, CancellationToken ct) =>
        Results.Ok(new
        {
            exports = (await store.ListExportsAsync(variantId, ct).ConfigureAwait(false)).Select(e => new
            {
                id = e.Id, target = e.Target, status = e.Status, branch = e.Branch, prNumber = e.PrNumber, prUrl = e.PrUrl, error = e.Error,
                files = JsonSerializer.Deserialize<JsonElement>(e.Files), createdAt = e.CreatedAt,
            }),
        });

    private static async Task<IResult> ExportAsync(
        Guid variantId, PlaygroundExportRequest? body, string actor, PlaygroundStore store, PlaygroundCatalog catalog, IClock clock, IServiceProvider services, CancellationToken ct)
    {
        var target = body?.Target?.Trim().ToLowerInvariant();
        if (target is not ("pull_request" or "bundle"))
        {
            return Results.BadRequest("'target' must be 'pull_request' or 'bundle'.");
        }

        var variant = await store.GetVariantAsync(variantId, ct).ConfigureAwait(false);
        if (variant is null)
        {
            return Results.NotFound();
        }

        PlaygroundOverrides? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<PlaygroundOverrides>(variant.Overrides, Json);
        }
        catch (JsonException)
        {
            parsed = null;
        }

        var (problem, overrides) = catalog.Validate(variant.Name, variant.Description, parsed);
        if (problem is not null)
        {
            return Results.BadRequest($"Variant '{variant.Name}' is not valid here: {problem}");
        }

        if (overrides.IsEmpty)
        {
            return Results.BadRequest("The variant changes nothing: there is nothing to propose.");
        }

        var now = clock.UtcNow;
        var (runs, judgements) = await EvidenceAsync(variant, store, ct).ConfigureAwait(false);
        var proposal = PlaygroundProposalBuilder.Build(variant, overrides, catalog.PromptCatalog(), runs, judgements, now);

        var record = new PlaygroundExportRow
        {
            Id = Guid.NewGuid(), VariantId = variant.Id, VariantName = variant.Name, Target = target, CreatedBy = actor, CreatedAt = now,
            Status = "failed", Files = "[]",
        };

        if (target == "bundle")
        {
            var files = BundleFiles(proposal);
            record.Status = "created";
            record.Files = JsonSerializer.Serialize(files.Select(f => f.Path), Json);
            await store.SaveExportAsync(record, ct).ConfigureAwait(false);
            return Results.Ok(new
            {
                exportId = record.Id, target, status = record.Status, files = files.Select(f => f.Path),
                fileName = $"{proposal.Folder.Split('/').Last()}.zip", zipBase64 = Convert.ToBase64String(Zip(files)),
            });
        }

        var publisher = services.GetService<GitHubPullRequestPublisher>();
        if (publisher is null || !publisher.IsConfigured)
        {
            return Results.Conflict(new { error = "GitHub is not configured in this environment (or is off here). Download the bundle instead.", githubConfigured = false });
        }

        try
        {
            var result = await PublishAsync(publisher, proposal, ct).ConfigureAwait(false);
            record.Status = "opened";
            record.Branch = proposal.Branch;
            record.PrNumber = result.Number;
            record.PrUrl = result.Url;
            record.Files = JsonSerializer.Serialize(result.Files, Json);
            await store.SaveExportAsync(record, ct).ConfigureAwait(false);
            return Results.Ok(new { exportId = record.Id, target, status = record.Status, prUrl = result.Url, prNumber = result.Number, branch = proposal.Branch, files = result.Files, notes = result.Notes });
        }
        catch (GitHubPublishException ex)
        {
            record.Error = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            record.Branch = proposal.Branch;
            await store.SaveExportAsync(record, ct).ConfigureAwait(false);
            return Results.Json(new { error = ex.Message, exportId = record.Id }, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>The proposal's files for a bundle: documents, the new orchestrator prompt file when there is one, and the apply guide.</summary>
    private static List<PlaygroundFileChange> BundleFiles(PlaygroundProposal proposal)
    {
        var files = new List<PlaygroundFileChange>(proposal.Documents);
        string? nextVersion = null;
        string? nextFile = null;
        if (proposal.OrchestratorPrompt is not null &&
            OrchestratorPromptEdit.NextVersion($"public const string Version = \"{AgentPrompt.Version}\";") is { } next)
        {
            nextVersion = next.NextVersion;
            nextFile = next.NextFile;
            files.Add(new PlaygroundFileChange(
                OrchestratorPromptEdit.PromptPath(next.NextFile),
                OrchestratorPromptEdit.PromptFile(next.NextVersion, proposal.OrchestratorPrompt, string.Empty), "orchestrator prompt"));
        }

        files.Add(new PlaygroundFileChange($"{proposal.Folder}/APPLY.md", PlaygroundProposalBuilder.ApplyGuide(proposal, nextVersion, nextFile), "apply guide"));
        return files;
    }

    private static byte[] Zip(IEnumerable<PlaygroundFileChange> files)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Path, CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(file.Content);
            }
        }

        return memory.ToArray();
    }

    private sealed record Published(int Number, string Url, IReadOnlyList<string> Files, IReadOnlyList<string> Notes);

    private static async Task<Published> PublishAsync(GitHubPullRequestPublisher publisher, PlaygroundProposal proposal, CancellationToken ct)
    {
        var baseBranch = await publisher.DefaultBranchAsync(ct).ConfigureAwait(false);
        var files = new List<PlaygroundFileChange>(proposal.Documents);
        var notes = new List<string>();

        // The orchestrator prompt: edit the code the way the drift test expects, from the files as they are on the base branch.
        if (proposal.OrchestratorPrompt is not null)
        {
            var code = await publisher.GetFileAsync(OrchestratorPromptEdit.CodePath, baseBranch, ct).ConfigureAwait(false);
            var test = await publisher.GetFileAsync(OrchestratorPromptEdit.TestPath, baseBranch, ct).ConfigureAwait(false);
            var next = code is null ? null : OrchestratorPromptEdit.NextVersion(code.Content);
            var previous = next is null ? null : await publisher.GetFileAsync(OrchestratorPromptEdit.PromptPath(next.Value.Current), baseBranch, ct).ConfigureAwait(false);
            string? failure = null;
            if (code is null || test is null || previous is null)
            {
                failure = "the orchestrator prompt files are not where this export expects them on the base branch";
            }
            else
            {
                var (changes, error, _) = OrchestratorPromptEdit.Apply(code.Content, previous.Content, test.Content, proposal.OrchestratorPrompt, proposal.Title);
                if (changes is null)
                {
                    failure = error;
                }
                else
                {
                    files.AddRange(changes);
                }
            }

            if (failure is not null)
            {
                notes.Add("The orchestrator prompt was not applied to the code: " + failure + ". Its proposed text is in the proposal folder.");
                files.Add(new PlaygroundFileChange($"{proposal.Folder}/APPLY.md", PlaygroundProposalBuilder.ApplyGuide(proposal, null, null) + $"\nNote: {failure}.\n", "apply guide"));
            }
        }

        var sha = await publisher.BranchShaAsync(baseBranch, ct).ConfigureAwait(false);
        await publisher.CreateBranchAsync(proposal.Branch, sha, ct).ConfigureAwait(false);
        try
        {
            foreach (var file in files)
            {
                var existing = await publisher.GetFileAsync(file.Path, proposal.Branch, ct).ConfigureAwait(false);
                await publisher.PutFileAsync(file.Path, file.Content, file.Message, proposal.Branch, existing?.Sha, ct).ConfigureAwait(false);
            }

            var body = proposal.Body + (notes.Count > 0 ? "\n" + string.Join("\n", notes.Select(n => "> " + n)) + "\n" : string.Empty);
            var (number, url) = await publisher.CreatePullRequestAsync(proposal.Title, proposal.Branch, baseBranch, body, ct).ConfigureAwait(false);
            return new Published(number, url, [.. files.Select(f => f.Path)], notes);
        }
        catch (GitHubPublishException ex)
        {
            throw new GitHubPublishException($"{ex.Message} (the branch '{proposal.Branch}' was created and can be deleted).");
        }
    }

    // ----- evidence -----

    private static async Task<(List<ExportRunEvidence> Runs, List<ExportJudgementEvidence> Judgements)> EvidenceAsync(
        PlaygroundVariantRow variant, PlaygroundStore store, CancellationToken ct)
    {
        var recent = await store.ListRunsAsync(200, ct).ConfigureAwait(false);
        var variantRuns = recent.Where(r => r.VariantId == variant.Id).Take(12).ToList();
        var runs = new List<ExportRunEvidence>();
        var questions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var run in variantRuns)
        {
            var question = QuestionOf(run.Input);
            questions[run.Id.ToString()] = question;
            runs.Add(Evidence(run, variant.Name, question));
            if (run.ComparisonId is { } comparison)
            {
                foreach (var sibling in recent.Where(r => r.ComparisonId == comparison && r.VariantId is null && r.Id != run.Id))
                {
                    runs.Add(Evidence(sibling, "Baseline", question));
                }
            }
        }

        var judgements = new List<ExportJudgementEvidence>();
        var seen = new HashSet<Guid>();
        foreach (var run in variantRuns.Take(10))
        {
            foreach (var row in await store.ListJudgementsAsync(null, run.Id, ct).ConfigureAwait(false))
            {
                if (!seen.Add(row.Id))
                {
                    continue;
                }

                using var view = JsonDocument.Parse(row.Result);
                var root = view.RootElement;
                var answers = root.GetProperty("answers").EnumerateArray().ToList();
                string NameOf(string id) => answers.FirstOrDefault(a => a.GetProperty("runId").GetString() == id).GetProperty("variantName").GetString() ?? "?";
                var preferredId = root.GetProperty("preferredRunId").ValueKind == JsonValueKind.String ? root.GetProperty("preferredRunId").GetString() : null;
                judgements.Add(new ExportJudgementEvidence(
                    row.Workflow,
                    questions.GetValueOrDefault(run.Id.ToString(), string.Empty),
                    root.GetProperty("rubric").GetProperty("name").GetString() ?? "?",
                    root.GetProperty("judgeModel").ValueKind == JsonValueKind.String ? root.GetProperty("judgeModel").GetString() : null,
                    root.GetProperty("passes").GetInt32(),
                    root.GetProperty("passesAgreed").GetBoolean(),
                    root.GetProperty("tie").GetBoolean(),
                    preferredId is null ? null : NameOf(preferredId),
                    [.. answers.Select(a => (a.GetProperty("variantName").GetString() ?? "?", a.GetProperty("weighted").GetDouble()))]));
            }
        }

        return (runs, judgements);
    }

    private static ExportRunEvidence Evidence(PlaygroundRunSummaryRow run, string side, string question) =>
        new(run.Workflow, run.SandboxKind, question, side, run.DurationMs, run.EstimatedCostUsd, run.Status);

    private static string QuestionOf(string inputJson)
    {
        try
        {
            using var document = JsonDocument.Parse(inputJson);
            return document.RootElement.TryGetProperty("question", out var q) && q.ValueKind == JsonValueKind.String ? q.GetString() ?? string.Empty : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
