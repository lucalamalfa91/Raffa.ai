using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Raffa.AiFlows.QuoteExtraction.Orchestration;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.Api.Tests.TestSupport;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Api.Tests;

/// <summary>
/// Task F6-T07: the header stage (supplier, quote date, currency, geography read from the document)
/// and the person's confirmation of it (<c>PUT /api/quotes/{id}/header</c>). In-memory host, scripted
/// `extract` role per stage: no Postgres, Docker or Foundry. The stage is off by default; the tests
/// that need it set <c>QuoteExtraction:HeaderStageEnabled</c>.
/// </summary>
public sealed class QuoteHeaderEndpointTests : IClassFixture<RaffaApiFactory>
{
    private const string QuoteText =
        "QUOTE Q-2026-0417 from Northwind Traders, Milan, dated 20 September 2026. " +
        "Cloud Suite Enterprise, 250 seats, EUR 96.00 per seat per year.";

    private const string Lines = """
        {"items":[{"sku":"CS-ENT","description":"Cloud Suite Enterprise","quantity":250,"unitPrice":96,"term":"Annual","confidence":0.95}]}
        """;

    private const string Header = """
        {"supplier":{"value":"Northwind Traders","sourcePage":1,"sourceSpan":"from Northwind Traders","confidence":0.93},
         "quoteDate":{"value":"2026-09-20","sourcePage":1,"sourceSpan":"dated 20 September 2026","confidence":0.9},
         "currency":{"value":"EUR","sourcePage":1,"sourceSpan":"EUR 96.00","confidence":0.97},
         "geography":{"value":"IT","sourcePage":1,"sourceSpan":"Milan","confidence":0.8}}
        """;

    private readonly WebApplicationFactory<Program> _baseFactory;

    public QuoteHeaderEndpointTests(RaffaApiFactory factory)
    {
        _baseFactory = factory;
    }

    // ----- the stage -----

    [Fact]
    public async Task By_default_no_header_call_is_made_and_the_upload_is_as_before()
    {
        var host = Host.Create(_baseFactory, headerStage: false);
        var body = await UploadAsync(host, Guid.NewGuid());

        Assert.Equal(["QuoteLineItems"], host.Stages);
        Assert.Empty(body.GetProperty("headerFilled").EnumerateArray());
        var quote = (await host.ReadQuotesAsync()).Single();
        Assert.Null(quote.Supplier);
        Assert.Null(quote.Currency);
        Assert.Null(quote.Geography);
        Assert.DoesNotContain(host.Audit.Entries, e => e.Action == "quote.header.extracted");
    }

    [Fact]
    public async Task With_the_stage_on_the_blank_header_fields_are_filled_and_reported()
    {
        var host = Host.Create(_baseFactory, headerStage: true);

        var body = await UploadAsync(host, Guid.NewGuid());

        Assert.Equal(["QuoteLineItems", "QuoteHeader"], host.Stages);
        Assert.Equal(
            ["supplier", "currency", "geography", "purchaseDate"],
            body.GetProperty("headerFilled").EnumerateArray().Select(e => e.GetString()!).ToArray());
        Assert.Equal("Completed", body.GetProperty("processingStatus").GetString());
        var quote = (await host.ReadQuotesAsync()).Single();
        Assert.Equal("Northwind Traders", quote.Supplier);
        Assert.Equal("EUR", quote.Currency);
        Assert.Equal("IT", quote.Geography);
        Assert.Equal(new DateOnly(2026, 9, 20), quote.PurchaseDate);
        var audit = Assert.Single(host.Audit.Entries, e => e.Action == "quote.header.extracted");
        Assert.Equal("supplier,currency,geography,purchaseDate", audit.Detail);
    }

    [Fact]
    public async Task A_field_given_at_upload_is_kept_and_only_the_rest_is_filled()
    {
        var host = Host.Create(_baseFactory, headerStage: true);

        var body = await UploadAsync(host, Guid.NewGuid(), ("supplier", "Fabrikam Reseller"), ("currency", "USD"));

        Assert.Equal(
            ["geography", "purchaseDate"],
            body.GetProperty("headerFilled").EnumerateArray().Select(e => e.GetString()!).ToArray());
        var quote = (await host.ReadQuotesAsync()).Single();
        Assert.Equal("Fabrikam Reseller", quote.Supplier);
        Assert.Equal("USD", quote.Currency);
        Assert.Equal("IT", quote.Geography);
    }

    [Fact]
    public async Task A_complete_header_at_upload_costs_no_header_call()
    {
        var host = Host.Create(_baseFactory, headerStage: true);

        await UploadAsync(
            host, Guid.NewGuid(),
            ("supplier", "Fabrikam Reseller"), ("currency", "USD"), ("geography", "US"), ("purchaseDate", "2026-08-01"));

        Assert.Equal(["QuoteLineItems"], host.Stages);
    }

