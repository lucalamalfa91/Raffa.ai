using System.Text.Json;
using System.Text.Json.Serialization;
using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.AiFlows.DocumentExtraction.Stages;

/// <summary>Shared deserialisation settings of every stage payload (the options the pipeline always used).</summary>
internal static class StagePayload
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static T? Deserialize<T>(string payloadJson) => JsonSerializer.Deserialize<T>(payloadJson, Options);
}

/// <summary>Name, schema and request are identical in shape for all seven stages.</summary>
internal abstract class ExtractionStageBase(ExtractionStage stage, string jsonSchema) : IExtractionStage
{
    public ExtractionStage Stage { get; } = stage;

    public string StageName { get; } = stage.ToString();

    /// <summary>The scalar stages, in pipeline order: what every list stage depends on.</summary>
    protected static readonly IReadOnlyList<ExtractionStage> ScalarStages =
        [ExtractionStage.Metadata, ExtractionStage.CommercialTerms, ExtractionStage.DatesAndRenewalTerms];

    public virtual IReadOnlyList<ExtractionStage> DependsOn => [];

    public string JsonSchema { get; } = jsonSchema;

    /// <summary>Byte-identical to the pipeline's historic request when <see cref="ExtractionStageInput.Context"/>
    /// yields no block (always the case with context dependencies off).</summary>
    public AiExtractionRequest BuildRequest(ExtractionStageInput input) =>
        input.DocumentFirst
            // F5-T03: the document leads, so the prefix is shared by every stage; the context follows it.
            ? new(StageName, input.DocumentText + ExtractionContextBlock.Prefix(input.Context, DependsOn), JsonSchema, DocumentFirst: true)
            : new(StageName, ExtractionContextBlock.Prefix(input.Context, DependsOn) + input.DocumentText, JsonSchema);

    public abstract StageFacts Parse(string payloadJson);
}
