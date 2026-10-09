using System.Net;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.AiGateway.Foundry;
using Raffa.AiGateway.Tests.TestSupport;

namespace Raffa.AiGateway.Tests.Foundry;

/// <summary>
/// F3-T01 — the hosted web search tool returns a URL and a title and no page text, so the research role
/// asks the model for <c>sources[].quote</c>, the passage it copied verbatim, and carries it onto
/// <see cref="AiWebSource.Quote"/>; the snippet stays empty, as production's always was.
/// </summary>
public class FoundryResearchQuoteTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri FoundryBaseAddress = new("https://fake-foundry.example.com/");

    private static (FoundryResearchClient Client, FakeHttpMessageHandler Handler) CreateClient(string envelope)
    {
        var handler = new FakeHttpMessageHandler(FakeHttpMessageHandler.Json(HttpStatusCode.OK, envelope));
        var httpClient = new HttpClient(handler) { BaseAddress = FoundryBaseAddress };
        var foundryOptions = new AiGatewayFoundryOptions { Endpoint = FoundryBaseAddress.ToString() };
        var httpJsonClient = new FoundryHttpJsonClient(
            httpClient, new FoundryTokenProvider(new FakeTokenCredential()), foundryOptions, TestRetryPolicies.NoDelay());
        var client = new FoundryResearchClient(
            httpJsonClient,
            foundryOptions,
            new AiGatewayModelOptions { Research = new AiModelSelection("gpt-5.4-research-dev", "2026-03-17") { MaxCompletionTokens = 2048 } },
            new FixedClock(Now));
        return (client, handler);
    }

    private static AiResearchRequest Request(int maxSources = 5) =>
        new("saas renewal uplift market practice", "MarketPractice", "en", maxSources, "You research procurement topics only.", "research-v2");

    private static string Envelope(object payload, params (string Url, string Title)[] citations) =>
        JsonSerializer.Serialize(new
        {
            status = "completed",
            model = "gpt-5.4-research-dev",
            output = new object[]
            {
                new { type = "web_search_call", status = "completed" },
                new
                {
                    type = "message",
                    content = new[]
                    {
                        new
                        {
                            type = "output_text",
                            text = JsonSerializer.Serialize(payload),
                            annotations = citations.Select(c => new { type = "url_citation", url = c.Url, title = c.Title }).ToArray(),
                        },
                    },
                },
            },
            usage = new { input_tokens = 300, output_tokens = 150 },
        });

    [Fact]
    public async Task The_output_schema_requires_a_quote_on_every_source()
    {
        var (client, handler) = CreateClient(Envelope(
            new { summaryMarkdown = "Caps run 5-10% [1].", offTopic = false, sources = new[] { new { n = 1, url = "https://example.com/a", title = "A", quote = "Caps run 5-10%." } } },
            ("https://example.com/a", "A")));

        await client.ResearchAsync(Request(), CancellationToken.None);

        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies)!);
        var item = body.RootElement.GetProperty("text").GetProperty("format").GetProperty("schema")
            .GetProperty("properties").GetProperty("sources").GetProperty("items");
        Assert.Equal("string", item.GetProperty("properties").GetProperty("quote").GetProperty("type").GetString());
        Assert.Contains("quote", item.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.False(item.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public async Task The_models_quote_reaches_the_source_and_the_snippet_stays_empty()
    {
        var (client, _) = CreateClient(Envelope(
            new
            {
                summaryMarkdown = "Salesforce raised list prices by 9% [1].",
                offTopic = false,
                sources = new[] { new { n = 1, url = "https://example.com/sf", title = "Salesforce pricing", quote = "  Effective August 1, 2025,\n list prices rise by an average of 9%.  " } },
            },
            ("https://example.com/sf", "Salesforce pricing update")));

        var result = await client.ResearchAsync(Request(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var source = Assert.Single(result.Value.Sources);
        Assert.Equal("Effective August 1, 2025, list prices rise by an average of 9%.", source.Quote);
        Assert.Equal(string.Empty, source.Snippet);
        Assert.Equal("Salesforce pricing update", source.Title);
    }

    [Fact]
    public async Task A_quote_follows_its_source_through_the_renumbering_not_the_annotation_order()
    {
        var (client, _) = CreateClient(Envelope(
            new
            {
                summaryMarkdown = "First fact 5% [1]. Second fact 60 days [2].",
                offTopic = false,
                sources = new[]
                {
                    new { n = 1, url = "https://example.com/z", title = "Z", quote = "Fact five percent: 5%." },
                    new { n = 2, url = "https://example.com/x", title = "X", quote = "Notice is 60 days." },
                },
            },
            ("https://example.com/x", "X"), ("https://example.com/y", "Y"), ("https://example.com/z", "Z")));

        var result = await client.ResearchAsync(Request(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(["https://example.com/z", "https://example.com/x"], result.Value.Sources.Select(s => s.Url).ToArray());
        Assert.Equal(["Fact five percent: 5%.", "Notice is 60 days."], result.Value.Sources.Select(s => s.Quote).ToArray());
    }

    [Fact]
    public async Task A_page_listed_under_two_numbers_keeps_both_passages_once()
    {
        var (client, _) = CreateClient(Envelope(
            new
            {
                summaryMarkdown = "Caps run 5-10% [1] and notice is 60 days [2].",
                offTopic = false,
                sources = new[]
                {
                    new { n = 1, url = "https://example.com/a", title = "A", quote = "Caps run 5-10%." },
                    new { n = 2, url = "https://example.com/a/", title = "A", quote = "Notice is 60 days." },
                    new { n = 3, url = "https://example.com/a", title = "A", quote = "Caps run 5-10%." },
                },
            },
            ("https://example.com/a", "A")));

        var result = await client.ResearchAsync(Request(), CancellationToken.None);

        var source = Assert.Single(result.Value.Sources);
        Assert.Equal("Caps run 5-10%. Notice is 60 days.", source.Quote);
    }

    [Fact]
    public async Task A_missing_or_blank_quote_is_an_empty_quote_never_an_error()
    {
        var (client, _) = CreateClient(Envelope(
            new
            {
                summaryMarkdown = "Buyers trade term for price [1] and [2].",
                offTopic = false,
                sources = new object[]
                {
                    new { n = 1, url = "https://example.com/a", title = "A" },
                    new { n = 2, url = "https://example.com/b", title = "B", quote = "   " },
                },
            },
            ("https://example.com/a", "A"), ("https://example.com/b", "B")));

        var result = await client.ResearchAsync(Request(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.All(result.Value.Sources, source => Assert.Equal(string.Empty, source.Quote));
    }

    [Fact]
    public async Task A_quote_is_capped_so_it_stays_a_passage_and_not_the_page()
    {
        var long_quote = new string('a', WebSourceReconciler.MaxQuoteChars + 500);
        var (client, _) = CreateClient(Envelope(
            new { summaryMarkdown = "Buyers trade term for price [1].", offTopic = false, sources = new[] { new { n = 1, url = "https://example.com/a", title = "A", quote = long_quote } } },
            ("https://example.com/a", "A")));

        var result = await client.ResearchAsync(Request(), CancellationToken.None);

        Assert.Equal(WebSourceReconciler.MaxQuoteChars, Assert.Single(result.Value.Sources).Quote.Length);
    }

    [Fact]
    public async Task A_quote_for_a_url_the_tool_never_cited_is_not_a_source()
    {
        var (client, _) = CreateClient(Envelope(
            new
            {
                summaryMarkdown = "Caps run 5-10% [1].",
                offTopic = false,
                sources = new[] { new { n = 1, url = "https://invented.example.net/a", title = "A", quote = "Caps run 5-10%." } },
            },
            ("https://example.com/real", "Real")));

        var result = await client.ResearchAsync(Request(), CancellationToken.None);

        // The tool cited another page: the invented one is no source, so the summary has none to cite
        // (the caller's WebGuard then abstains on the empty list).
        Assert.Empty(result.Value.Sources);
    }

    [Fact]
    public async Task The_fixture_double_has_the_same_shape_as_production_an_empty_snippet_and_the_text_in_the_quote()
    {
        var fixture = new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now));

        var result = await fixture.ResearchAsync(Request());

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.NotEmpty(result.Value.Sources);
        Assert.All(result.Value.Sources, source =>
        {
            Assert.Equal(string.Empty, source.Snippet);
            Assert.False(string.IsNullOrWhiteSpace(source.Quote));
        });
        Assert.Contains("5-10%", result.Value.Sources[0].Quote, StringComparison.Ordinal);
    }
}
