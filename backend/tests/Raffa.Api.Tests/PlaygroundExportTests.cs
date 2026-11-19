using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raffa.AiFlows.Ask.Agentic;
using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.AiGateway.Logging;
using Raffa.Api.Infrastructure;
using Raffa.Api.Tests.TestSupport;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.Suppliers.Products.Infrastructure;

namespace Raffa.Api.Tests;

internal static class RepoFiles
{
    /// <summary>The repository root, found from the test binaries.</summary>
    public static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "backend", "src", "Raffa.AiFlows", "Ask", "Agentic", "AgentPrompt.cs")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    public static string Read(string relative) => File.ReadAllText(Path.Combine(Root(), relative.Replace('/', Path.DirectorySeparatorChar)));
}

/// <summary>The text diff and the orchestrator edit that make a proposal reviewable and keep the drift test green.</summary>
public sealed class PlaygroundExportUnitTests
{
    [Fact]
    public void A_diff_shows_changed_lines_with_context_and_nothing_when_text_is_the_same()
    {
        Assert.Equal(string.Empty, PlaygroundTextDiff.Unified("a\nb\nc", "a\nb\nc", "old", "new").Unified);

        var (diff, added, removed) = PlaygroundTextDiff.Unified("one\ntwo\nthree\nfour\nfive", "one\ntwo\nTHREE\nfour\nfive\nsix", "old", "new", context: 1);

        Assert.Equal(2, added);
        Assert.Equal(1, removed);
        Assert.Contains("--- old", diff, StringComparison.Ordinal);
        Assert.Contains("-three", diff, StringComparison.Ordinal);
        Assert.Contains("+THREE", diff, StringComparison.Ordinal);
        Assert.Contains("+six", diff, StringComparison.Ordinal);
        Assert.Contains(" two", diff, StringComparison.Ordinal);
        Assert.DoesNotContain(" one", diff, StringComparison.Ordinal);
    }

    private static string Unindent(string rawStringBody) =>
        string.Join("\n", rawStringBody.Split('\n').Select(l => l.StartsWith("        ", StringComparison.Ordinal) ? l[8..] : l));

