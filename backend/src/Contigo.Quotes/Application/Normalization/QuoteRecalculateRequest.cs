namespace Contigo.Quotes.Application.Normalization;

/// <summary>
/// Request body for `POST /api/quotes/{id}/assessment/recalculate` (task E05/F01/US02/T02,
/// sku-recalculate; product spec Appendix A "Re-run after product mapping correction"). Lives next
/// to <see cref="SkuMappingService"/> rather than <c>Contigo.Api</c> — same reason
/// <c>Contigo.Documents.Contracts.Application.ContractCorrectionRequest</c>/<c>Contigo.Quotes
/// .Application.Outcome.NegotiationOutcomeCaptureRequest</c> live next to their own services (see
/// either type's own doc comment): <c>Contigo.ArchitectureTests.DependencyDirectionTests
/// .Host_must_not_contain_domain_types</c> only inspects the <c>Contigo.Api</c>/<c>Contigo.Worker</c>
/// assemblies, so a request contract living there would be flagged as business logic leaking into a
/// host that must stay a thin composition root.
///
/// <see cref="Mappings"/> is optional and may be empty/omitted — a caller can invoke this endpoint
/// with no corrections at all just to re-read the current unmatched-line list and a fresh
/// <see cref="Assessment.QuoteMarketAssessment"/> (e.g. right after `POST /api/quotes` reports a
/// non-zero `unmatchedSkuCount`, before any correction has been decided) — see
/// <see cref="SkuMappingService.RecalculateAsync"/>'s own doc comment.
/// </summary>
public sealed record QuoteRecalculateRequest(IReadOnlyList<SkuMappingCorrection>? Mappings);
