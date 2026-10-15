using System.Text.Json.Serialization;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Raw structured-output shapes the AiFlows extraction stages (<c>Raffa.AiFlows.DocumentExtraction.Stages</c>)
/// deserialize <c>AiExtractionResult.PayloadJson</c> into, mirroring
/// <c>StagedExtractionJsonSchemas</c>'s shapes exactly. Every property is nullable (even
/// ones the JSON Schema marks <c>required</c>): a structured-output model is not guaranteed to
/// honour the schema perfectly, and <see cref="Raffa.AiGateway.Fixtures.FixtureAiGateway"/>
/// today always returns an empty <c>{}</c> object (no live model behind it yet) — deserializing
/// that into these types must produce nulls, not throw. <c>ExtractionRunStore</c> is
/// responsible for validating required fields are actually present before persisting, and for
/// skipping (not crashing on) an item that is missing one.
/// </summary>
public sealed record ExtractedFactsPayload(
    [property: JsonPropertyName("facts")] IReadOnlyList<ExtractedFact>? Facts);

public sealed record ExtractedFact(
    [property: JsonPropertyName("field")] string? Field,
    [property: JsonPropertyName("value")] string? Value,
    [property: JsonPropertyName("sourcePage")] int? SourcePage,
    [property: JsonPropertyName("sourceSpan")] string? SourceSpan,
    [property: JsonPropertyName("confidence")] double? Confidence)
{
    /// <summary>F5-T07: set by the span verifier in Gate mode only (false = the evidence span was not found
    /// on the page the model named; true = found; null = not verified: Off, Shadow or no span). Never part
    /// of the wire contract and never persisted as such: the store turns false into review-required.</summary>
    [JsonIgnore]
    public bool? SpanVerified { get; init; }

    /// <summary>F5-D19: set by the cross-stage validators in Gate mode only (the named rule that routed this
    /// field to review, e.g. <c>cross-stage:date-order.end-before-start</c>); null otherwise. Never part of the
    /// wire contract. The store turns a non-null reason into review-required; the value is never changed.</summary>
    [JsonIgnore]
    public string? ReviewReason { get; init; }
}


public sealed record ExtractedLineItemsPayload(
    [property: JsonPropertyName("items")] IReadOnlyList<ExtractedLineItemFact>? Items);

public sealed record ExtractedLineItemFact(
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
    [property: JsonPropertyName("confidence")] double? Confidence)
{
    /// <summary>F5-T07: set by the span verifier in Gate mode only (false = the evidence span was not found
    /// on the page the model named; true = found; null = not verified: Off, Shadow or no span). Never part
    /// of the wire contract and never persisted as such: the store turns false into review-required.</summary>
    [JsonIgnore]
    public bool? SpanVerified { get; init; }
}


public sealed record ExtractedClausesPayload(
    [property: JsonPropertyName("items")] IReadOnlyList<ExtractedClauseFact>? Items);

public sealed record ExtractedClauseFact(
    [property: JsonPropertyName("clauseType")] string? ClauseType,
    [property: JsonPropertyName("rawText")] string? RawText,
    [property: JsonPropertyName("normalizedValue")] string? NormalizedValue,
    [property: JsonPropertyName("riskLevel")] string? RiskLevel,
    [property: JsonPropertyName("sourcePage")] int? SourcePage,
    [property: JsonPropertyName("sourceSpan")] string? SourceSpan,
    [property: JsonPropertyName("confidence")] double? Confidence)
{
    /// <summary>F5-T07: set by the span verifier in Gate mode only (false = the evidence span was not found
    /// on the page the model named; true = found; null = not verified: Off, Shadow or no span). Never part
    /// of the wire contract and never persisted as such: the store turns false into review-required.</summary>
    [JsonIgnore]
    public bool? SpanVerified { get; init; }
}


public sealed record ExtractedObligationsPayload(
    [property: JsonPropertyName("items")] IReadOnlyList<ExtractedObligationFact>? Items);

public sealed record ExtractedObligationFact(
    [property: JsonPropertyName("party")] string? Party,
    [property: JsonPropertyName("obligationType")] string? ObligationType,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("dueDate")] string? DueDate,
    [property: JsonPropertyName("recurrenceRule")] string? RecurrenceRule,
    [property: JsonPropertyName("criticality")] string? Criticality,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("sourcePage")] int? SourcePage,
    [property: JsonPropertyName("sourceSpan")] string? SourceSpan,
    [property: JsonPropertyName("confidence")] double? Confidence)
{
    /// <summary>F5-T07: set by the span verifier in Gate mode only (false = the evidence span was not found
    /// on the page the model named; true = found; null = not verified: Off, Shadow or no span). Never part
    /// of the wire contract and never persisted as such: the store turns false into review-required.</summary>
    [JsonIgnore]
    public bool? SpanVerified { get; init; }
}


public sealed record ExtractedRisksPayload(
    [property: JsonPropertyName("items")] IReadOnlyList<ExtractedRiskFact>? Items);

public sealed record ExtractedRiskFact(
    [property: JsonPropertyName("riskType")] string? RiskType,
    [property: JsonPropertyName("severity")] string? Severity,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("sourcePage")] int? SourcePage,
    [property: JsonPropertyName("sourceSpan")] string? SourceSpan,
    [property: JsonPropertyName("confidence")] double? Confidence)
{
    /// <summary>F5-T07: set by the span verifier in Gate mode only (false = the evidence span was not found
    /// on the page the model named; true = found; null = not verified: Off, Shadow or no span). Never part
    /// of the wire contract and never persisted as such: the store turns false into review-required.</summary>
    [JsonIgnore]
    public bool? SpanVerified { get; init; }
}

