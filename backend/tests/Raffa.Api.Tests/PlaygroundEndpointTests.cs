using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.AiGateway.Logging;
using Raffa.Api.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.Suppliers.Products.Infrastructure;

namespace Raffa.Api.Tests;

/// <summary>
/// The Super Admin agent playground (docs/architecture/admin-agent-playground-plan.md, PR 1) on the in-memory
/// Ask host with the agentic orchestrator and the fixture gateway: only the allowlisted platform admin reaches it,
/// the three sandbox workspaces are created on first use, and a run returns the reply with the trace of what the
/// agent did (model calls, tool calls) and is stored for later.
/// </summary>
public sealed class PlaygroundEndpointTests(RaffaApiFactory factory) : IClassFixture<RaffaApiFactory>
{
    private const string Staff = "staff@raffa.example";
    private const string Customer = "someone@customer.example";

    private WebApplicationFactory<Program> Host() =>
        factory
            .WithPresentedCallersAsMembers()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("PlatformAdmin:ObjectIds:0", Staff);
                builder.UseSetting(
                    "ConnectionStrings:Chat",
                    "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true");
            })
            .WithInMemoryAskEngine(
                new RecordingAiGateway(new TraceRecordingAiGateway(new FixtureAiGateway(
                    new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions(), new AskAgentFixtureScript()))),
                new FixedClock(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero)))
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<AskTurnOptions>();
                services.AddSingleton(new AskTurnOptions { Agentic = true });

                // The sandbox seeder writes suppliers: keep that store in memory too (as the Ask contexts are).
                var suppliersDb = $"suppliers-{Guid.NewGuid()}";
                services.RemoveAll<DbContextOptions<SuppliersDbContext>>();
                services.RemoveAll<SuppliersDbContext>();
                services.AddDbContext<SuppliersDbContext>(o => o
                    .UseInMemoryDatabase(suppliersDb)
                    .UseInternalServiceProvider(InMemoryAskEngineFactory.InMemoryProviderServices));
            }));

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string user, object? body = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("X-User-Id", user);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Only_a_platform_admin_reaches_the_playground()
    {
        var client = Host().CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, "/api/admin/playground/sandboxes", Customer)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, "/api/admin/playground/runs", Customer)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await SendAsync(client, HttpMethod.Post, "/api/admin/playground/runs", Customer, new { sandbox = "seeded", question = "Ciao" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/admin/playground/sandboxes", Staff)).StatusCode);
    }

    [Fact]
    public async Task The_three_sandboxes_exist_and_the_seeded_one_holds_four_contracts()
    {
        var client = Host().CreateClient();

        var response = await SendAsync(client, HttpMethod.Get, "/api/admin/playground/sandboxes", Staff);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var sandboxes = json.RootElement.GetProperty("sandboxes").EnumerateArray().ToDictionary(s => s.GetProperty("kind").GetString()!, s => s.GetProperty("contracts").GetInt32());

        Assert.Equal(new Dictionary<string, int> { ["empty"] = 0, ["seeded"] = 4, ["needs-review"] = 1 }, sandboxes);
    }

    [Fact]
    public async Task A_run_returns_the_reply_with_its_trace_and_is_stored()
    {
        var client = Host().CreateClient();

        var response = await SendAsync(client, HttpMethod.Post, "/api/admin/playground/runs", Staff,
            new { sandbox = "seeded", question = "Quando scade il contratto Salesforce?" });
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {raw}");

        using var run = JsonDocument.Parse(raw);
        Assert.Equal("ok", run.RootElement.GetProperty("status").GetString());
        Assert.Equal("answer", run.RootElement.GetProperty("reply").GetProperty("kind").GetString());

        var trace = run.RootElement.GetProperty("trace").EnumerateArray().ToList();
        var tools = trace.Where(e => e.GetProperty("kind").GetString() == "tool_call").Select(e => e.GetProperty("name").GetString()).ToList();
        Assert.Contains("list_portfolio", tools);
        Assert.Contains("get_contract", tools);
        Assert.Contains(trace, e => e.GetProperty("kind").GetString() == "model_call" && e.GetProperty("name").GetString() == "converse:ask-orchestrator");
        Assert.All(trace.Where(e => e.GetProperty("kind").GetString() == "tool_call"), e => Assert.False(string.IsNullOrEmpty(e.GetProperty("output").GetString())));

        var runId = run.RootElement.GetProperty("runId").GetGuid();
        var stored = await SendAsync(client, HttpMethod.Get, $"/api/admin/playground/runs/{runId}", Staff);
        Assert.Equal(HttpStatusCode.OK, stored.StatusCode);
        using var storedJson = JsonDocument.Parse(await stored.Content.ReadAsStringAsync());
        Assert.Equal(trace.Count, storedJson.RootElement.GetProperty("trace").GetArrayLength());

        var list = await SendAsync(client, HttpMethod.Get, "/api/admin/playground/runs", Staff);
        Assert.Contains(runId.ToString(), await list.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<JsonDocument> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static async Task<Guid> CreateVariantAsync(HttpClient client, object overrides, string name = "Variant")
    {
        var response = await SendAsync(client, HttpMethod.Post, "/api/admin/playground/variants", Staff, new { name, overrides });
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{response.StatusCode}: {raw}");
        using var json = JsonDocument.Parse(raw);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static JsonElement ConverseCall(JsonDocument run) =>
        run.RootElement.GetProperty("trace").EnumerateArray().First(e => e.GetProperty("name").GetString() == "converse:ask-orchestrator");

    [Fact]
    public async Task The_baseline_lists_the_prompt_models_and_tools_a_variant_can_change()
    {
        var client = Host().CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, "/api/admin/playground/baseline", Customer)).StatusCode);
        using var baseline = await JsonOf(await SendAsync(client, HttpMethod.Get, "/api/admin/playground/baseline", Staff));

        var prompt = baseline.RootElement.GetProperty("prompts")[0];
        Assert.Equal("agent", prompt.GetProperty("agent").GetString());
        Assert.Contains("senior procurement consultant", prompt.GetProperty("text").GetString());
        Assert.Contains(baseline.RootElement.GetProperty("tools").EnumerateArray(), t => t.GetProperty("name").GetString() == "get_contract");
        Assert.Contains(baseline.RootElement.GetProperty("models").EnumerateArray(), m => m.GetProperty("role").GetString() == "agent");
        Assert.NotEmpty(baseline.RootElement.GetProperty("allowedModelIds").EnumerateArray());
    }

    [Fact]
    public async Task A_variant_is_refused_when_it_names_something_this_environment_does_not_have()
    {
        var client = Host().CreateClient();

        async Task<HttpStatusCode> TryAsync(object overrides) =>
            (await SendAsync(client, HttpMethod.Post, "/api/admin/playground/variants", Staff, new { name = "x", overrides })).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(new { prompts = new { nobody = "text" } }));
        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(new { models = new { agent = new { modelId = "gpt-not-deployed" } } }));
        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(new { models = new { agent = new { temperature = 0.9 } } }));
        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(new { models = new { agent = new { reasoningEffort = "extreme" } } }));
        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(new { disabledTools = new[] { "no_such_tool" } }));
        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(new { maxToolCalls = 99 }));
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await SendAsync(client, HttpMethod.Post, "/api/admin/playground/variants", Staff, new { name = " ", overrides = new { } })).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await SendAsync(client, HttpMethod.Post, "/api/admin/playground/variants", Customer, new { name = "x", overrides = new { } })).StatusCode);
    }

    [Fact]
    public async Task A_variant_can_be_saved_edited_listed_and_archived()
    {
        var client = Host().CreateClient();
        var id = await CreateVariantAsync(client, new { maxToolCalls = 3 }, "Short loop");

        var edited = await SendAsync(client, HttpMethod.Put, $"/api/admin/playground/variants/{id}", Staff,
            new { name = "Shorter loop", description = "two tool calls at most", overrides = new { maxToolCalls = 2 } });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        using var list = await JsonOf(await SendAsync(client, HttpMethod.Get, "/api/admin/playground/variants", Staff));
        var saved = list.RootElement.GetProperty("variants").EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == id);
        Assert.Equal("Shorter loop", saved.GetProperty("name").GetString());
        Assert.Equal(2, saved.GetProperty("overrides").GetProperty("maxToolCalls").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"/api/admin/playground/variants/{id}", Staff)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Put, $"/api/admin/playground/variants/{id}", Staff, new { name = "x", overrides = new { } })).StatusCode);
        using var after = await JsonOf(await SendAsync(client, HttpMethod.Get, "/api/admin/playground/variants", Staff));
        Assert.DoesNotContain(after.RootElement.GetProperty("variants").EnumerateArray(), v => v.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task A_variant_changes_what_the_model_is_sent_and_the_baseline_does_not()
    {
        var client = Host().CreateClient();
        var variantId = await CreateVariantAsync(
            client,
            new { prompts = new { agent = "CUSTOM PLAYGROUND PROMPT" }, disabledTools = new[] { "web_research", "get_contract" } },
            "No contract tool");
        var comparisonId = Guid.NewGuid();

        using var baseline = await JsonOf(await SendAsync(client, HttpMethod.Post, "/api/admin/playground/runs", Staff,
            new { sandbox = "seeded", question = "Quando scade il contratto Salesforce?", comparisonId }));
        using var variant = await JsonOf(await SendAsync(client, HttpMethod.Post, "/api/admin/playground/runs", Staff,
            new { sandbox = "seeded", question = "Quando scade il contratto Salesforce?", variantId, comparisonId }));

        var baselineRequest = ConverseCall(baseline).GetProperty("input").GetString()!;
        var variantRequest = ConverseCall(variant).GetProperty("input").GetString()!;
        Assert.DoesNotContain("CUSTOM PLAYGROUND PROMPT", baselineRequest, StringComparison.Ordinal);
        Assert.Contains("senior procurement consultant", baselineRequest, StringComparison.Ordinal);
        Assert.Contains("CUSTOM PLAYGROUND PROMPT", variantRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("senior procurement consultant", variantRequest, StringComparison.Ordinal);
        Assert.Contains("\"get_contract\"", baselineRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("\"get_contract\"", variantRequest, StringComparison.Ordinal);
        Assert.Contains("playground", ConverseCall(variant).GetProperty("note").GetString(), StringComparison.Ordinal);

        Assert.Equal(variantId, variant.RootElement.GetProperty("variantId").GetGuid());
        Assert.Equal(comparisonId, variant.RootElement.GetProperty("comparisonId").GetGuid());
        Assert.Equal(JsonValueKind.Null, baseline.RootElement.GetProperty("variantId").ValueKind);

        // A variant never leaks into the next run: the override ended with the request that opened it.
        using var again = await JsonOf(await SendAsync(client, HttpMethod.Post, "/api/admin/playground/runs", Staff,
            new { sandbox = "seeded", question = "Quando scade il contratto Salesforce?" }));
        Assert.DoesNotContain("CUSTOM PLAYGROUND PROMPT", ConverseCall(again).GetProperty("input").GetString()!, StringComparison.Ordinal);

        // The run keeps its own copy of the variant it used.
        using var stored = await JsonOf(await SendAsync(client, HttpMethod.Get, $"/api/admin/playground/runs/{variant.RootElement.GetProperty("runId").GetGuid()}", Staff));
        Assert.Equal("No contract tool", stored.RootElement.GetProperty("variantName").GetString());
        Assert.Equal("CUSTOM PLAYGROUND PROMPT", stored.RootElement.GetProperty("input").GetProperty("variant").GetProperty("overrides").GetProperty("prompts").GetProperty("agent").GetString());
    }

    private static async Task<JsonDocument> RunWorkflowAsync(HttpClient client, string workflow, object input, string sandbox = "seeded", Guid? variantId = null)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/api/admin/playground/runs", Staff, new { workflow, sandbox, input, variantId });
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {raw}");
        return JsonDocument.Parse(raw);
    }

    private static IEnumerable<JsonElement> ModelCalls(JsonDocument run) =>
        run.RootElement.GetProperty("trace").EnumerateArray().Where(e => e.GetProperty("kind").GetString() == "model_call");

    [Fact]
    public async Task The_council_runs_on_a_sandbox_contract_and_shows_the_analysts_and_their_evidence()
    {
        var client = Host().CreateClient();

        using var run = await RunWorkflowAsync(client, "council", new { supplier = "salesforce", goal = "reduce 20k" });

        Assert.Equal("ok", run.RootElement.GetProperty("status").GetString());
        Assert.Equal("council", run.RootElement.GetProperty("workflow").GetString());
        var result = run.RootElement.GetProperty("result");
        Assert.Equal("Salesforce", result.GetProperty("supplier").GetString());
        Assert.NotEmpty(result.GetProperty("evidence").EnumerateArray());
        Assert.Contains(ModelCalls(run), e => e.GetProperty("name").GetString() == "analyze:contract-analyst");
    }

    [Fact]
    public async Task A_workflow_on_a_supplier_the_sandbox_does_not_have_says_which_it_has()
    {
        var client = Host().CreateClient();

        using var unknown = await RunWorkflowAsync(client, "council", new { supplier = "Initech" });
        Assert.Equal("error", unknown.RootElement.GetProperty("status").GetString());
        Assert.Contains("Salesforce", unknown.RootElement.GetProperty("error").GetString());

        using var empty = await RunWorkflowAsync(client, "drafting", new { supplier = "Salesforce" }, sandbox: "empty");
        Assert.Equal("error", empty.RootElement.GetProperty("status").GetString());
        Assert.Contains("seeded", empty.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Drafting_returns_the_email_and_how_it_was_written()
    {
        var client = Host().CreateClient();

        using var run = await RunWorkflowAsync(client, "drafting", new { supplier = "Salesforce", goal = "reduce 20k", language = "en" });

        Assert.Equal("ok", run.RootElement.GetProperty("status").GetString());
        var result = run.RootElement.GetProperty("result");
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("subject").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("body").GetString()));
        Assert.Equal("Salesforce", result.GetProperty("supplier").GetString());
    }

    [Fact]
    public async Task A_registered_agents_prompt_can_be_replaced_by_a_variant_and_only_for_that_run()
    {
        var client = Host().CreateClient();
        var variantId = await CreateVariantAsync(client, new { prompts = new Dictionary<string, string> { ["contract-analyst"] = "PLAYGROUND ANALYST PROMPT" } }, "Analyst prompt");

        using var changed = await RunWorkflowAsync(client, "council", new { supplier = "Salesforce" }, variantId: variantId);
        using var plain = await RunWorkflowAsync(client, "council", new { supplier = "Salesforce" });

        string AnalystRequest(JsonDocument run) => ModelCalls(run).First(e => e.GetProperty("name").GetString() == "analyze:contract-analyst").GetProperty("input").GetString()!;
        Assert.Contains("PLAYGROUND ANALYST PROMPT", AnalystRequest(changed), StringComparison.Ordinal);
        Assert.DoesNotContain("PLAYGROUND ANALYST PROMPT", AnalystRequest(plain), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Web_research_runs_the_research_core_and_validates_its_purpose()
    {
        var client = Host().CreateClient();

        using var run = await RunWorkflowAsync(client, "research", new { query = "Salesforce price increases 2026", purpose = "SupplierNews", language = "en" });
        Assert.Equal("research", run.RootElement.GetProperty("workflow").GetString());
        Assert.True(run.RootElement.GetProperty("result").TryGetProperty("kind", out _));
        // What leaves Raffa is the cleaned query (same sanitiser as the production consent gate).
        Assert.Contains("Salesforce", run.RootElement.GetProperty("result").GetProperty("sentQuery").GetString());

        using var tooShort = await RunWorkflowAsync(client, "research", new { query = "hi", purpose = "Open" });
        Assert.Equal("error", tooShort.RootElement.GetProperty("status").GetString());
        Assert.Contains(ModelCalls(run), e => e.GetProperty("name").GetString()!.StartsWith("research:", StringComparison.Ordinal));

        var bad = await SendAsync(client, HttpMethod.Post, "/api/admin/playground/runs", Staff,
            new { workflow = "research", sandbox = "seeded", input = new { query = "x y z", purpose = "Gossip" } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Extraction_returns_each_stage_payload_for_pasted_text_and_for_an_uploaded_file()
    {
        var client = Host().CreateClient();
        const string contract = "Master Services Agreement between Acme Ltd (supplier) and Globex SA. Start date 1 January 2026. " +
            "End date 31 December 2028. Annual fee 48000 EUR. Payment terms Net 30. Governing law Italy.";

        using var pasted = await RunWorkflowAsync(client, "extraction", new { text = contract });
        var stages = pasted.RootElement.GetProperty("result").GetProperty("stages").EnumerateArray().ToList();
        Assert.True(stages.Count >= 6, $"{stages.Count} stages");
        Assert.Contains(stages, s => s.GetProperty("stage").GetString() == "Metadata");
        Assert.All(stages, s => Assert.True(s.TryGetProperty("payload", out _)));
        Assert.Contains(ModelCalls(pasted), e => e.GetProperty("name").GetString() == "extract:Metadata");

        var pdf = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("%PDF-1.4 " + contract));
        using var uploaded = await RunWorkflowAsync(client, "extraction", new { fileBase64 = pdf, fileName = "contract.pdf", mimeType = "application/pdf" });
        Assert.Equal("ok", uploaded.RootElement.GetProperty("status").GetString());

        // The file itself is never stored with the run: only its name and size.
        var runId = uploaded.RootElement.GetProperty("runId").GetGuid();
        var stored = await SendAsync(client, HttpMethod.Get, $"/api/admin/playground/runs/{runId}", Staff);
        var storedText = await stored.Content.ReadAsStringAsync();
        Assert.DoesNotContain(pdf, storedText, StringComparison.Ordinal);
        Assert.Contains("contract.pdf", storedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_workflow_with_bad_input_is_a_bad_request()
    {
        var client = Host().CreateClient();

        async Task<HttpStatusCode> TryAsync(object body) =>
            (await SendAsync(client, HttpMethod.Post, "/api/admin/playground/runs", Staff, body)).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(new { workflow = "poetry", sandbox = "seeded", question = "x" }));
        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(new { workflow = "council", sandbox = "seeded", input = new { } }));
        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(new { workflow = "extraction", sandbox = "seeded", input = new { } }));
        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(new { workflow = "extraction", sandbox = "seeded", input = new { fileBase64 = "AAAA", mimeType = "text/html" } }));
        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(new { workflow = "extraction", sandbox = "seeded", input = new { fileBase64 = "%%%", mimeType = "application/pdf" } }));
    }

    [Fact]
    public async Task The_baseline_lists_every_replaceable_prompt_with_its_flow()
    {
        var client = Host().CreateClient();

        using var baseline = await JsonOf(await SendAsync(client, HttpMethod.Get, "/api/admin/playground/baseline", Staff));

        var prompts = baseline.RootElement.GetProperty("prompts").EnumerateArray().ToDictionary(p => p.GetProperty("agent").GetString()!);
        Assert.Contains("agent", prompts.Keys);
        Assert.Contains("extract", prompts.Keys);
        Assert.Contains("contract-analyst", prompts.Keys);
        Assert.Equal("document-extraction", prompts["extract"].GetProperty("flow").GetString());
        Assert.False(string.IsNullOrWhiteSpace(prompts["contract-analyst"].GetProperty("text").GetString()));
    }

    private static async Task<(Guid Baseline, Guid Variant, Guid Comparison)> RunComparisonAsync(HttpClient client, string question = "Quando scade il contratto Salesforce?")
    {
        var variantId = await CreateVariantAsync(client, new { maxToolCalls = 4 }, "Secret variant name");
        var comparison = Guid.NewGuid();
        using var baseline = await JsonOf(await SendAsync(client, HttpMethod.Post, "/api/admin/playground/runs", Staff,
            new { sandbox = "seeded", question, comparisonId = comparison }));
        using var variant = await JsonOf(await SendAsync(client, HttpMethod.Post, "/api/admin/playground/runs", Staff,
            new { sandbox = "seeded", question, variantId, comparisonId = comparison }));
        return (baseline.RootElement.GetProperty("runId").GetGuid(), variant.RootElement.GetProperty("runId").GetGuid(), comparison);
    }

    [Fact]
    public async Task The_judge_surfaces_are_for_the_platform_admin_only()
    {
        var client = Host().CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, "/api/admin/playground/rubrics", Customer)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, "/api/admin/playground/judgements", Customer, new { comparisonId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"/api/admin/playground/judgements?comparisonId={Guid.NewGuid()}", Customer)).StatusCode);
    }

    [Fact]
    public async Task Rubrics_are_built_in_per_workflow_and_custom_ones_can_be_saved_edited_and_archived()
    {
        var client = Host().CreateClient();

        using var initial = await JsonOf(await SendAsync(client, HttpMethod.Get, "/api/admin/playground/rubrics", Staff));
        var defaults = initial.RootElement.GetProperty("rubrics").EnumerateArray().Where(r => r.GetProperty("isDefault").GetBoolean()).ToList();
        Assert.Equal(new[] { "default:ask", "default:council", "default:drafting", "default:extraction", "default:research" }, defaults.Select(r => r.GetProperty("id").GetString()!).Order().ToArray());
        Assert.All(defaults, r => Assert.NotEmpty(r.GetProperty("criteria").EnumerateArray()));

        var created = await SendAsync(client, HttpMethod.Post, "/api/admin/playground/rubrics", Staff, new
        {
            name = "Strict grounding",
            workflow = "ask",
            criteria = new object[] { new { label = "Grounding", description = "Only facts from the evidence", weight = 5 }, new { label = "Grounding", description = "again", weight = 1 } },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var rubric = await JsonOf(created);
        var id = rubric.RootElement.GetProperty("id").GetGuid();
        // Criterion ids come from the label and are unique inside the rubric.
        Assert.Equal(new[] { "grounding", "grounding_2" }, rubric.RootElement.GetProperty("criteria").EnumerateArray().Select(c => c.GetProperty("id").GetString()!).ToArray());

        var edited = await SendAsync(client, HttpMethod.Put, $"/api/admin/playground/rubrics/{id}", Staff,
            new { name = "Strict grounding v2", workflow = "ask", criteria = new[] { new { label = "Grounding", description = "x", weight = 4 } } });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"/api/admin/playground/rubrics/{id}", Staff)).StatusCode);
        using var after = await JsonOf(await SendAsync(client, HttpMethod.Get, "/api/admin/playground/rubrics", Staff));
        Assert.DoesNotContain(after.RootElement.GetProperty("rubrics").EnumerateArray(), r => r.GetProperty("id").GetString() == id.ToString());
    }

    [Fact]
    public async Task A_rubric_that_is_not_valid_is_refused_with_the_reason()
    {
        var client = Host().CreateClient();

        async Task<HttpStatusCode> TryAsync(object body) =>
            (await SendAsync(client, HttpMethod.Post, "/api/admin/playground/rubrics", Staff, body)).StatusCode;

        var ok = new[] { new { label = "A", description = "d", weight = 3 } };
        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(new { name = " ", workflow = "ask", criteria = ok }));
        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(new { name = "n", workflow = "poetry", criteria = ok }));
        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(new { name = "n", workflow = "ask", criteria = Array.Empty<object>() }));
        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(new { name = "n", workflow = "ask", criteria = new[] { new { label = "A", description = "d", weight = 9 } } }));
        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(new { name = "n", workflow = "ask", criteria = new[] { new { label = "", description = "d", weight = 3 } } }));
    }

    [Fact]
    public async Task The_judge_scores_the_runs_of_a_comparison_blind_and_never_sees_the_variant()
    {
        var client = Host().CreateClient();
        var (baseline, variant, comparison) = await RunComparisonAsync(client);

        var response = await SendAsync(client, HttpMethod.Post, "/api/admin/playground/judgements", Staff, new { comparisonId = comparison, passes = 2 });
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {raw}");
        using var judgement = JsonDocument.Parse(raw);
        var root = judgement.RootElement;

        Assert.Equal("ask", root.GetProperty("workflow").GetString());
        Assert.Equal("default:ask", root.GetProperty("rubric").GetProperty("id").GetString());
        var answers = root.GetProperty("answers").EnumerateArray().ToList();
        Assert.Equal(new[] { baseline, variant }.Order().ToArray(), answers.Select(a => a.GetProperty("runId").GetGuid()).Order().ToArray());
        Assert.All(answers, a => Assert.Equal(root.GetProperty("rubric").GetProperty("criteria").GetArrayLength(), a.GetProperty("scores").GetArrayLength()));
        Assert.Contains(root.GetProperty("answers").EnumerateArray().Select(a => a.GetProperty("variantName").GetString()), n => n == "Secret variant name");

        // Two passes, the second in the reverse order; the preference is computed from the weighted scores.
        var passes = root.GetProperty("passDetails").EnumerateArray().ToList();
        Assert.Equal(2, passes.Count);
        Assert.Equal(passes[0].GetProperty("order").EnumerateArray().Select(o => o.GetString()).Reverse().ToArray(), passes[1].GetProperty("order").EnumerateArray().Select(o => o.GetString()).ToArray());

        // What the judge was sent: anonymous labels, never a variant name or a run id.
        var judgeCalls = root.GetProperty("trace").EnumerateArray().Where(e => e.GetProperty("name").GetString() == "analyze:playground-judge").ToList();
        Assert.Equal(2, judgeCalls.Count);
        foreach (var call in judgeCalls)
        {
            // The recorded request is the gateway call as JSON; the judge's own input is its inputJson field.
            using var recorded = JsonDocument.Parse(call.GetProperty("input").GetString()!);
            var sent = recorded.RootElement.GetProperty("inputJson").GetString()!;
            Assert.Contains("\"label\":\"A\"", sent, StringComparison.Ordinal);
            Assert.DoesNotContain("Secret variant name", sent, StringComparison.Ordinal);
            Assert.DoesNotContain(baseline.ToString(), sent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(variant.ToString(), sent, StringComparison.OrdinalIgnoreCase);
        }

        // It is stored and found again by comparison and by run.
        using var byComparison = await JsonOf(await SendAsync(client, HttpMethod.Get, $"/api/admin/playground/judgements?comparisonId={comparison}", Staff));
        Assert.Single(byComparison.RootElement.GetProperty("judgements").EnumerateArray());
        using var byRun = await JsonOf(await SendAsync(client, HttpMethod.Get, $"/api/admin/playground/judgements?runId={baseline}", Staff));
        Assert.Single(byRun.RootElement.GetProperty("judgements").EnumerateArray());
    }

    [Fact]
    public async Task A_custom_rubric_is_applied_and_kept_as_a_snapshot_of_the_judgement()
    {
        var client = Host().CreateClient();
        var (baseline, variant, _) = await RunComparisonAsync(client);
        var created = await SendAsync(client, HttpMethod.Post, "/api/admin/playground/rubrics", Staff,
            new { name = "Only tone", workflow = "ask", criteria = new[] { new { label = "Tone", description = "Direct and concrete", weight = 5 } } });
        using var rubric = await JsonOf(created);
        var rubricId = rubric.RootElement.GetProperty("id").GetString();

        using var judgement = await JsonOf(await SendAsync(client, HttpMethod.Post, "/api/admin/playground/judgements", Staff, new { runIds = new[] { baseline, variant }, rubricId }));

        Assert.Equal("Only tone", judgement.RootElement.GetProperty("rubric").GetProperty("name").GetString());
        Assert.All(judgement.RootElement.GetProperty("answers").EnumerateArray(), a =>
            Assert.Equal(new[] { "tone" }, a.GetProperty("scores").EnumerateArray().Select(s => s.GetProperty("criterionId").GetString()!).ToArray()));

        // Archiving the rubric afterwards does not change the stored judgement.
        await SendAsync(client, HttpMethod.Delete, $"/api/admin/playground/rubrics/{rubricId}", Staff);
        using var stored = await JsonOf(await SendAsync(client, HttpMethod.Get, $"/api/admin/playground/judgements?runId={baseline}", Staff));
        Assert.Equal("Only tone", stored.RootElement.GetProperty("judgements")[0].GetProperty("rubric").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Judging_is_refused_for_runs_that_cannot_be_compared()
    {
        var client = Host().CreateClient();
        var (baseline, _, comparison) = await RunComparisonAsync(client);
        using var other = await RunWorkflowAsync(client, "council", new { supplier = "Salesforce" });
        var otherRun = other.RootElement.GetProperty("runId").GetGuid();
        using var otherQuestion = await JsonOf(await SendAsync(client, HttpMethod.Post, "/api/admin/playground/runs", Staff, new { sandbox = "seeded", question = "Un'altra domanda su Salesforce?" }));
        var otherQuestionRun = otherQuestion.RootElement.GetProperty("runId").GetGuid();

        async Task<HttpStatusCode> TryAsync(object body) =>
            (await SendAsync(client, HttpMethod.Post, "/api/admin/playground/judgements", Staff, body)).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(new { runIds = new[] { baseline, otherRun } }));
        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(new { runIds = new[] { baseline, otherQuestionRun } }));
        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(new { comparisonId = comparison, rubricId = "default:council" }));
        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(new { comparisonId = comparison, judgeModelId = "gpt-not-deployed" }));
        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(new { }));
        Assert.Equal(HttpStatusCode.NotFound, await TryAsync(new { comparisonId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Any_workflow_can_be_judged_with_its_own_default_rubric()
    {
        var client = Host().CreateClient();
        using var council = await RunWorkflowAsync(client, "council", new { supplier = "Salesforce" });

        using var judgement = await JsonOf(await SendAsync(client, HttpMethod.Post, "/api/admin/playground/judgements", Staff,
            new { runIds = new[] { council.RootElement.GetProperty("runId").GetGuid() } }));

        Assert.Equal("default:council", judgement.RootElement.GetProperty("rubric").GetProperty("id").GetString());
        Assert.Single(judgement.RootElement.GetProperty("answers").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, judgement.RootElement.GetProperty("preferredRunId").ValueKind);
    }

    [Fact]
    public async Task A_run_with_an_unknown_variant_is_not_found()
    {
        var client = Host().CreateClient();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await SendAsync(client, HttpMethod.Post, "/api/admin/playground/runs", Staff, new { sandbox = "seeded", question = "Ciao", variantId = Guid.NewGuid() })).StatusCode);
    }

    [Fact]
    public async Task A_run_on_an_unknown_sandbox_or_without_a_question_is_a_bad_request()
    {
        var client = Host().CreateClient();

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await SendAsync(client, HttpMethod.Post, "/api/admin/playground/runs", Staff, new { sandbox = Guid.NewGuid().ToString(), question = "Ciao" })).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await SendAsync(client, HttpMethod.Post, "/api/admin/playground/runs", Staff, new { sandbox = "seeded", question = "  " })).StatusCode);
    }
}

