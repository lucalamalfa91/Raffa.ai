using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.AiGateway.Tests.TestSupport;

namespace Raffa.AiGateway.Tests;

/// <summary>
/// us-01-ai-gateway-classification, parent story Definition of Done: "classification fixture
/// returns expected type". These tests are that proof, plus the surrounding AC-1/AC-2 contract
/// (config-selected model id, confidence, reproducibility metadata).
/// </summary>
public class FixtureAiGatewayClassifyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static FixtureAiGateway CreateGateway(AiGatewayModelOptions? options = null) =>
        new(options ?? new AiGatewayModelOptions(), new FixedClock(Now));

    [Fact]
    public async Task Classify_recognizes_master_services_agreement_text_as_Msa()
    {
        var gateway = CreateGateway();

        var result = await gateway.ClassifyAsync(
            new AiClassificationRequest("This MASTER SERVICES AGREEMENT is entered into as of ..."));

        Assert.True(result.IsSuccess);
        Assert.Equal(AiDocumentType.Msa, result.Value.DocumentType);
        Assert.True(result.Value.Confidence > 0.95);
    }

    [Theory]
    [InlineData("ORDER FORM #4471 for additional seats", AiDocumentType.OrderForm)]
    [InlineData("STATEMENT OF WORK - Phase 2 implementation", AiDocumentType.Sow)]
    [InlineData("AMENDMENT NO. 3 to the existing service contract", AiDocumentType.Amendment)]
    [InlineData("QUOTE valid for 30 days", AiDocumentType.Quote)]
    [InlineData("INVOICE due within 30 days", AiDocumentType.Invoice)]
    [InlineData("2026 PRICE LIST", AiDocumentType.PriceList)]
    [InlineData("NON-DISCLOSURE AGREEMENT between the parties", AiDocumentType.Nda)]
    [InlineData("DATA PROCESSING AGREEMENT (GDPR Article 28)", AiDocumentType.Dpa)]
    public async Task Classify_recognizes_each_taxonomy_member(string documentText, AiDocumentType expected)
    {
        var gateway = CreateGateway();

        var result = await gateway.ClassifyAsync(new AiClassificationRequest(documentText));

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.DocumentType);
    }

    [Fact]
    public async Task Classify_falls_back_to_Other_for_unrecognized_text()
    {
        var gateway = CreateGateway();

        var result = await gateway.ClassifyAsync(
            new AiClassificationRequest("Dear team, please find attached the quarterly newsletter."));

        Assert.True(result.IsSuccess);
        Assert.Equal(AiDocumentType.Other, result.Value.DocumentType);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Classify_fails_for_empty_or_whitespace_text(string documentText)
    {
        var gateway = CreateGateway();

        var result = await gateway.ClassifyAsync(new AiClassificationRequest(documentText));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Classify_result_metadata_carries_the_configured_model_id_and_reproducibility_fields()
    {
        var options = new AiGatewayModelOptions
        {
            Classify = new AiModelSelection("test-classify-model", "2026-09-01"),
        };
        var gateway = CreateGateway(options);

        var result = await gateway.ClassifyAsync(new AiClassificationRequest("MSA renewal terms"));

        Assert.True(result.IsSuccess);
        var metadata = result.Value.Metadata;
        Assert.Equal("test-classify-model", metadata.ModelId);
        Assert.Equal("2026-09-01", metadata.ModelVersion);
        Assert.Equal(Now, metadata.RespondedAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(metadata.InputHash));
    }

    [Fact]
    public async Task Classify_input_hash_is_deterministic_for_the_same_text_and_differs_for_different_text()
    {
        var gateway = CreateGateway();

        var first = await gateway.ClassifyAsync(new AiClassificationRequest("MSA text A"));
        var second = await gateway.ClassifyAsync(new AiClassificationRequest("MSA text A"));
        var third = await gateway.ClassifyAsync(new AiClassificationRequest("MSA text B"));

        Assert.Equal(first.Value.Metadata.InputHash, second.Value.Metadata.InputHash);
        Assert.NotEqual(first.Value.Metadata.InputHash, third.Value.Metadata.InputHash);
    }
}
