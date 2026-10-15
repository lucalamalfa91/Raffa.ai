using System.Net;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Foundry;
using Raffa.AiGateway.Foundry.Prompts;
using Raffa.AiGateway.Tests.TestSupport;

namespace Raffa.AiGateway.Tests.Foundry;

/// <summary>F5-T03: the document-first (prompt-cache-friendly) layout of the `extract` role and the cached-token usage.</summary>
public class FoundryExtractDocumentFirstTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri FoundryBaseAddress = new("https://fake-foundry.example.com/");

    private const string Schema =
        """
        {"type":"object","properties":{"facts":{"type":"array","items":{"type":"object","properties":{"field":{"type":"string"},"value":{"type":["string","null"]},"confidence":{"type":"number"}},"required":["field","value","confidence"],"additionalProperties":false}}},"required":["facts"],"additionalProperties":false}
        """;

    private const string Document = "[[PAGE 1]]\nSupplier: ACME S.p.A.\n\n";

    private static (FoundryExtractClient Client, FakeHttpMessageHandler Handler) CreateClient()
    {
        var envelope = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { role = "assistant", content = "{\"facts\":[]}" }, finish_reason = "stop" } },
            usage = new
            {
                prompt_tokens = 3000,
                completion_tokens = 150,
                total_tokens = 3150,
                prompt_tokens_details = new { cached_tokens = 2048 },
            },
        });
        var handler = new FakeHttpMessageHandler(FakeHttpMessageHandler.Json(HttpStatusCode.OK, envelope));
        var httpClient = new HttpClient(handler) { BaseAddress = FoundryBaseAddress };
        var foundryOptions = new AiGatewayFoundryOptions { Endpoint = FoundryBaseAddress.ToString() };
        var httpJsonClient = new FoundryHttpJsonClient(
            httpClient, new FoundryTokenProvider(new FakeTokenCredential()), foundryOptions, TestRetryPolicies.NoDelay());
        var client = new FoundryExtractClient(
            new FoundryChatCompletionsClient(httpJsonClient, foundryOptions), new AiGatewayModelOptions(), new FixedClock(Now));
        return (client, handler);
    }

    private static (string System, string User) Messages(string body)
    {
        using var json = JsonDocument.Parse(body);
        var messages = json.RootElement.GetProperty("messages").EnumerateArray().ToList();
        return (messages[0].GetProperty("content").ToString(), messages[^1].GetProperty("content").ToString());
    }

    [Fact]
    public async Task The_historic_layout_is_unchanged_when_document_first_is_off()
    {
        var (client, handler) = CreateClient();

        var result = await client.ExtractAsync(new AiExtractionRequest("Risk", Document, Schema), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("foundry-extract-v3", result.Value.Metadata.PromptVersion);
        var (system, user) = Messages(Assert.Single(handler.RequestBodies)!);
        Assert.Equal(ExtractPromptTemplate.SystemPrompt("Risk"), system);
        Assert.Contains("'Risk' stage", system, StringComparison.Ordinal);
        Assert.Equal(Document, user);
    }

    [Fact]
    public async Task Document_first_shares_the_system_prompt_and_the_document_prefix_across_stages()
    {
        var (client, handler) = CreateClient();

        var risk = await client.ExtractAsync(new AiExtractionRequest("Risk", Document, Schema, DocumentFirst: true), CancellationToken.None);
        var obligations = await client.ExtractAsync(new AiExtractionRequest("Obligations", Document, Schema, DocumentFirst: true), CancellationToken.None);

        Assert.True(risk.IsSuccess && obligations.IsSuccess);
        Assert.Equal(ExtractPromptTemplate.DocumentFirstVersion, risk.Value.Metadata.PromptVersion);
        var first = Messages(handler.RequestBodies[0]!);
        var second = Messages(handler.RequestBodies[1]!);
        Assert.Equal(first.System, second.System);
        Assert.Equal(ExtractPromptTemplate.SharedSystemPrompt(), first.System);
        Assert.DoesNotContain("'Risk'", first.System, StringComparison.Ordinal);
        Assert.StartsWith(Document, first.User, StringComparison.Ordinal);
        Assert.StartsWith(Document, second.User, StringComparison.Ordinal);
        Assert.Contains("Extract the 'Risk' stage", first.User, StringComparison.Ordinal);
        Assert.Contains("Extract the 'Obligations' stage", second.User, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_providers_cached_token_count_is_carried_on_the_usage()
    {
        var (client, _) = CreateClient();

        var result = await client.ExtractAsync(new AiExtractionRequest("Risk", Document, Schema), CancellationToken.None);

        Assert.Equal(new AiTokenUsage(3000, 150, 2048), result.Value.Metadata.Usage);
    }

    [Fact]
    public void The_shared_and_per_stage_system_prompts_carry_the_same_rules()
    {
        var perStage = ExtractPromptTemplate.SystemPrompt("Risk");
        var shared = ExtractPromptTemplate.SharedSystemPrompt();

        var marker = "from the document text only.";
        Assert.Equal(
            perStage[perStage.IndexOf(marker, StringComparison.Ordinal)..],
            shared[shared.IndexOf(marker, StringComparison.Ordinal)..]);
    }
}
