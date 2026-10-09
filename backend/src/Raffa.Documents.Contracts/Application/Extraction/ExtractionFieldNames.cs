namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Names of the extracted facts that code outside the per-field allow-lists addresses by literal.
/// These are the values stored in <c>ExtractionEvidence.FieldName</c> (and accepted by
/// <c>ContractCorrectionService</c>), so they are part of the persisted contract: never rename one.
/// </summary>
public static class ExtractionFieldNames
{
    /// <summary>Field name of the `supplier` fact (requirements R-SUP-01): the supplier's legal
    /// name exactly as written in the document. Public because the processing pipeline and the
    /// review surfaces address the resulting <c>ExtractionEvidence.FieldName</c> by this literal,
    /// and <c>ContractCorrectionService</c> accepts a human correction under the same name.</summary>
    public const string Supplier = "supplier";

    /// <summary>Field name of the classification's own evidence row (<c>Contract.Type</c>).
    /// Classification is the one extracted fact the staged pipeline does not produce itself — the
    /// admission gate / document processing pipeline run the `classify` role — yet the review
    /// screen shows "Contract type" next to every other field and needs the same real confidence
    /// behind it. Keyed exactly as <c>ContractCorrectionService</c> accepts a <c>type</c>
    /// correction.</summary>
    public const string Type = "type";

    /// <summary>Field name of the last date a termination or non-renewal notice can be sent.</summary>
    public const string CancellationDeadline = "cancellationDeadline";

    /// <summary>Field name of the notice period, in calendar days before the end date.</summary>
    public const string NoticePeriodDays = "noticePeriodDays";
}
