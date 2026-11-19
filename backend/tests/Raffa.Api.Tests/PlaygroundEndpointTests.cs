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
