namespace Raffa.AiGateway.Contracts;

/// <summary>
/// Output of the `ocr` role: one <see cref="AiOcrPage"/> per page the provider (or, today, the
/// fixture) recognized, in document order, for the <em>entire</em> submitted document (ADR-017:
/// "no 2-page cap"). <see cref="Pages"/>.Count IS the page count ADR-017 requires logging
/// alongside model id/version/timestamp/input hash (Appendix C rule 8 "OCR spend observable") —
/// callers and <see cref="Logging.LoggingAiGateway"/> do not need a separate counter. Since ADR-017
/// w18, each page's geometry (<see cref="AiOcrPage.Words"/>, `prebuilt-layout`) flows on
/// <see cref="Pages"/> too — this record's own shape is unchanged, it now just carries a richer
/// <see cref="AiOcrPage"/>.
/// </summary>
/// <param name="Pages">
/// Every page's recognized text (and, since ADR-017 w18, geometry), 1-based, in document order.
/// Never truncated — a caller-configured page budget must reject the call outright (see
/// <see cref="Configuration.AiGatewayOcrOptions.MaxPagesPerDocument"/>), not ask this result to
/// silently drop pages past some limit (ADR-017: "fail visibly ... never silently truncate").
/// </param>
/// <param name="Metadata">Reproducibility metadata for this call (ADR-004, ADR-011, ADR-017).</param>
public sealed record AiOcrResult(
    IReadOnlyList<AiOcrPage> Pages,
    AiCallMetadata Metadata);
