namespace Raffa.AiGateway.Contracts;

/// <summary>
/// One recognized page of `ocr`-role output (ADR-017 "Implications for the decomposition": "must
/// persist a page map so evidence source.page / section still resolve"). Deliberately the same
/// shape as
/// <see cref="Raffa.Documents.Contracts.Application.Extraction.DocumentPageText"/> — the gateway
/// cannot reference that domain type directly (ADR-002: this project must stay domain-agnostic),
/// so this is the gateway-side twin the caller maps 1:1 once the OCR call returns.
/// </summary>
/// <param name="PageNumber">1-based page number, matching how a human would cite "page N".</param>
/// <param name="Text">The page's recognized text.</param>
public sealed record AiOcrPage(int PageNumber, string Text);
