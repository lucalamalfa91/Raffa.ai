using System.Text.Json;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Builds the JSON Schema text <see cref="StagedExtractionService"/> sends as
/// <c>AiExtractionRequest.JsonSchema</c> for each of AC-1's seven stages (product spec §7.3:
/// "schema-constrained output"; ADR-004: "a structured-output-capable model ... not free text").
/// <see cref="Raffa.AiGateway.IAiGateway"/>'s own doc comment is explicit that "the gateway
/// does not know or validate the domain schema" — this class is the caller-owned schema that
/// comment refers to.
///
/// <para>
/// Every schema is written for Azure OpenAI <b>strict</b> structured outputs (ADR-004 amendment
/// 2026-09-09): every object sets <c>additionalProperties: false</c> and lists <em>every</em>
/// property in <c>required</c>, an absent value is expressed as <c>null</c> through a
/// <c>["type", "null"]</c> union (never by omitting the property), and no numeric/string bound
/// keyword is used — the value contract (formats, ranges, meanings) travels in each property's
/// <c>description</c>, which the model reads. <c>Raffa.AiGateway.Foundry.StrictJsonSchemaValidator</c>
/// refuses anything else before a request is sent, and <c>StagedExtractionJsonSchemasTests</c>
/// proves every builder here passes it.
/// </para>
///
/// Every shape ends in the same evidence tail — <c>sourcePage</c>, <c>sourceSpan</c>,
/// <c>confidence</c> — so every stage satisfies AC-2 ("every extracted fact carries source span
/// + confidence") uniformly. Built via <see cref="JsonSerializer"/> over plain anonymous C#
/// objects rather than hand-written JSON text, so the emitted schema is guaranteed
/// well-formed JSON by construction.
/// </summary>
internal static class StagedExtractionJsonSchemas
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    private const string SourcePageDescription =
        "The integer n of the [[PAGE n]] marker that precedes the evidence, or null when no page can be pointed to.";

    private const string SourceSpanDescription =
        "A verbatim quote of at most 300 characters from that page, in the document's original language, that supports the value; null when there is no supporting passage.";

    private const string ConfidenceDescription =
        "Number from 0 to 1: your probability that the value is right. Below 0.6 when the document is ambiguous or contradicts itself; 0 next to a null value.";

    /// <summary>What each scalar fact name means, so the model resolves the document's own wording
    /// (Italian, German, English ...) to the right field. Keyed by the exact allow-list names
    /// <see cref="StagedExtractionService"/> applies; an unknown name gets a generic gloss.</summary>
    private static readonly IReadOnlyDictionary<string, string> FieldMeanings = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [StagedExtractionService.SupplierFieldName] =
            "the supplier's (vendor's, provider's, fornitore's) legal name exactly as written in the document - the party that provides the goods or services, never the customer",
        ["currency"] = "ISO 4217 code of the currency the fees are expressed in (EUR, USD, CHF, GBP)",
        ["governingLaw"] = "the governing law as a short English phrase, e.g. 'Italy', 'State of Delaware'",
        ["status"] = "the contract's status as one short English word: active, draft, expired, terminated",
        ["annualSpend"] = "the recurring annual fee or spend as a plain number (per year; a monthly fee times 12 only when the document says it is monthly)",
        ["totalContractValue"] = "the total value over the whole contract term as a plain number, only when the document states it",
        ["paymentTerms"] = "payment terms as a short English phrase, e.g. 'Net 30', '60 days end of month'",
        ["startDate"] = "the date the contract term starts, YYYY-MM-DD",
        ["endDate"] = "the date the current term ends, YYYY-MM-DD",
        ["effectiveDate"] = "the effective date of the agreement, YYYY-MM-DD",
        ["cancellationDeadline"] = "the last date a termination or non-renewal notice can be sent, YYYY-MM-DD (end date minus the notice period, only when both are stated)",
        ["autoRenewal"] = "'true' if the contract renews automatically (tacit renewal) unless a party terminates it, otherwise 'false'",
        ["renewalTermMonths"] = "the length of each renewal term in months, as an integer",
    };

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
    /// <c>StagedExtractionService.ApplyMetadataFact</c> and friends). A <c>null</c> value means
    /// "the document does not state it" and is not persisted.
    ///
    /// <para>
    /// The `metadata` stage's allow-list includes <c>supplier</c> (requirements R-SUP-01): the
    /// supplier's legal name <em>as written in the document</em>, carrying the same evidence tail
    /// as every other fact. Nothing about this schema marks it as a critical field — that is a
    /// confidence bar the caller applies to the returned fact
    /// (<c>StagedExtractionService.CriticalConfidenceThreshold</c>), not a constraint a model can
    /// be asked to honour.
    /// </para>
    /// </summary>
    public static string Facts(IReadOnlyList<string> allowedFieldNames)
    {
        var fieldDescription = "Which contract field this fact is evidence for. " + string.Join("; ", allowedFieldNames.Select(
            name => $"{name} = {(FieldMeanings.TryGetValue(name, out var meaning) ? meaning : "the contract's " + name)}"));

        var schema = new
        {
            type = "object",
            properties = new
            {
                facts = new
                {
                    type = "array",
                    description = "One fact per field the document states; at most one fact per field name. Leave out fields the document does not mention, or return them with value null.",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            field = new { type = "string", @enum = allowedFieldNames, description = fieldDescription },
                            value = new
                            {
                                type = new[] { "string", "null" },
                                description = "The normalised value as a string: dates YYYY-MM-DD; amounts as digits with '.' as decimal separator, no thousands separators, symbols or units; currencies as ISO 4217 codes; booleans 'true'/'false'; month counts as integers; null when the document does not state it.",
                            },
                            sourcePage = new { type = new[] { "integer", "null" }, description = SourcePageDescription },
                            sourceSpan = new { type = new[] { "string", "null" }, description = SourceSpanDescription },
                            confidence = new { type = "number", description = ConfidenceDescription },
                        },
                        required = new[] { "field", "value", "sourcePage", "sourceSpan", "confidence" },
                        additionalProperties = false,
                    },
                },
            },
            required = new[] { "facts" },
            additionalProperties = false,
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
                    description = "Every priced product, service or licence line the document states (a master agreement without prices has none). At most 40 items, the most material first.",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            sku = new { type = new[] { "string", "null" }, description = "Product code / SKU / article number as written, or null." },
                            description = new { type = "string", description = "The line item as named in the document (original language)." },
                            quantity = new { type = new[] { "number", "null" }, description = "Quantity as a JSON number, or null." },
                            unit = new { type = new[] { "string", "null" }, description = "Unit of measure as written (seats, users, hours, licences ...), or null." },
                            unitPrice = new { type = new[] { "number", "null" }, description = "Price per unit as a JSON number, no currency symbol, or null." },
                            listPrice = new { type = new[] { "number", "null" }, description = "List (undiscounted) price per unit as a JSON number, or null." },
                            discount = new { type = new[] { "number", "null" }, description = "Discount as stated in the document, as a JSON number (a percentage as its number, e.g. 15 for 15%), or null." },
                            billingPeriod = new { type = new[] { "string", "null" }, description = "Billing period as a short English word: monthly, quarterly, annual, one-time; or null." },
                            annualCost = new { type = new[] { "number", "null" }, description = "Cost of this line per year as a JSON number when the document states it, or null." },
                            totalCost = new { type = new[] { "number", "null" }, description = "Total cost of this line over the term as a JSON number when the document states it, or null." },
                            sourcePage = new { type = new[] { "integer", "null" }, description = SourcePageDescription },
                            sourceSpan = new { type = new[] { "string", "null" }, description = SourceSpanDescription },
                            confidence = new { type = "number", description = ConfidenceDescription },
                        },
                        required = new[]
                        {
                            "sku", "description", "quantity", "unit", "unitPrice", "listPrice", "discount",
                            "billingPeriod", "annualCost", "totalCost", "sourcePage", "sourceSpan", "confidence",
                        },
                        additionalProperties = false,
                    },
                },
            },
            required = new[] { "items" },
            additionalProperties = false,
        };

        return JsonSerializer.Serialize(schema, Options);
    }

    /// <summary>Shape for the `clauses` stage (spec §6 "ContractClause"). <c>riskLevel</c> is a
    /// required enum with an explicit <c>Unknown</c> member (strict mode accepts no <c>null</c>
    /// inside an enum); the pipeline stores <c>Unknown</c> as "no risk level".</summary>
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
                    description = "The legally material clauses of the document (liability, indemnity, termination, renewal, confidentiality, data protection, SLA, penalties, exclusivity, IP, payment, price adjustment ...). At most 40, the most material first.",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            clauseType = new { type = "string", description = "Short English label of the clause type, e.g. 'limitation of liability', 'termination for convenience', 'auto-renewal'." },
                            rawText = new { type = "string", description = "The clause text verbatim in the original language, at most 2000 characters." },
                            normalizedValue = new { type = new[] { "string", "null" }, description = "The clause's key term normalised as a short English phrase (e.g. 'capped at 12 months of fees', '90 days notice'), or null." },
                            riskLevel = new { type = "string", @enum = new[] { "Low", "Medium", "High", "Critical", "Unknown" }, description = "Risk this clause carries for the customer; Unknown when it cannot be judged." },
                            sourcePage = new { type = new[] { "integer", "null" }, description = SourcePageDescription },
                            sourceSpan = new { type = new[] { "string", "null" }, description = SourceSpanDescription },
                            confidence = new { type = "number", description = ConfidenceDescription },
                        },
                        required = new[] { "clauseType", "rawText", "normalizedValue", "riskLevel", "sourcePage", "sourceSpan", "confidence" },
                        additionalProperties = false,
                    },
                },
            },
            required = new[] { "items" },
            additionalProperties = false,
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
                    description = "Concrete obligations the document places on a party (deliver, pay, notify, report, renew, insure, comply ...). At most 40, the most material first.",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            party = new { type = "string", description = "Who is bound: the party's name as written, or 'Supplier' / 'Customer'." },
                            obligationType = new { type = "string", description = "Short English label, e.g. 'payment', 'notice', 'delivery', 'reporting', 'insurance'." },
                            description = new { type = "string", description = "What must be done, summarised in one or two sentences (original language or English)." },
                            dueDate = new { type = new[] { "string", "null" }, description = "Calendar due date as YYYY-MM-DD when the document states one, or null." },
                            recurrenceRule = new { type = new[] { "string", "null" }, description = "How often it recurs as a short English phrase (e.g. 'monthly', 'every 12 months') or an RFC 5545 rule, or null." },
                            criticality = new { type = new[] { "string", "null" }, description = "low, medium, high or critical, or null." },
                            status = new { type = new[] { "string", "null" }, description = "pending, active, fulfilled or breached when the document states it, or null." },
                            sourcePage = new { type = new[] { "integer", "null" }, description = SourcePageDescription },
                            sourceSpan = new { type = new[] { "string", "null" }, description = SourceSpanDescription },
                            confidence = new { type = "number", description = ConfidenceDescription },
                        },
                        required = new[]
                        {
                            "party", "obligationType", "description", "dueDate", "recurrenceRule", "criticality",
                            "status", "sourcePage", "sourceSpan", "confidence",
                        },
                        additionalProperties = false,
                    },
                },
            },
            required = new[] { "items" },
            additionalProperties = false,
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
                    description = "Commercial or legal risks the document creates for the customer (uncapped liability, automatic price increases, lock-in, one-sided termination, missing SLA ...). At most 40, the most material first.",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            riskType = new { type = "string", description = "Short English label, e.g. 'uncapped liability', 'price escalation', 'auto-renewal lock-in'." },
                            severity = new { type = "string", @enum = new[] { "Low", "Medium", "High", "Critical" }, description = "Severity for the customer." },
                            description = new { type = "string", description = "Why it is a risk, in one or two sentences." },
                            status = new { type = new[] { "string", "null" }, description = "open, mitigated or accepted when the document states it, or null." },
                            sourcePage = new { type = new[] { "integer", "null" }, description = SourcePageDescription },
                            sourceSpan = new { type = new[] { "string", "null" }, description = SourceSpanDescription },
                            confidence = new { type = "number", description = ConfidenceDescription },
                        },
                        required = new[] { "riskType", "severity", "description", "status", "sourcePage", "sourceSpan", "confidence" },
                        additionalProperties = false,
                    },
                },
            },
            required = new[] { "items" },
            additionalProperties = false,
        };

        return JsonSerializer.Serialize(schema, Options);
    }
}