    [Fact]
    public void The_orchestrator_edit_bumps_the_version_and_keeps_the_prompt_file_the_constant_and_the_drift_test_in_step()
    {
        var code = RepoFiles.Read("backend/src/Raffa.AiFlows/Ask/Agentic/AgentPrompt.cs");
        var previous = RepoFiles.Read("backend/src/Raffa.AiFlows/Ask/Prompts/agent/v1.6.md");
        var test = RepoFiles.Read("backend/tests/Raffa.AiFlows.Tests/Ask/Agentic/AgenticAskTurnTests.cs");
        var newPrompt = AgentPrompt.SystemPrompt + "\n\nPlayground rule: answer in at most five sentences.";

        var (changes, error, version) = OrchestratorPromptEdit.Apply(code, previous, test, newPrompt, "Shorter answers <test> & more");

        Assert.Null(error);
        Assert.Equal("agent-v1.7", version);
        Assert.Equal(
            ["backend/src/Raffa.AiFlows/Ask/Prompts/agent/v1.7.md", OrchestratorPromptEdit.CodePath, OrchestratorPromptEdit.TestPath],
            changes!.Select(c => c.Path).ToArray());

        Assert.NotNull(changes);
        var updatedCode = changes.Single(c => c.Path == OrchestratorPromptEdit.CodePath).Content;
        Assert.Contains("public const string Version = \"agent-v1.7\";", updatedCode, StringComparison.Ordinal);
        Assert.Contains("Prompts/agent/v1.7.md", updatedCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Prompts/agent/v1.6.md", updatedCode, StringComparison.Ordinal);
        Assert.Contains("/// v1.7: Shorter answers (test) and more", updatedCode, StringComparison.Ordinal);

        // The C# raw string, un-indented, is the new prompt exactly.
        var literal = Regex.Match(updatedCode, "public const string SystemPrompt =\n        \"\"\"\n(?<body>.*?)\n        \"\"\";", RegexOptions.Singleline);
        Assert.True(literal.Success);
        Assert.Equal(OrchestratorPromptEdit.Normalize(newPrompt), Unindent(literal.Groups["body"].Value));

        // The new file has the shape the drift test reads: title, blank line, body, footer.
        var file = changes.Single(c => c.Path.EndsWith("v1.7.md", StringComparison.Ordinal)).Content;
        Assert.StartsWith("# Ask Raffa — orchestrator persona (agent-v1.7)\n\n", file, StringComparison.Ordinal);
        var start = file.IndexOf("\n\n", StringComparison.Ordinal) + 2;
        var end = file.LastIndexOf("\nBump the version", StringComparison.Ordinal);
        Assert.Equal(OrchestratorPromptEdit.Normalize(newPrompt), file[start..end].TrimEnd());

        var updatedTest = changes.Single(c => c.Path == OrchestratorPromptEdit.TestPath).Content;
        Assert.DoesNotContain("\"v1.6.md\"", updatedTest, StringComparison.Ordinal);
        Assert.Contains("\"v1.7.md\"", updatedTest, StringComparison.Ordinal);
    }

    [Fact]
    public void The_orchestrator_edit_refuses_a_repository_that_no_longer_has_the_shape_it_knows()
    {
        var code = RepoFiles.Read("backend/src/Raffa.AiFlows/Ask/Agentic/AgentPrompt.cs");
        var previous = RepoFiles.Read("backend/src/Raffa.AiFlows/Ask/Prompts/agent/v1.6.md");
        var test = RepoFiles.Read("backend/tests/Raffa.AiFlows.Tests/Ask/Agentic/AgenticAskTurnTests.cs");

        Assert.Contains("triple quote", OrchestratorPromptEdit.Apply(code, previous, test, "has \"\"\" inside", "n").Error);
        Assert.Contains("Version", OrchestratorPromptEdit.Apply(code.Replace("agent-v1.6", "agent-next", StringComparison.Ordinal), previous, test, "p", "n").Error);
        Assert.Contains("drift test", OrchestratorPromptEdit.Apply(code, previous, test.Replace("v1.6.md", "other.md", StringComparison.Ordinal), "p", "n").Error);
        Assert.Contains("raw string", OrchestratorPromptEdit.Apply(code.Replace("\"\"\"", "\"x\"", StringComparison.Ordinal), previous, test, "p", "n").Error);
    }

    [Fact]
    public void The_proposal_lists_what_changes_where_to_set_it_and_the_evidence()
    {
        var variant = new PlaygroundVariantRow { Id = Guid.NewGuid(), Name = "Short & sharp", Description = "Fewer words, cheaper model" };
        var overrides = new Raffa.AiGateway.Telemetry.PlaygroundOverrides(
            Prompts: new Dictionary<string, string> { ["agent"] = "NEW ORCHESTRATOR", ["contract-analyst"] = "NEW ANALYST" },
            Models: new Dictionary<string, Raffa.AiGateway.Telemetry.PlaygroundModelOverride> { ["agent"] = new("gpt-5.4-nano-dev", "low", 2048, null) },
            DisabledTools: ["web_research"],
            MaxToolCalls: 4);
        var baseline = new (string, string, string, string)[]
        {
            ("agent", "ask", "agent-v1.6", "OLD ORCHESTRATOR"),
            ("contract-analyst", "council", "council-v3", "OLD ANALYST"),
        };
        var runs = new[] { new ExportRunEvidence("ask", "seeded", "Quando scade | Salesforce?", "Short & sharp", 900, 0.002m, "ok"), new ExportRunEvidence("ask", "seeded", "Quando scade | Salesforce?", "Baseline", 1500, null, "ok") };
        var judgements = new[]
        {
            new ExportJudgementEvidence("ask", "Quando scade?", "Ask answer", "gpt-5.4-dev", 2, false, false, "Short & sharp", [("Baseline", 3.1), ("Short & sharp", 4.6)]),
        };

        var proposal = PlaygroundProposalBuilder.Build(variant, overrides, baseline, runs, judgements, new DateTimeOffset(2026, 11, 19, 10, 30, 0, TimeSpan.Zero));

        Assert.Equal("docs/playground/proposals/2026-11-19-short-sharp", proposal.Folder);
        Assert.StartsWith("playground/short-sharp-20261119-103000", proposal.Branch, StringComparison.Ordinal);
        Assert.Equal("NEW ORCHESTRATOR", proposal.OrchestratorPrompt);
        Assert.Equal(["contract-analyst"], proposal.ManualPrompts);
        var readme = proposal.Documents.Single(d => d.Path.EndsWith("/README.md", StringComparison.Ordinal)).Content;
        Assert.Contains("AiGateway__Models__Agent__ModelId", readme, StringComparison.Ordinal);
        Assert.Contains("AiGateway__Models__Agent__MaxCompletionTokens", readme, StringComparison.Ordinal);
        Assert.Contains("Chat__AskTurn__AgenticMaxToolCalls", readme, StringComparison.Ordinal);
        Assert.Contains("`web_research`", readme, StringComparison.Ordinal);
        Assert.Contains(Raffa.AiGateway.Agents.AgentDefinition.HashPrompt("NEW ANALYST"), readme, StringComparison.Ordinal);
        Assert.Contains("the verdict changed with the order", readme, StringComparison.Ordinal);
        Assert.Contains("Quando scade / Salesforce?", readme, StringComparison.Ordinal);
        Assert.Contains(proposal.Documents, d => d.Path.EndsWith("/prompts/agent.diff", StringComparison.Ordinal) && d.Content.Contains("-OLD ORCHESTRATOR", StringComparison.Ordinal));
        Assert.Contains("draft", proposal.Body, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>A stand-in for the GitHub REST API: serves the orchestrator files of the real repository on the base branch, records what is written.</summary>
internal sealed class FakeGitHub : HttpMessageHandler
{
    public const string BaseBranch = "main";

    public List<(HttpMethod Method, string Path, string? Body)> Calls { get; } = [];

    public HttpStatusCode? FailWrites { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery.TrimStart('/'));
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Calls)
        {
            Calls.Add((request.Method, path, body));
        }

        if (request.Method == HttpMethod.Get && path == "repos/acme/raffa")
        {
            return Ok("""{"default_branch":"main"}""");
        }

        if (request.Method == HttpMethod.Get && path == "repos/acme/raffa/git/ref/heads/main")
        {
            return Ok("""{"object":{"sha":"abc123"}}""");
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("repos/acme/raffa/contents/", StringComparison.Ordinal))
        {
            var file = path["repos/acme/raffa/contents/".Length..];
            var query = file.IndexOf('?', StringComparison.Ordinal);
            var branch = file[(query + 1)..].Replace("ref=", string.Empty, StringComparison.Ordinal);
            file = file[..query];
            if (branch != BaseBranch || !File.Exists(Path.Combine(RepoFiles.Root(), file.Replace('/', Path.DirectorySeparatorChar))))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"message":"Not Found"}""") };
            }

            var content = Convert.ToBase64String(Encoding.UTF8.GetBytes(RepoFiles.Read(file)));
            return Ok(JsonSerializer.Serialize(new { content, sha = "sha-" + Path.GetFileName(file) }));
        }

        if (request.Method == HttpMethod.Put && FailWrites is { } status)
        {
            return new HttpResponseMessage(status) { Content = new StringContent("""{"message":"Resource not accessible by personal access token"}""") };
        }

        if (request.Method == HttpMethod.Post && path == "repos/acme/raffa/pulls")
        {
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("""{"number":7,"html_url":"https://github.com/acme/raffa/pull/7"}""") };
        }

        return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{}") };
    }

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

/// <summary>The export of a variant to the repository: a draft pull request through the feedback connection, or a bundle when GitHub is not there.</summary>
public sealed class PlaygroundExportEndpointTests(RaffaApiFactory factory) : IClassFixture<RaffaApiFactory>
{
    private const string Staff = "staff@raffa.example";
    private const string Customer = "someone@customer.example";

    private WebApplicationFactory<Program> Host(FakeGitHub? github = null) =>
        factory
            .WithPresentedCallersAsMembers()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("PlatformAdmin:ObjectIds:0", Staff);
                builder.UseSetting("ConnectionStrings:Chat", "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true");
                if (github is not null)
                {
                    builder.UseSetting("Feedback:GitHub:Enabled", "true");
                    builder.UseSetting("Feedback:GitHub:Token", "test-token");
                    builder.UseSetting("Feedback:GitHub:Repository", "acme/raffa");
                }
            })
            .WithInMemoryAskEngine(
                new RecordingAiGateway(new TraceRecordingAiGateway(new FixtureAiGateway(
                    new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions(), new AskAgentFixtureScript()))),
                new FixedClock(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero)))
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<AskTurnOptions>();
                services.AddSingleton(new AskTurnOptions { Agentic = true });
                var suppliersDb = $"suppliers-{Guid.NewGuid()}";
                services.RemoveAll<DbContextOptions<SuppliersDbContext>>();
                services.RemoveAll<SuppliersDbContext>();
                services.AddDbContext<SuppliersDbContext>(o => o.UseInMemoryDatabase(suppliersDb).UseInternalServiceProvider(InMemoryAskEngineFactory.InMemoryProviderServices));
                if (github is not null)
                {
                    services.AddHttpClient<GitHubPullRequestPublisher>().ConfigurePrimaryHttpMessageHandler(() => github);
                }
            }));

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string user, object? body = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("X-User-Id", user);
        return client.SendAsync(request);
    }

    private static async Task<Guid> VariantAsync(HttpClient client, object overrides, string name = "Short & sharp")
    {
        var response = await SendAsync(client, HttpMethod.Post, "/api/admin/playground/variants", Staff, new { name, description = "Fewer words", overrides });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static object Overrides() => new
    {
        prompts = new Dictionary<string, string> { ["agent"] = "You are Ask Raffa. Be brief.", ["contract-analyst"] = "You analyse contracts tersely." },
        maxToolCalls = 4,
    };

    [Fact]
    public async Task Export_is_for_the_platform_admin_only_and_needs_a_valid_target_and_a_variant_with_changes()
    {
        var client = Host().CreateClient();
        var variantId = await VariantAsync(client, Overrides());
        var empty = await VariantAsync(client, new { }, "Nothing");

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, $"/api/admin/playground/variants/{variantId}/export", Customer, new { target = "bundle" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, "/api/admin/playground/export", Customer)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Post, $"/api/admin/playground/variants/{variantId}/export", Staff, new { target = "email" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Post, $"/api/admin/playground/variants/{empty}/export", Staff, new { target = "bundle" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, $"/api/admin/playground/variants/{Guid.NewGuid()}/export", Staff, new { target = "bundle" })).StatusCode);
    }

    [Fact]
    public async Task Without_github_the_pull_request_is_unavailable_and_a_bundle_carries_the_same_files()
    {
        var client = Host().CreateClient();
        var variantId = await VariantAsync(client, Overrides());

        using var capabilities = JsonDocument.Parse(await (await SendAsync(client, HttpMethod.Get, "/api/admin/playground/export", Staff)).Content.ReadAsStringAsync());
        Assert.False(capabilities.RootElement.GetProperty("pullRequest").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(client, HttpMethod.Post, $"/api/admin/playground/variants/{variantId}/export", Staff, new { target = "pull_request" })).StatusCode);

        var response = await SendAsync(client, HttpMethod.Post, $"/api/admin/playground/variants/{variantId}/export", Staff, new { target = "bundle" });
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {raw}");
        using var json = JsonDocument.Parse(raw);
        using var zip = new ZipArchive(new MemoryStream(Convert.FromBase64String(json.RootElement.GetProperty("zipBase64").GetString()!)));
        var entries = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains(entries, e => e.EndsWith("/README.md", StringComparison.Ordinal));
        Assert.Contains(entries, e => e.EndsWith("/prompts/agent.md", StringComparison.Ordinal));
        Assert.Contains(entries, e => e.EndsWith("/prompts/agent.diff", StringComparison.Ordinal));
        Assert.Contains(entries, e => e.EndsWith("/prompts/contract-analyst.md", StringComparison.Ordinal));
        Assert.Contains(entries, e => e.EndsWith("/APPLY.md", StringComparison.Ordinal));
        Assert.Contains("backend/src/Raffa.AiFlows/Ask/Prompts/agent/v1.7.md", entries);
        var agentPrompt = new StreamReader(zip.GetEntry("backend/src/Raffa.AiFlows/Ask/Prompts/agent/v1.7.md")!.Open()).ReadToEnd();
        Assert.Contains("You are Ask Raffa. Be brief.", agentPrompt, StringComparison.Ordinal);
        Assert.Contains("(agent-v1.7)", agentPrompt, StringComparison.Ordinal);
        Assert.Contains("Chat__AskTurn__AgenticMaxToolCalls", new StreamReader(zip.Entries.First(e => e.FullName.EndsWith("/README.md", StringComparison.Ordinal)).Open()).ReadToEnd(), StringComparison.Ordinal);

        using var listed = JsonDocument.Parse(await (await SendAsync(client, HttpMethod.Get, $"/api/admin/playground/variants/{variantId}/exports", Staff)).Content.ReadAsStringAsync());
        Assert.Equal("bundle", listed.RootElement.GetProperty("exports")[0].GetProperty("target").GetString());
    }

    [Fact]
    public async Task A_pull_request_is_a_draft_on_a_new_branch_with_the_report_the_prompts_and_the_orchestrator_version_bump()
    {
        var github = new FakeGitHub();
        var client = Host(github).CreateClient();
        var variantId = await VariantAsync(client, Overrides());

        using var capabilities = JsonDocument.Parse(await (await SendAsync(client, HttpMethod.Get, "/api/admin/playground/export", Staff)).Content.ReadAsStringAsync());
        Assert.True(capabilities.RootElement.GetProperty("pullRequest").GetBoolean());
        Assert.Equal("acme/raffa", capabilities.RootElement.GetProperty("repository").GetString());

        var response = await SendAsync(client, HttpMethod.Post, $"/api/admin/playground/variants/{variantId}/export", Staff, new { target = "pull_request" });
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {raw}");
        using var result = JsonDocument.Parse(raw);
        Assert.Equal("https://github.com/acme/raffa/pull/7", result.RootElement.GetProperty("prUrl").GetString());
        var branch = result.RootElement.GetProperty("branch").GetString()!;
        Assert.StartsWith("playground/short-sharp-", branch, StringComparison.Ordinal);

        var calls = github.Calls.ToList();
        // A new branch from the base commit; never a write to the base branch.
        var createBranch = Assert.Single(calls, c => c.Method == HttpMethod.Post && c.Path == "repos/acme/raffa/git/refs");
        using (var body = JsonDocument.Parse(createBranch.Body!))
        {
            Assert.Equal("refs/heads/" + branch, body.RootElement.GetProperty("ref").GetString());
            Assert.Equal("abc123", body.RootElement.GetProperty("sha").GetString());
        }

        var writes = calls.Where(c => c.Method == HttpMethod.Put).Select(c => (c.Path["repos/acme/raffa/contents/".Length..], Body: JsonDocument.Parse(c.Body!).RootElement.Clone())).ToList();
        Assert.All(writes, w => Assert.Equal(branch, w.Body.GetProperty("branch").GetString()));
        string Written(string path) => Encoding.UTF8.GetString(Convert.FromBase64String(writes.Single(w => w.Item1 == path).Body.GetProperty("content").GetString()!));
        Assert.Contains(writes, w => w.Item1.EndsWith("/README.md", StringComparison.Ordinal));
        Assert.Contains("agent-v1.7", Written(OrchestratorPromptEdit.CodePath), StringComparison.Ordinal);
        Assert.Contains("Be brief.", Written(OrchestratorPromptEdit.CodePath), StringComparison.Ordinal);
        Assert.Contains("\"v1.7.md\"", Written(OrchestratorPromptEdit.TestPath), StringComparison.Ordinal);
        Assert.Contains("Be brief.", Written("backend/src/Raffa.AiFlows/Ask/Prompts/agent/v1.7.md"), StringComparison.Ordinal);

        var pull = Assert.Single(calls, c => c.Method == HttpMethod.Post && c.Path == "repos/acme/raffa/pulls");
        using var pullBody = JsonDocument.Parse(pull.Body!);
        Assert.True(pullBody.RootElement.GetProperty("draft").GetBoolean());
        Assert.Equal("main", pullBody.RootElement.GetProperty("base").GetString());
        Assert.Equal(branch, pullBody.RootElement.GetProperty("head").GetString());
        Assert.Contains("contract-analyst", pullBody.RootElement.GetProperty("body").GetString(), StringComparison.Ordinal);

        using var listed = JsonDocument.Parse(await (await SendAsync(client, HttpMethod.Get, $"/api/admin/playground/variants/{variantId}/exports", Staff)).Content.ReadAsStringAsync());
        var export = listed.RootElement.GetProperty("exports")[0];
        Assert.Equal("opened", export.GetProperty("status").GetString());
        Assert.Equal(7, export.GetProperty("prNumber").GetInt32());
    }

    [Fact]
    public async Task A_token_that_cannot_write_to_the_repository_fails_with_the_reason_and_leaves_a_record()
    {
        var github = new FakeGitHub { FailWrites = HttpStatusCode.Forbidden };
        var client = Host(github).CreateClient();
        var variantId = await VariantAsync(client, Overrides());

        var response = await SendAsync(client, HttpMethod.Post, $"/api/admin/playground/variants/{variantId}/export", Staff, new { target = "pull_request" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains("Contents and Pull requests", raw, StringComparison.Ordinal);
        Assert.Contains("can be deleted", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("test-token", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(github.Calls, c => c.Path == "repos/acme/raffa/pulls");

        using var listed = JsonDocument.Parse(await (await SendAsync(client, HttpMethod.Get, $"/api/admin/playground/variants/{variantId}/exports", Staff)).Content.ReadAsStringAsync());
        var export = listed.RootElement.GetProperty("exports")[0];
        Assert.Equal("failed", export.GetProperty("status").GetString());
        Assert.Contains("Contents", export.GetProperty("error").GetString(), StringComparison.Ordinal);
    }
}
