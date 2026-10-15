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

    public IReadOnlyList<ExtractionStage> DependsOn => [];

    public string JsonSchema { get; } = jsonSchema;

    public AiExtractionRequest BuildRequest(ExtractionStageInput input) =>
        new(StageName, input.DocumentText, JsonSchema);

    public abstract StageFacts Parse(string payloadJson);
}
