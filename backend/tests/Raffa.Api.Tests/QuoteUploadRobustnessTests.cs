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
using Raffa.Documents.Contracts.Application.Admission;
using Raffa.Quotes.Application.Extraction;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Api.Tests;

/// <summary>
/// <c>POST /api/quotes</c> (tasks F6-T02 and F6-T04): the request-level admission now matches
/// <c>POST /api/documents</c> (413 over <c>Documents:MaxFileBytes</c>, 415 for a format the
/// extension and magic bytes do not agree on, both before any storage, parse or model call); a
/// crash anywhere in the synchronous extraction leaves the quote and its job <c>Failed</c> with a
/// typed error (never <c>Queued</c>/<c>Running</c> forever, never half-persisted lines); the same
/// bytes are not extracted twice; and an extracted row with an impossible value is discarded and
/// counted. Runs on the in-memory host with a call-recording gateway whose <c>extract</c> role the
/// test scripts, so no Postgres, Docker or Foundry is needed.
/// </summary>
public sealed class QuoteUploadRobustnessTests : IClassFixture<RaffaApiFactory>
{
    private const string QuoteText =
        "QUOTE number Q-2026-0417 from Northwind Traders for Fabrikam Inc, valid for thirty days. " +
        "Line 1: Cloud Suite Enterprise, 250 seats, unit price EUR 96.00 per seat per year. " +
        "Line 2: Premium support, one year, EUR 4,800. Payment terms net thirty.";

    private const string ValidPayload = """
        {"items":[
            {"sku":"CS-ENT","edition":"Enterprise","description":"Cloud Suite Enterprise","quantity":250,
             "unit":"seat","unitPrice":96,"listPrice":null,"discountPercent":null,"term":"Annual",
             "sourcePage":1,"sourceSpan":"250 seats, unit price EUR 96.00","confidence":0.95}
        ]}
        """;

    private readonly WebApplicationFactory<Program> _baseFactory;

    public QuoteUploadRobustnessTests(RaffaApiFactory factory)
    {
        _baseFactory = factory;
    }

    // ----- F6-T04: parity with POST /api/documents (413 / 415) -----

