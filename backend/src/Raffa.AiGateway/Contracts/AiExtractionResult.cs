namespace Raffa.AiGateway.Contracts;

/// <summary>
/// Output of the `extract` role. <see cref="PayloadJson"/> is the model's raw structured-output
/// JSON, unparsed and unvalidated by the gateway — schema validation and per-field source/
/// confidence handling belong to the caller (task E02/F01/US02/T01, staged extraction), which
/// knows the domain shape the gateway deliberately does not.
/// </summary>
/// <param name="PayloadJson">Raw JSON text produced against the request's <see cref="AiExtractionRequest.JsonSchema"/>.</param>
/// <param name="Metadata">Reproducibility metadata for this call (ADR-004, ADR-011).</param>
public sealed record AiExtractionResult(
    string PayloadJson,
    AiCallMetadata Metadata);
