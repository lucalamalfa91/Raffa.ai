namespace Raffa.AiGateway.Contracts;

/// <summary>
/// Input to the `extract` role: one bounded, schema-constrained extraction stage (product spec
/// §7.2 — "avoid one giant prompt"; ADR-004 — "structured-output-capable... not free text").
/// </summary>
/// <param name="StageName">
/// Caller-owned stage label for prompt selection and logging (e.g. "commercial-terms",
/// "dates-and-renewal-terms" — mirrors, but does not reference,
/// <c>Raffa.Documents.Contracts.Domain.ExtractionStage</c>; the gateway must not depend on a
/// domain module's enum, so this stays a free-form string).
/// </param>
/// <param name="DocumentText">The (sub-)document text this stage extracts from.</param>
/// <param name="JsonSchema">
/// JSON Schema the model's structured output must satisfy (product spec §7.3's
/// <c>source</c>/<c>confidence</c>-per-field shape is expressed here by the caller, not hard-coded
/// in the gateway).
/// </param>
public sealed record AiExtractionRequest(
    string StageName,
    string DocumentText,
    string JsonSchema);
