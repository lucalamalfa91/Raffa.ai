namespace Raffa.AiGateway.Contracts;

/// <summary>
/// Input to the `ocr` role (ADR-017), the AI Gateway's fifth role, added by task
/// E02/F01/US02/T02 (hybrid-ocr). Unlike <see cref="AiClassificationRequest"/> and
/// <see cref="AiExtractionRequest"/> — which both operate on text some earlier step already
/// produced — this role's entire job is turning raw document bytes into text, so it is the one
/// role whose input is bytes rather than a string.
///
/// ADR-017 "no 2-page cap" / "full document": there is no page-range parameter on this request.
/// <see cref="Content"/> is always the complete, unmodified document; a caller that wants to
/// enforce a safety budget must decide *before* calling this role (or rely on the gateway's own
/// configured budget, <see cref="Configuration.AiGatewayOcrOptions.MaxPagesPerDocument"/>), never
/// by slicing pages off <see cref="Content"/> itself — that would be exactly the "2-page cap"
/// ADR-017 forbids.
/// </summary>
/// <param name="FileName">Original file name, for logging/diagnostics only — never parsed for routing.</param>
/// <param name="MimeType">Caller-declared content type (e.g. "application/pdf"), so a real provider can pick Read vs Layout.</param>
/// <param name="Content">The complete, unmodified document bytes.</param>
public sealed record AiOcrRequest(
    string FileName,
    string MimeType,
    ReadOnlyMemory<byte> Content);
