namespace Raffa.AiGateway.Contracts;

/// <summary>
/// Output of the `classify` role (us-01-ai-gateway-classification AC-2: "returns a type and
/// confidence, logging model/version/prompt/timestamp/input-hash").
/// </summary>
/// <param name="DocumentType">The recognized document type.</param>
/// <param name="Confidence">Model confidence in <c>[0, 1]</c>.</param>
/// <param name="Metadata">Reproducibility metadata for this call (ADR-004, ADR-011).</param>
public sealed record AiClassificationResult(
    AiDocumentType DocumentType,
    double Confidence,
    AiCallMetadata Metadata);