/// <summary>Token cost of a playground run: longest-prefix deployment match, cached input priced apart, no price = no cost.</summary>
public sealed class PlaygroundPricingTests
{
    private static Raffa.Api.Infrastructure.PlaygroundPricingOptions Options() => new()
    {
        Models =
        {
            ["gpt-5.4"] = new() { InputPer1M = 2.75m, CachedInputPer1M = 0.275m, OutputPer1M = 16.5m },
            ["gpt-5.4-nano"] = new() { InputPer1M = 0.22m, CachedInputPer1M = 0.022m, OutputPer1M = 1.375m },
        },
    };

    [Fact]
    public void A_deployment_takes_the_price_of_its_longest_matching_prefix()
    {
        // 1M uncached prompt + 1M completion on gpt-5.4-nano-dev = 0.22 + 1.375, not the gpt-5.4 price.
        Assert.Equal(1.595m, Options().CostUsd("gpt-5.4-nano-dev", 1_000_000, 1_000_000, 0));
        Assert.Equal(19.25m, Options().CostUsd("gpt-5.4-demo", 1_000_000, 1_000_000, 0));
    }

    [Fact]
    public void Cached_prompt_tokens_are_priced_at_the_cached_rate()
    {
        // 600k uncached + 400k cached prompt tokens, no output: 0.6 * 2.75 + 0.4 * 0.275 = 1.76.
        Assert.Equal(1.76m, Options().CostUsd("gpt-5.4", 1_000_000, 0, 400_000));
    }

    [Fact]
    public void A_deployment_with_no_price_has_no_cost_not_zero()
    {
        Assert.Null(Options().CostUsd("gpt-6-astra", 1000, 1000, 0));
        Assert.Null(Options().CostUsd(null, 1000, 1000, 0));
    }
}
