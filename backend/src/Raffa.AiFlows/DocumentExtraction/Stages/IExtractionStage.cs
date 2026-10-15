using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.AiFlows.DocumentExtraction.Stages;

/// <summary>
/// One extraction stage = one agent = one task: its name, its strict-mode JSON schema, the gateway request
/// it sends and the parsing of the model's payload into typed facts. Stateless; the orchestrator owns
/// scheduling and the store owns persistence.
/// </summary>
public interface IExtractionStage
{
    ExtractionStage Stage { get; }

    /// <summary>Caller-owned label of the <c>extract</c> call: always <c>Stage.ToString()</c>. Contract with
    /// the fixture extractor (case-insensitive keys), the scripted test gateways and the audit step label.</summary>
    string StageName { get; }

    /// <summary>F5-T05: stages whose parsed output this stage wants as context. Honoured only when
    /// <c>ExtractionOrchestrationOptions.UseContextDependencies</c> is true; ignored otherwise (today's
    /// behaviour). The three scalar stages declare none; the four list stages declare the three scalar ones.</summary>
    IReadOnlyList<ExtractionStage> DependsOn { get; }

    /// <summary>Strict-mode JSON schema, built once.</summary>
    string JsonSchema { get; }

    AiExtractionRequest BuildRequest(ExtractionStageInput input);

    /// <summary>Deserialises the payload. Throws <see cref="System.Text.Json.JsonException"/> on malformed
    /// JSON; never returns null (an empty object becomes an empty fact list).</summary>
    StageFacts Parse(string payloadJson);
}
