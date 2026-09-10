using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.AiGateway.Tests.TestSupport;

namespace Raffa.AiGateway.Tests;

public class FixtureAiGatewayExtractTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static FixtureAiGateway CreateGateway() =>
        new(new AiGatewayModelOptions(), new FixedClock(Now));

    [Fact]
    public async Task Extract_returns_parseable_json_payload_and_configured_model_metadata()
    {
        var options = new AiGatewayModelOptions
        {
            Extract = new AiModelSelection("test-extract-model", "1"),
        };
        var gateway = new FixtureAiGateway(options, new FixedClock(Now));

        var result = await gateway.ExtractAsync(new AiExtractionRequest(
            StageName: "commercial-terms",
            DocumentText: "Auto-renewal for 12 months, 90 days cancellation notice.",
            JsonSchema: """{"type":"object","properties":{"auto_renewal":{"type":"boolean"}}}"""));

        Assert.True(result.IsSuccess);
        Assert.Equal("test-extract-model", result.Value.Metadata.ModelId);

        // Must be valid JSON — the caller (staged extraction, task E02/F01/US02/T01) parses this
        // against its own schema; a fixture that returned non-JSON would break every consumer.
        using var document = JsonDocument.Parse(result.Value.PayloadJson);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
    }

    [Fact]
    public async Task Extract_fails_without_document_text()
    {
        var gateway = CreateGateway();

        var result = await gateway.ExtractAsync(new AiExtractionRequest(
            StageName: "metadata", DocumentText: "", JsonSchema: "{}"));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Extract_fails_without_a_json_schema()
    {
        var gateway = CreateGateway();

        var result = await gateway.ExtractAsync(new AiExtractionRequest(
            StageName: "metadata", DocumentText: "some contract text", JsonSchema: ""));

        Assert.True(result.IsFailure);
    }
}