    [Fact]
    public async Task A_failing_header_call_never_fails_the_quote()
    {
        var host = Host.Create(_baseFactory, headerStage: true);
        host.HeaderAnswer = _ => throw new InvalidOperationException("header model outage");

        var body = await UploadAsync(host, Guid.NewGuid());

        Assert.Equal("Completed", body.GetProperty("processingStatus").GetString());
        Assert.Equal(1, body.GetProperty("lineItemCount").GetInt32());
        Assert.Empty(body.GetProperty("headerFilled").EnumerateArray());
        Assert.Null((await host.ReadQuotesAsync()).Single().Supplier);
    }

    [Fact]
    public async Task A_malformed_header_answer_never_fails_the_quote()
    {
        var host = Host.Create(_baseFactory, headerStage: true);
        host.HeaderAnswer = _ => Result<AiExtractionResult>.Failure("model returned nothing usable");

        var body = await UploadAsync(host, Guid.NewGuid());
        Assert.Equal("Completed", body.GetProperty("processingStatus").GetString());

        host.HeaderAnswer = _ => Extracted("this is not json");
        var second = await UploadAsync(host, Guid.NewGuid());
        Assert.Equal("Completed", second.GetProperty("processingStatus").GetString());
        Assert.All(await host.ReadQuotesAsync(), q => Assert.Null(q.Supplier));
    }

    [Fact]
    public async Task A_low_confidence_header_leaves_the_fields_blank_for_the_person()
    {
        var host = Host.Create(_baseFactory, headerStage: true);
        host.HeaderAnswer = _ => Extracted(Header.Replace("0.93", "0.5").Replace("0.97", "0.4"));

        var body = await UploadAsync(host, Guid.NewGuid());

        Assert.Equal(
            new[] { "geography", "purchaseDate" },
            body.GetProperty("headerFilled").EnumerateArray().Select(e => e.GetString()!).Order().ToArray());
        var quote = (await host.ReadQuotesAsync()).Single();
        Assert.Null(quote.Supplier);
        Assert.Null(quote.Currency);
    }

    // ----- the person's confirmation -----

