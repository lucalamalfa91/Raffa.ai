using System.Text.Json;

namespace Contigo.Documents.Contracts.Application.Extraction;

/// <summary>
/// Builds the JSON Schema text <see cref="StagedExtractionService"/> sends as
/// <c>AiExtractionRequest.JsonSchema</c> for each of AC-1's seven stages (product spec §7.3:
/// "schema-constrained output"; ADR-004: "a structured-output-capable model ... not free text").
/// <see cref="Contigo.AiGateway.IAiGateway"/>'s own doc comment is explicit that "the gateway
/// does not know or validate the domain schema" — this class is the caller-owned schema that
/// comment refers to.
///
/// Every shape ends in the same evidence tail — <c>sourcePage</c>, <c>sourceSpan</c>,
/// <c>confidence</c> — so every stage satisfies AC-2 ("every extracted fact carries source span
/// + confidence") uniformly. Built via <see cref="JsonSerializer"/> over plain anonymous C#
/// objects rather than hand-written JSON text, so the emitted schema is guaranteed
/// well-formed JSON by construction (no manual brace-matching to get wrong).
/// </summary>
internal static class StagedExtractionJsonSchemas
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>
    /// Shape for a scalar-field stage (Metadata, CommercialTerms, DatesAndRenewalTerms): an
    /// array of <c>{field, value, sourcePage, sourceSpan, confidence}</c> facts, each naming
    /// which <see cref="Domain.Contract"/> property it is evidence for.
    /// <paramref name="allowedFieldNames"/> is the exact allow-list
    /// <see cref="StagedExtractionService"/> knows how to apply for that stage — expressed as a
    /// JSON Schema <c>enum</c> so a structured-output model is constrained to only ever propose
    /// a field name the pipeline can actually persist. <c>value</c> is deliberately always a
    /// string (or null), even for numeric/boolean/date facts: the target CLR type depends on
    /// which <paramref name="allowedFieldNames"/> entry <c>field</c> is, which a JSON Schema
    /// union type cannot express per-enum-value, so the caller (not the schema) is responsible
    /// for parsing <c>value</c> against the field's real type (see
    /// <c>StagedExtractionService.ApplyMetadataFact</c> and friends).
    ///
    /// <para>
    /// The `metadata` stage's allow-list includes <c>supplier</c> (requirements R-SUP-01): the
    /// supplier's legal name <em>as written in the document</em>, carrying the same evidence tail
    /// as every other fact. Nothing about this schema marks it as a critical field — that is a
    /// confidence bar the caller applies to the returned fact
    /// (<c>StagedExtractionService.CriticalConfidenceThreshold</c>), not a constraint a model can
    /// be asked to honour. The same reasoning as <c>value</c>'s always-string type above: a JSON
    /// Schema constrains the <em>shape</em> of what a model may propose; what is trustworthy enough
    /// to persist and link is a domain decision.
    /// </para>
    /// </summary>
    public static string Facts(IReadOnlyList<string> allowedFieldNames)
    {
        var schema = new
        {
            type = "object",
            properties = new
            {
                facts = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            field = new { type = "string", @enum = allowedFieldNames },
                            value = new { type = new[] { "string", "null" } },
                            sourcePage = new { type = new[] { "integer", "null" } },
                            sourceSpan = new { type = new[] { "string", "null" } },
                            confidence = new { type = "number", minimum = 0, maximum = 1 },
                        },
                        required = new[] { "field", "value", "confidence" },
                    },
                },
            },
            required = new[] { "facts" },
        };

        return JsonSerializer.Serialize(schema, Options);
    }

    /// <summary>Shape for the `price/SKU` stage (spec §6 "ContractLineItem").</summary>
    public static string LineItems()
    {
        var schema = new
        {
            type = "object",
            properties = new
            {
                items = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            sku = new { type = new[] { "string", "null" } },
                            description = new { type = "string" },
                            quantity = new { type = new[] { "number", "null" } },
                            unit = new { type = new[] { "string", "null" } },
                            unitPrice = new { type = new[] { "number", "null" } },
                            listPrice = new { type = new[] { "number", "null" } },
                            discount = new { type = new[] { "number", "null" } },
                            billingPeriod = new { type = new[] { "string", "null" } },
                            annualCost = new { type = new[] { "number", "null" } },
                            totalCost = new { type = new[] { "number", "null" } },
                            sourcePage = new { type = new[] { "integer", "null" } },
                            sourceSpan = new { type = new[] { "string", "null" } },
                            confidence = new { type = "number", minimum = 0, maximum = 1 },
                        },
                        required = new[] { "description", "confidence" },
                    },
                },
            },
            required = new[] { "items" },
        };

        return JsonSerializer.Serialize(schema, Options);
    }

    /// <summary>Shape for the `clauses` stage (spec §6 "ContractClause").</summary>
    public static string Clauses()
    {
        var schema = new
        {
            type = "object",
            properties = new
            {
                items = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            clauseType = new { type = "string" },
                            rawText = new { type = "string" },
                            normalizedValue = new { type = new[] { "string", "null" } },
                            riskLevel = new { type = new[] { "string", "null" }, @enum = new[] { "Low", "Medium", "High", "Critical", null } },
                            sourcePage = new { type = new[] { "integer", "null" } },
                            sourceSpan = new { type = new[] { "string", "null" } },
                            confidence = new { type = "number", minimum = 0, maximum = 1 },
                        },
                        required = new[] { "clauseType", "rawText", "confidence" },
                    },
                },
            },
            required = new[] { "items" },
        };

        return JsonSerializer.Serialize(schema, Options);
    }

    /// <summary>Shape for the `obligations` stage (spec §6 "Obligation").</summary>
    public static string Obligations()
    {
        var schema = new
        {
            type = "object",
            properties = new
            {
                items = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            party = new { type = "string" },
                            obligationType = new { type = "string" },
                            description = new { type = "string" },
                            dueDate = new { type = new[] { "string", "null" }, format = "date" },
                            recurrenceRule = new { type = new[] { "string", "null" } },
                            criticality = new { type = new[] { "string", "null" } },
                            status = new { type = new[] { "string", "null" } },
                            sourcePage = new { type = new[] { "integer", "null" } },
                            sourceSpan = new { type = new[] { "string", "null" } },
                            confidence = new { type = "number", minimum = 0, maximum = 1 },
                        },
                        required = new[] { "party", "obligationType", "description", "confidence" },
                    },
                },
            },
            required = new[] { "items" },
        };

        return JsonSerializer.Serialize(schema, Options);
    }

    /// <summary>Shape for the `risk` stage (spec §6, Appendix C rules 2/10).</summary>
    public static string Risks()
    {
        var schema = new
        {
            type = "object",
            properties = new
            {
                items = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            riskType = new { type = "string" },
                            severity = new { type = "string", @enum = new[] { "Low", "Medium", "High", "Critical" } },
                            description = new { type = "string" },
                            status = new { type = new[] { "string", "null" } },
                            sourcePage = new { type = new[] { "integer", "null" } },
                            sourceSpan = new { type = new[] { "string", "null" } },
                            confidence = new { type = "number", minimum = 0, maximum = 1 },
                        },
                        required = new[] { "riskType", "severity", "description", "confidence" },
                    },
                },
            },
            required = new[] { "items" },
        };

        return JsonSerializer.Serialize(schema, Options);
    }
}
