using System.IO.Compression;
using System.Net;
using Raffa.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Raffa.Api.Tests;

/// <summary>
/// Host-level proof that <c>Program.cs</c> compresses the JSON the SPA reads (latency work, phase D):
/// Brotli when the client accepts it, Gzip as the fallback, nothing at all when the client
/// advertises no encoding, and the decoded body byte-identical to the uncompressed one -- the wire
/// contract does not change, only its size. <c>GET /api/capabilities</c> is the probe: a real route
/// of the real host that answers a static JSON catalog without touching a database.
/// </summary>
public sealed class ResponseCompressionTests : IClassFixture<RaffaApiFactory>
{
    private const string JsonRoute = "/api/capabilities";

    private readonly WebApplicationFactory<Program> _factory;

    public ResponseCompressionTests(RaffaApiFactory factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Storage", "UseDevelopmentStorage=true");
            // Compression is the subject here; keep the host's background warm-up out of it.
            builder.UseSetting("Startup:Warmup:Enabled", "false");
        });
    }

    [Fact]
    public async Task Json_is_brotli_compressed_when_the_client_accepts_it_and_decodes_to_the_same_body()
    {
        var plain = await GetAsync(JsonRoute, acceptEncoding: null);
        var compressed = await GetAsync(JsonRoute, acceptEncoding: "br, gzip");

        Assert.Equal(HttpStatusCode.OK, compressed.StatusCode);
        Assert.Equal(["br"], compressed.ContentEncoding);
        Assert.Equal(plain.Body, Decode(compressed.Raw, "br"));
        Assert.True(compressed.Raw.Length < plain.Raw.Length, "A compressed catalog must be smaller on the wire.");
        Assert.Contains("Accept-Encoding", compressed.Vary);
    }

    [Fact]
    public async Task Gzip_is_the_fallback_for_a_client_that_does_not_advertise_brotli()
    {
        var plain = await GetAsync(JsonRoute, acceptEncoding: null);
        var compressed = await GetAsync(JsonRoute, acceptEncoding: "gzip");

        Assert.Equal(["gzip"], compressed.ContentEncoding);
        Assert.Equal(plain.Body, Decode(compressed.Raw, "gzip"));
    }

    [Fact]
    public async Task A_client_that_advertises_no_encoding_gets_the_body_untouched()
    {
        var plain = await GetAsync(JsonRoute, acceptEncoding: null);

        Assert.Empty(plain.ContentEncoding);
        Assert.Equal("application/json", plain.ContentType);
        Assert.StartsWith("{", plain.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Compression_is_limited_to_json_and_text_and_enabled_for_https()
    {
        var options = _factory.Services.GetRequiredService<IOptions<ResponseCompressionOptions>>().Value;

        Assert.True(options.EnableForHttps);
        Assert.Contains("application/json", options.MimeTypes);
        Assert.Contains("application/problem+json", options.MimeTypes);
        Assert.Contains("text/plain", options.MimeTypes);

        // The document preview answers a PNG (already compressed) and must never be re-encoded;
        // an SSE stream would be buffered by the compressor. Neither may ever be listed.
        Assert.DoesNotContain("image/png", options.MimeTypes);
        Assert.DoesNotContain("text/event-stream", options.MimeTypes);
        Assert.DoesNotContain(options.MimeTypes, mime => mime.EndsWith("/*", StringComparison.Ordinal));
    }

    [Fact]
    public void Both_providers_run_at_the_fastest_level()
    {
        var services = _factory.Services;

        Assert.Equal(CompressionLevel.Fastest, services.GetRequiredService<IOptions<BrotliCompressionProviderOptions>>().Value.Level);
        Assert.Equal(CompressionLevel.Fastest, services.GetRequiredService<IOptions<GzipCompressionProviderOptions>>().Value.Level);

    }

    private async Task<Result> GetAsync(string route, string? acceptEncoding)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        if (acceptEncoding is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);
        }

        using var response = await client.SendAsync(request);
        var raw = await response.Content.ReadAsByteArrayAsync();

        return new Result(
            response.StatusCode,
            [.. response.Content.Headers.ContentEncoding],
            response.Content.Headers.ContentType?.MediaType,
            [.. response.Headers.Vary],
            raw);
    }

    private static string Decode(byte[] raw, string encoding)
    {
        using var input = new MemoryStream(raw);
        using Stream decoder = encoding == "br"
            ? new BrotliStream(input, CompressionMode.Decompress)
            : new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(decoder);
        return reader.ReadToEnd();
    }

    private sealed record Result(
        HttpStatusCode StatusCode,
        string[] ContentEncoding,
        string? ContentType,
        string[] Vary,
        byte[] Raw)
    {
        public string Body => System.Text.Encoding.UTF8.GetString(Raw);
    }
}