    [Fact]
    public async Task Put_header_sets_the_fields_over_whatever_was_there_and_audits_the_names_only()
    {
        var host = Host.Create(_baseFactory, headerStage: true);
        var tenantId = Guid.NewGuid();
        var quoteId = (await UploadAsync(host, tenantId)).GetProperty("id").GetGuid();

        var response = await PutHeaderAsync(
            host, tenantId, quoteId, new { supplier = "Contoso Ltd", currency = "gbp", purchaseDate = "2026-09-01" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Contoso Ltd", body.GetProperty("supplier").GetString());
        Assert.Equal("GBP", body.GetProperty("currency").GetString());
        Assert.Equal("IT", body.GetProperty("geography").GetString());
        Assert.Equal("2026-09-01", body.GetProperty("purchaseDate").GetString());
        var quote = (await host.ReadQuotesAsync()).Single();
        Assert.Equal("Contoso Ltd", quote.Supplier);
        Assert.Equal("GBP", quote.Currency);
        var audit = Assert.Single(host.Audit.Entries, e => e.Action == "quote.header.updated");
        Assert.Equal("supplier,currency,purchaseDate", audit.Detail);
        Assert.DoesNotContain("Contoso", audit.Detail);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"currency":"euro"}""")]
    [InlineData("""{"currency":"EU"}""")]
    [InlineData("""{"supplier":"X"}""")]
    [InlineData("""{"purchaseDate":"1999-01-01"}""")]
    [InlineData("""{"purchaseDate":"2099-01-01"}""")]
    [InlineData("""{"geography":" "}""")]
    public async Task Put_header_refuses_an_empty_request_or_an_invalid_value_and_changes_nothing(string json)
    {
        var host = Host.Create(_baseFactory, headerStage: true);
        var tenantId = Guid.NewGuid();
        var quoteId = (await UploadAsync(host, tenantId)).GetProperty("id").GetGuid();
        var before = (await host.ReadQuotesAsync()).Single();

        var response = await PutRawAsync(host, tenantId, quoteId, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = (await host.ReadQuotesAsync()).Single();
        Assert.Equal(before.Supplier, after.Supplier);
        Assert.Equal(before.Currency, after.Currency);
        Assert.Equal(before.Geography, after.Geography);
        Assert.Equal(before.PurchaseDate, after.PurchaseDate);
        Assert.DoesNotContain(host.Audit.Entries, e => e.Action == "quote.header.updated");
    }

    [Fact]
    public async Task Put_header_of_an_unknown_or_another_tenants_quote_is_404_and_a_non_guid_is_400()
    {
        var host = Host.Create(_baseFactory, headerStage: false);
        var quoteId = (await UploadAsync(host, Guid.NewGuid())).GetProperty("id").GetGuid();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await PutHeaderAsync(host, Guid.NewGuid(), Guid.NewGuid(), new { supplier = "Contoso Ltd" })).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await PutHeaderAsync(host, Guid.NewGuid(), quoteId, new { supplier = "Contoso Ltd" })).StatusCode);

        var client = host.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/quotes/not-a-guid/header")
        {
            Content = JsonContent.Create(new { supplier = "Contoso Ltd" }),
        };
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(request)).StatusCode);
        Assert.Null((await host.ReadQuotesAsync()).Single().Supplier);
    }

    // ----- helpers -----

    private static Result<AiExtractionResult> Extracted(string payloadJson) =>
        Result<AiExtractionResult>.Success(new AiExtractionResult(
            payloadJson,
            new AiCallMetadata("test-model", "1", "test", DateTimeOffset.UtcNow, "hash")));

    private static async Task<JsonElement> UploadAsync(
        Host host, Guid tenantId, params (string Name, string Value)[] fields)
    {
        var client = host.Factory.CreateClient();
        var file = new ByteArrayContent(BuildPdf(QuoteText));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using var content = new MultipartFormDataContent { { file, "file", "quote.pdf" } };
        foreach (var (name, value) in fields)
        {
            content.Add(new StringContent(value), name);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/quotes") { Content = content };
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static Task<HttpResponseMessage> PutHeaderAsync(Host host, Guid tenantId, Guid quoteId, object body) =>
        PutRawAsync(host, tenantId, quoteId, JsonSerializer.Serialize(body));

    private static async Task<HttpResponseMessage> PutRawAsync(Host host, Guid tenantId, Guid quoteId, string json)
    {
        var client = host.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/quotes/{quoteId}/header")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        return await client.SendAsync(request);
    }

    private static byte[] BuildPdf(string text) =>
        Encoding.Latin1.GetBytes(
            "%PDF-1.4\n1 0 obj << /Type /Page >> endobj\n2 0 obj << /Length 0 >>\nstream\n" +
            $"BT ({text}) Tj ET\nendstream\nendobj\n%%EOF\n");

    private sealed class Host
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
        private readonly List<string> _stages = [];

        public WebApplicationFactory<Program> Factory { get; private set; } = null!;
        public required RecordingAuditWriter Audit { get; init; }

        /// <summary>The stage name of every `extract` call, in order.</summary>
        public IReadOnlyList<string> Stages
        {
            get
            {
                lock (_stages)
                {
                    return [.. _stages];
                }
            }
        }

        /// <summary>What the header stage answers; the default is a confident, complete header.</summary>
        public Func<AiExtractionRequest, Result<AiExtractionResult>> HeaderAnswer { get; set; } = _ => Extracted(Header);

        public static Host Create(WebApplicationFactory<Program> baseFactory, bool headerStage)
        {
            var gateway = new RecordingAiGateway(
                new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now), new AiGatewayOcrOptions()));
            var storage = new RecordingDocumentStorage();
            var audit = new RecordingAuditWriter();
            var quotesDbName = $"quotes-{Guid.NewGuid()}";
            var host = new Host { Audit = audit };

            gateway.ExtractOverride = (request, _) =>
            {
                lock (host._stages)
                {
                    host._stages.Add(request.StageName);
                }

                return Task.FromResult(
                    request.StageName == QuoteExtractionStages.HeaderStageName
                        ? host.HeaderAnswer(request)
                        : Extracted(Lines));
            };

            var factory = baseFactory
                .WithWebHostBuilder(builder =>
                {
                    if (headerStage)
                    {
                        builder.UseSetting("QuoteExtraction:HeaderStageEnabled", "true");
                    }
                })
                .WithPresentedCallersAsMembers()
                .WithInMemoryAskEngine(gateway, new FixedClock(Now), storage, audit)
                .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<QuotesDbContext>>();
                    services.RemoveAll<QuotesDbContext>();
                    services.AddDbContext<QuotesDbContext>(o => o
                        .UseInMemoryDatabase(quotesDbName)
                        .UseInternalServiceProvider(InMemoryAskEngineFactory.InMemoryProviderServices));
                }));

            host.Factory = factory;
            return host;
        }

        public async Task<IReadOnlyList<Quote>> ReadQuotesAsync()
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
            return await db.Quotes.AsNoTracking().ToListAsync();
        }
    }
}
