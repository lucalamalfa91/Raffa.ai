using Raffa.AiFlows.DocumentExtraction.Schemas;
using System.Security.Cryptography;
using System.Text;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.AiFlows.Tests.DocumentExtraction.Orchestration;

/// <summary>
/// F5 PR-1: the JSON Schema each stage sends to the model is part of the model's behaviour. These
/// SHA-256 digests were captured from the schemas as they were before the store extraction; any later
/// change (including moving the builders to another assembly) must keep them byte-identical, or bump
/// the pipeline version on purpose and update the digests in the same commit.
/// </summary>
public sealed class StagedExtractionSchemaSnapshotTests
{
    private static readonly Dictionary<ExtractionStage, string> ExpectedDigests = new()
    {
        [ExtractionStage.Metadata] =
            "4d862ec8694ff869451e36f4c40c5ca2436bec789a1cbb758a6b8405dced53f9",
        [ExtractionStage.CommercialTerms] =
            "224b4ab5ead613ea96f0a72286e50623d0b703966b3e74454b97882a39e483c8",
        [ExtractionStage.DatesAndRenewalTerms] =
            "e6c42f05268df274215a56d43f55c6a15c259d7a05ee9c5e1a4ef6633f2d1066",
        [ExtractionStage.LineItems] =
            "255e28562e0d2bdd6d0220ad35639b878caff5d1f159354b6260328122b58119",
        [ExtractionStage.LegalClauses] =
            "cf720191b94b0f3741de545dd50260d9820e3322ddec0e7604b7f16dda3e4f74",
        [ExtractionStage.Obligations] =
            "8a92f09573b4abc013cd85e8211520b0803fe0548d5d655cb809d83d8c2e9eb1",
        [ExtractionStage.Risk] =
            "e45265eefd1db228b670731f99151d89eeb0c2b8ea6f6d532fb6005fa9207f11",
    };

    private static string SchemaOf(ExtractionStage stage) => stage switch
    {
        ExtractionStage.Metadata => StagedExtractionJsonSchemas.Facts(ExtractionStageFields.Metadata),
        ExtractionStage.CommercialTerms => StagedExtractionJsonSchemas.Facts(ExtractionStageFields.CommercialTerms),
        ExtractionStage.DatesAndRenewalTerms => StagedExtractionJsonSchemas.Facts(ExtractionStageFields.DatesAndRenewalTerms),
        ExtractionStage.LineItems => StagedExtractionJsonSchemas.LineItems(),
        ExtractionStage.LegalClauses => StagedExtractionJsonSchemas.Clauses(),
        ExtractionStage.Obligations => StagedExtractionJsonSchemas.Obligations(),
        ExtractionStage.Risk => StagedExtractionJsonSchemas.Risks(),
        _ => throw new ArgumentOutOfRangeException(nameof(stage)),
    };

    public static TheoryData<ExtractionStage> Stages => new(ExtractionPipeline.Stages);

    [Fact]
    public void The_snapshot_covers_exactly_the_pipeline_stages()
    {
        Assert.Equal(
            ExtractionPipeline.Stages.OrderBy(s => s),
            ExpectedDigests.Keys.OrderBy(s => s));
    }

    [Theory]
    [MemberData(nameof(Stages))]
    public void The_schema_sent_for_a_stage_is_byte_identical_to_the_pinned_snapshot(ExtractionStage stage)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SchemaOf(stage)))).ToLowerInvariant();

        Assert.Equal(ExpectedDigests[stage], digest);
    }
}
