using System.Text.Json.Serialization;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Raw structured-output shapes <see cref="StagedExtractionService"/> deserializes
/// <c>AiExtractionResult.PayloadJson</c> into, mirroring
/// <see cref="StagedExtractionJsonSchemas"/>'s shapes exactly. Every property is nullable (even
/// ones the JSON Schema marks <c>required</c>): a structured-output model is not guaranteed to
/// honour the schema perfectly, and <see cref="Raffa.AiGateway.Fixtures.FixtureAiGateway"/>
/// today always returns an empty <c>{}</c> object (no live model behind it yet) — deserializing
/// that into these types must produce nulls, not throw. <see cref="StagedExtractionService"/> is
/// responsible for validating required fields are actually present before persisting, and for
/// skipping (not crashing on) an item that is missing one.
/// </summary>
internal sealed record ExtractedFactsPayload(
    [property: JsonPropertyName("facts")] IReadOnlyList<ExtractedFact>? Facts);

internal sealed record ExtractedFact(
    [property: JsonPropertyName("field")] string? Field,
    [property: JsonPropertyName("value")] string? Value,
    [property: JsonPropertyName("sourcePage")] int? SourcePage,
    [property: JsonPropertyName("sourceSpan")] string? SourceSpan,
    [property: JsonPropertyName("confidence")] double? Confidence);

internal sealed record ExtractedLineItemsPayload(
    [property: JsonPropertyName("items")] IReadOnlyList<ExtractedLineItemFact>? Items);

internal sealed record ExtractedLineItemFact(
    [property: JsonPropertyName("sku")] string? Sku,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("quantity")] decimal? Quantity,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("unitPrice")] decimal? UnitPrice,
    [property: JsonPropertyName("listPrice")] decimal? ListPrice,
    [property: JsonPropertyName("discount")] decimal? Discount,
    [property: JsonPropertyName("billingPeriod")] string? BillingPeriod,
    [property: JsonPropertyName("annualCost")] decimal? AnnualCost,
    [property: JsonPropertyName("totalCost")] decimal? TotalCost,
    [property: JsonPropertyName("sourcePage")] int? SourcePage,
    [property: JsonPropertyName("sourceSpan")] string? SourceSpan,
    [property: JsonPropertyName("confidence")] double? Confidence);

internal sealed record ExtractedClausesPayload(
    [property: JsonPropertyName("items")] IReadOnlyList<ExtractedClauseFact>? Items);

internal sealed record ExtractedClauseFact(
    [property: JsonPropertyName("clauseType")] string? ClauseType,
    [property: JsonPropertyName("rawText")] string? RawText,
    [property: JsonPropertyName("normalizedValue")] string? NormalizedValue,
    [property: JsonPropertyName("riskLevel")] string? RiskLevel,
    [property: JsonPropertyName("sourcePage")] int? SourcePage,
    [property: JsonPropertyName("sourceSpan")] string? SourceSpan,
    [property: JsonPropertyName("confidence")] double? Confidence);

internal sealed record ExtractedObligationsPayload(
    [property: JsonPropertyName("items")] IReadOnlyList<ExtractedObligationFact>? Items);

internal sealed record ExtractedObligationFact(
    [property: JsonPropertyName("party")] string? Party,
    [property: JsonPropertyName("obligationType")] string? ObligationType,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("dueDate")] string? DueDate,
    [property: JsonPropertyName("recurrenceRule")] string? RecurrenceRule,
    [property: JsonPropertyName("criticality")] string? Criticality,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("sourcePage")] int? SourcePage,
    [property: JsonPropertyName("sourceSpan")] string? SourceSpan,
    [property: JsonPropertyName("confidence")] double? Confidence);

internal sealed record ExtractedRisksPayload(
    [property: JsonPropertyName("items")] IReadOnlyList<ExtractedRiskFact>? Items);

internal sealed record ExtractedRiskFact(
    [property: JsonPropertyName("riskType")] string? RiskType,
    [property: JsonPropertyName("severity")] string? Severity,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("sourcePage")] int? SourcePage,
    [property: JsonPropertyName("sourceSpan")] string? SourceSpan,
    [property: JsonPropertyName("confidence")] double? Confidence);