    [Fact]
    public async Task File_over_the_configured_limit_returns_413_before_any_storage_parse_or_model_call()
    {
        var host = Host.Create(_baseFactory, settings => settings["Documents:MaxFileBytes"] = "2097152");
        var client = host.Factory.CreateClient();
        var threeMegabytes = new byte[3 * 1024 * 1024];
        "%PDF-1.4\n"u8.CopyTo(threeMegabytes);
        using var content = Multipart(threeMegabytes, "huge.pdf", "application/pdf");
        using var request = Upload(content, Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        // Same body as POST /api/documents.
        Assert.Equal(
            "Raffa accepts files up to 2 MB. This file is larger.",
            await response.Content.ReadFromJsonAsync<string>());
        Assert.Empty(host.Gateway.Calls);
        Assert.Empty(host.Storage.Saved);
        Assert.Empty(host.Audit.Entries);
    }

    [Fact]
    public async Task Zip_renamed_pdf_returns_415_without_touching_the_ai_gateway_or_storage()
    {
        var host = Host.Create(_baseFactory);
        var client = host.Factory.CreateClient();
        var zipBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00 };
        using var content = Multipart(zipBytes, "quote.pdf", "application/pdf");
        using var request = Upload(content, Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(DocumentFormatSniffer.RejectionMessage, await response.Content.ReadFromJsonAsync<string>());
        Assert.Empty(host.Gateway.Calls);
        Assert.Empty(host.Storage.Saved);
        Assert.Empty(host.Audit.Entries);
        Assert.Empty(await host.ReadQuotesAsync());
    }

    [Theory]
    [InlineData("quote.tiff")]
    [InlineData("quote.txt")]
    [InlineData("quote")]
    public async Task Unsupported_extension_returns_415_even_with_pdf_bytes(string fileName)
    {
        var host = Host.Create(_baseFactory);
        var client = host.Factory.CreateClient();
        using var content = Multipart(BuildPdf(QuoteText), fileName);
        using var request = Upload(content, Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(host.Gateway.Calls);
        Assert.Empty(host.Storage.Saved);
    }

    [Fact]
    public async Task A_pdf_sent_as_octet_stream_is_admitted_and_stored_with_the_sniffed_mime_type()
    {
        var host = Host.Create(_baseFactory);
        host.Gateway.ExtractOverride = (_, _) => Task.FromResult(Extracted(ValidPayload));
        var client = host.Factory.CreateClient();
        using var content = Multipart(BuildPdf(QuoteText), "quote.pdf", "application/octet-stream");
        using var request = Upload(content, Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("application/pdf", body.RootElement.GetProperty("mimeType").GetString());
        Assert.Equal("Completed", body.RootElement.GetProperty("processingStatus").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("lineItemCount").GetInt32());
        Assert.Single(host.Storage.Saved);
    }

    // ----- F6-T02: a crash never leaves Queued/Running rows -----

    [Fact]
    public async Task A_model_call_that_throws_leaves_the_quote_and_job_failed_with_a_typed_error()
    {
        var host = Host.Create(_baseFactory);
        host.Gateway.ExtractOverride = (_, _) => throw new InvalidOperationException("foundry credential exploded");
        var tenantId = Guid.NewGuid();

        var body = await UploadOkAsync(host, tenantId, BuildPdf(QuoteText), "quote.pdf");

        // The upload itself is durable and reported honestly: Failed, not the pre-processing "Uploaded".
        Assert.Equal("Failed", body.GetProperty("processingStatus").GetString());
        Assert.Equal(0, body.GetProperty("lineItemCount").GetInt32());

        var state = await host.ReadStateAsync();
        var quote = Assert.Single(state.Quotes);
        Assert.Equal(QuoteProcessingStatus.Failed, quote.ProcessingStatus);
        var job = Assert.Single(state.Jobs);
        Assert.Equal(QuoteExtractionJobStatus.Failed, job.Status);
        Assert.NotNull(job.CompletedAt);
        Assert.True(QuoteExtractionFailure.TryParseKind(job.ErrorDetail, out var kind));
        Assert.Equal(QuoteExtractionFailureKind.ExtractionFailed, kind);
        Assert.Contains("InvalidOperationException", job.ErrorDetail, StringComparison.Ordinal);
        Assert.Empty(state.Lines);
        AssertNoOpenJobs(state);
        Assert.Contains(host.Audit.Entries, e => e.Action == "quote.extraction.failed");
    }

    [Fact]
    public async Task A_gateway_result_failure_is_typed_too_and_leaves_no_open_job()
    {
        var host = Host.Create(_baseFactory);
        host.Gateway.ExtractOverride = (_, _) =>
            Task.FromResult(Result<AiExtractionResult>.Failure("model endpoint unavailable"));

        var body = await UploadOkAsync(host, Guid.NewGuid(), BuildPdf(QuoteText), "quote.pdf");

        Assert.Equal("Failed", body.GetProperty("processingStatus").GetString());
        var state = await host.ReadStateAsync();
        var job = Assert.Single(state.Jobs);
        Assert.Equal(QuoteExtractionJobStatus.Failed, job.Status);
        Assert.StartsWith("[ExtractionFailed] model endpoint unavailable", job.ErrorDetail, StringComparison.Ordinal);
        AssertNoOpenJobs(state);
    }

    [Fact]
    public async Task A_malformed_model_payload_is_typed_as_such()
    {
        var host = Host.Create(_baseFactory);
        host.Gateway.ExtractOverride = (_, _) => Task.FromResult(Extracted("{ this is not json"));

        var body = await UploadOkAsync(host, Guid.NewGuid(), BuildPdf(QuoteText), "quote.pdf");

        Assert.Equal("Failed", body.GetProperty("processingStatus").GetString());
        var state = await host.ReadStateAsync();
        var job = Assert.Single(state.Jobs);
        Assert.True(QuoteExtractionFailure.TryParseKind(job.ErrorDetail, out var kind));
        Assert.Equal(QuoteExtractionFailureKind.MalformedPayload, kind);
        AssertNoOpenJobs(state);
    }

    [Fact]
    public async Task A_crash_after_the_first_save_removes_the_persisted_lines_and_leaves_no_running_job()
    {
        // The final status save is the one that throws: by then the raw lines are already committed
        // (first SaveChanges) and the job was Running in memory -- the exact orphan shape.
        var interceptor = new FailFinalStatusSaveInterceptor();
        var host = Host.Create(_baseFactory, interceptor: interceptor);
        host.Gateway.ExtractOverride = (_, _) => Task.FromResult(Extracted(ValidPayload));

        var body = await UploadOkAsync(host, Guid.NewGuid(), BuildPdf(QuoteText), "quote.pdf");

        Assert.True(interceptor.Fired, "the simulated crash must actually have happened");
        Assert.Equal("Failed", body.GetProperty("processingStatus").GetString());

        var state = await host.ReadStateAsync();
        Assert.Equal(QuoteProcessingStatus.Failed, Assert.Single(state.Quotes).ProcessingStatus);
        var job = Assert.Single(state.Jobs);
        Assert.Equal(QuoteExtractionJobStatus.Failed, job.Status);
        Assert.True(QuoteExtractionFailure.TryParseKind(job.ErrorDetail, out var kind));
        Assert.Equal(QuoteExtractionFailureKind.PersistenceFailed, kind);
        Assert.Empty(state.Lines);
        AssertNoOpenJobs(state);
    }

    [Fact]
    public async Task A_cancelled_request_marks_the_rows_failed_using_a_token_that_is_not_cancelled()
    {
        var host = Host.Create(_baseFactory);
        using var cts = new CancellationTokenSource();
        host.Gateway.ExtractOverride = (_, _) =>
        {
            // The client goes away while the model call is in flight.
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        };
        var tenantId = TenantId.New();
        var quote = new Quote
        {
            TenantId = tenantId,
            FileName = "quote.pdf",
            MimeType = DocumentFormatSniffer.PdfMimeType,
            StoragePath = $"{tenantId.Value:D}/quote.pdf",
            Checksum = "cafe",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await host.Factory.SeedQuoteAsync(quote);
        await host.SeedJobAsync(new QuoteExtractionJob
        {
            TenantId = tenantId,
            QuoteId = quote.Id,
            QueuedAt = DateTimeOffset.UtcNow,
        });

        await using (var scope = host.Factory.Services.CreateAsyncScope())
        {
            var pipeline = scope.ServiceProvider.GetRequiredService<QuoteExtractionPipeline>();
            var result = await pipeline.ProcessAsync(
                tenantId, quote.Id, "quote.pdf", DocumentFormatSniffer.PdfMimeType, BuildPdf(QuoteText), cts.Token);
            Assert.True(result.IsFailure);
        }

        var state = await host.ReadStateAsync();
        Assert.Equal(QuoteProcessingStatus.Failed, Assert.Single(state.Quotes).ProcessingStatus);
        var job = Assert.Single(state.Jobs);
        Assert.Equal(QuoteExtractionJobStatus.Failed, job.Status);
        Assert.True(QuoteExtractionFailure.TryParseKind(job.ErrorDetail, out var kind));
        Assert.Equal(QuoteExtractionFailureKind.Cancelled, kind);
        AssertNoOpenJobs(state);
    }

    [Fact]
    public async Task A_successful_extraction_is_unchanged_completed_with_its_lines_and_no_failure_audit()
    {
        var host = Host.Create(_baseFactory);
        host.Gateway.ExtractOverride = (_, _) => Task.FromResult(Extracted(ValidPayload));

        var body = await UploadOkAsync(host, Guid.NewGuid(), BuildPdf(QuoteText), "quote.pdf");

        Assert.Equal("Completed", body.GetProperty("processingStatus").GetString());
        Assert.Equal(1, body.GetProperty("lineItemCount").GetInt32());
        Assert.False(body.GetProperty("deduplicated").GetBoolean());
        var state = await host.ReadStateAsync();
        Assert.Equal(QuoteExtractionJobStatus.Completed, Assert.Single(state.Jobs).Status);
        Assert.Null(Assert.Single(state.Jobs).ErrorDetail);
        var line = Assert.Single(state.Lines);
        Assert.Equal(24000m, line.ExtendedPrice);
        Assert.DoesNotContain(host.Audit.Entries, e => e.Action == "quote.extraction.failed");
        Assert.Contains(host.Audit.Entries, e => e.Action == "quote.extraction.completed");
    }

    // ----- F6-T04: invalid rows are discarded and counted -----

    [Fact]
    public async Task Rows_with_impossible_values_are_discarded_and_counted_and_the_quote_needs_review()
    {
        const string payload = """
            {"items":[
                {"description":"Good line","quantity":2,"unitPrice":10,"confidence":0.9},
                {"description":"Negative quantity","quantity":-1,"unitPrice":10,"confidence":0.9},
                {"description":"Negative price","quantity":1,"unitPrice":-5,"confidence":0.9},
                {"description":"Discount over 100","quantity":1,"listPrice":10,"discountPercent":120,"confidence":0.9},
                {"description":"Confidence over 1","quantity":1,"unitPrice":10,"confidence":1.5}
            ]}
            """;
        var host = Host.Create(_baseFactory);
        host.Gateway.ExtractOverride = (_, _) => Task.FromResult(Extracted(payload));

        var body = await UploadOkAsync(host, Guid.NewGuid(), BuildPdf(QuoteText), "quote.pdf");

        Assert.Equal(1, body.GetProperty("lineItemCount").GetInt32());
        Assert.Equal(4, body.GetProperty("skippedLineCount").GetInt32());
        Assert.Equal(4, body.GetProperty("invalidLineCount").GetInt32());
        Assert.Equal("NeedsReview", body.GetProperty("processingStatus").GetString());
        var state = await host.ReadStateAsync();
        Assert.Equal("Good line", Assert.Single(state.Lines).Description);
    }

    // ----- F6-T04: dedup by checksum -----

    [Fact]
    public async Task Uploading_the_same_bytes_twice_returns_the_existing_quote_and_extracts_once()
    {
        var host = Host.Create(_baseFactory);
        host.Gateway.ExtractOverride = (_, _) => Task.FromResult(Extracted(ValidPayload));
        var tenantId = Guid.NewGuid();
        var pdf = BuildPdf(QuoteText);

        var first = await UploadOkAsync(host, tenantId, pdf, "quote.pdf");
        var second = await UploadOkAsync(host, tenantId, pdf, "renamed-copy.pdf");

        Assert.False(first.GetProperty("deduplicated").GetBoolean());
        Assert.True(second.GetProperty("deduplicated").GetBoolean());
        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Equal("Completed", second.GetProperty("processingStatus").GetString());
        Assert.Equal(1, second.GetProperty("lineItemCount").GetInt32());

        // One model call, one stored blob, one quote, one job, one set of lines.
        Assert.Equal(1, host.Gateway.Calls.Count(c => c == "ExtractAsync"));
        Assert.Single(host.Storage.Saved);
        var state = await host.ReadStateAsync();
        Assert.Single(state.Quotes);
        Assert.Single(state.Jobs);
        Assert.Single(state.Lines);
        Assert.Contains(host.Audit.Entries, e => e.Action == "quote.upload.deduplicated");
    }

    [Fact]
    public async Task The_same_bytes_with_a_different_supplier_are_a_new_quote_not_a_duplicate()
    {
        var host = Host.Create(_baseFactory);
        host.Gateway.ExtractOverride = (_, _) => Task.FromResult(Extracted(ValidPayload));
        var tenantId = Guid.NewGuid();
        var pdf = BuildPdf(QuoteText);

        var first = await UploadOkAsync(host, tenantId, pdf, "quote.pdf");
        var second = await UploadOkAsync(host, tenantId, pdf, "quote.pdf", supplier: "Northwind Traders");

        Assert.False(second.GetProperty("deduplicated").GetBoolean());
        Assert.NotEqual(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Equal("Northwind Traders", second.GetProperty("supplier").GetString());
        Assert.Equal(2, host.Gateway.Calls.Count(c => c == "ExtractAsync"));
    }

    [Fact]
    public async Task A_failed_upload_is_never_deduplicated_so_the_user_can_retry()
    {
        var host = Host.Create(_baseFactory);
        host.Gateway.ExtractOverride = (_, _) => throw new InvalidOperationException("transient outage");
        var tenantId = Guid.NewGuid();
        var pdf = BuildPdf(QuoteText);

        var first = await UploadOkAsync(host, tenantId, pdf, "quote.pdf");
        Assert.Equal("Failed", first.GetProperty("processingStatus").GetString());

        host.Gateway.ExtractOverride = (_, _) => Task.FromResult(Extracted(ValidPayload));
        var retry = await UploadOkAsync(host, tenantId, pdf, "quote.pdf");

        Assert.False(retry.GetProperty("deduplicated").GetBoolean());
        Assert.NotEqual(first.GetProperty("id").GetGuid(), retry.GetProperty("id").GetGuid());
        Assert.Equal("Completed", retry.GetProperty("processingStatus").GetString());
    }

    [Fact]
    public async Task The_same_bytes_in_another_tenant_are_not_a_duplicate()
    {
        var host = Host.Create(_baseFactory);
        host.Gateway.ExtractOverride = (_, _) => Task.FromResult(Extracted(ValidPayload));
        var pdf = BuildPdf(QuoteText);

        var tenantA = await UploadOkAsync(host, Guid.NewGuid(), pdf, "quote.pdf");
        var tenantB = await UploadOkAsync(host, Guid.NewGuid(), pdf, "quote.pdf");

        Assert.False(tenantB.GetProperty("deduplicated").GetBoolean());
        Assert.NotEqual(tenantA.GetProperty("id").GetGuid(), tenantB.GetProperty("id").GetGuid());
    }

    // ----- helpers -----

    private static void AssertNoOpenJobs(QuoteState state) =>
        Assert.DoesNotContain(
            state.Jobs,
            j => j.Status is QuoteExtractionJobStatus.Queued or QuoteExtractionJobStatus.Running);

    private static Result<AiExtractionResult> Extracted(string payloadJson) =>
        Result<AiExtractionResult>.Success(new AiExtractionResult(
            payloadJson,
            new AiCallMetadata("test-model", "1", "test", DateTimeOffset.UtcNow, "hash")));

    private static async Task<JsonElement> UploadOkAsync(
        Host host, Guid tenantId, byte[] bytes, string fileName, string? supplier = null)
    {
        var client = host.Factory.CreateClient();
        using var content = Multipart(bytes, fileName, "application/pdf");
        if (supplier is not null)
        {
            content.Add(new StringContent(supplier), "supplier");
        }

        using var request = Upload(content, tenantId.ToString());
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static MultipartFormDataContent Multipart(byte[] bytes, string fileName, string? contentType = null)
    {
        var file = new ByteArrayContent(bytes);
        if (contentType is not null)
        {
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }

        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static HttpRequestMessage Upload(HttpContent content, string tenantId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/quotes") { Content = content };
        request.Headers.Add("X-Tenant-Id", tenantId);
        return request;
    }

    private static byte[] BuildPdf(string text)
    {
        var pdf =
            "%PDF-1.4\n" +
            "1 0 obj << /Type /Page >> endobj\n" +
            "2 0 obj << /Length 0 >>\n" +
            "stream\n" +
            $"BT ({text}) Tj ET\n" +
            "endstream\n" +
            "endobj\n" +
            "%%EOF\n";
        return Encoding.Latin1.GetBytes(pdf);
    }

    private sealed record QuoteState(
        IReadOnlyList<Quote> Quotes, IReadOnlyList<QuoteExtractionJob> Jobs, IReadOnlyList<QuoteLine> Lines);

    /// <summary>Throws once, on the save that moves a quote to a successful terminal status -- the
    /// point after the raw lines are committed and before the job leaves Running.</summary>
    private sealed class FailFinalStatusSaveInterceptor : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired
                && eventData.Context is { } context
                && context.ChangeTracker.Entries<Quote>().Any(e =>
                    e.State == EntityState.Modified
                    && e.Entity.ProcessingStatus is QuoteProcessingStatus.Completed or QuoteProcessingStatus.NeedsReview))
            {
                Fired = true;
                throw new InvalidOperationException("simulated crash while saving the final status");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class Host
    {
        private static readonly DateTimeOffset Now = new(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);

        public required WebApplicationFactory<Program> Factory { get; init; }
        public required RecordingAiGateway Gateway { get; init; }
        public required RecordingDocumentStorage Storage { get; init; }
        public required RecordingAuditWriter Audit { get; init; }

        public static Host Create(
            WebApplicationFactory<Program> baseFactory,
            Action<Dictionary<string, string?>>? configure = null,
            SaveChangesInterceptor? interceptor = null)
        {
            var settings = new Dictionary<string, string?>();
            configure?.Invoke(settings);

            var gateway = new RecordingAiGateway(
                new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now), new AiGatewayOcrOptions()));
            var storage = new RecordingDocumentStorage();
            var audit = new RecordingAuditWriter();
            var quotesDbName = $"quotes-{Guid.NewGuid()}";

            var factory = baseFactory
                .WithWebHostBuilder(builder =>
                {
                    foreach (var (key, value) in settings)
                    {
                        builder.UseSetting(key, value);
                    }
                })
                .WithPresentedCallersAsMembers()
                .WithInMemoryAskEngine(gateway, new FixedClock(Now), storage, audit)
                .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<QuotesDbContext>>();
                    services.RemoveAll<QuotesDbContext>();
                    services.AddDbContext<QuotesDbContext>(o =>
                    {
                        o.UseInMemoryDatabase(quotesDbName)
                            .UseInternalServiceProvider(InMemoryAskEngineFactory.InMemoryProviderServices);
                        if (interceptor is not null)
                        {
                            o.AddInterceptors(interceptor);
                        }
                    });
                }));

            return new Host { Factory = factory, Gateway = gateway, Storage = storage, Audit = audit };
        }

        public async Task<IReadOnlyList<Quote>> ReadQuotesAsync()
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
            return await db.Quotes.AsNoTracking().ToListAsync();
        }

        public async Task<QuoteState> ReadStateAsync()
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
            return new QuoteState(
                await db.Quotes.AsNoTracking().ToListAsync(),
                await db.QuoteExtractionJobs.AsNoTracking().ToListAsync(),
                await db.QuoteLines.AsNoTracking().ToListAsync());
        }

        public async Task SeedJobAsync(QuoteExtractionJob job)
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
            db.QuoteExtractionJobs.Add(job);
            await db.SaveChangesAsync();
        }
    }
}
