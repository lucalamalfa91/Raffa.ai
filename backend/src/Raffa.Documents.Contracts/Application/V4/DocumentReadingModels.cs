using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel.Contracts;

namespace Raffa.Documents.Contracts.Application.V4;

/// <summary>The states of a document's reading as Revisione shows them.</summary>
public static class ReadingStatus
{
    public const string Processing = "processing";
    public const string Review = "review";
    public const string Done = "done";
    public const string Failed = "failed";
}

/// <summary>A field decision in Revisione.</summary>
public static class FieldDecisions
{
    public const string Accept = "accept";
    public const string Correct = "correct";
}

/// <summary>The quoted passage, split around the highlighted words.</summary>
public sealed record ReadingQuote(string Before, string Mark, string After);

/// <summary>
/// One field Raffa is unsure of. <see cref="Label"/> and <see cref="Why"/> are a
/// <see cref="ContractMessage"/> when Raffa wrote them, or the document's own text (a string).
/// </summary>
public sealed record ReadingField(
    string Key,
    object Label,
    string Value,
    int? Page,
    string Src,
    object Why,
    ContractMessage? FlagReason,
    ReadingQuote? Quote,
    bool Critical,
    string? Decision,
    string? CorrectedValue,
    ReadingBox? Box = null,
    ReadingBasis? Basis = null);

/// <summary>Pixel-space rectangle of the quoted words on the rendered page image (the evidence box).</summary>
public sealed record ReadingBox(double X, double Y, double Width, double Height);

/// <summary>How Raffa arrived at a field's value (<see cref="ReadingBasis.Kind"/>).</summary>
public static class BasisKinds
{
    /// <summary>Written in the contract: the highlighted words are the value.</summary>
    public const string Stated = "stated";

    /// <summary>Not written as such: worked out from other passages (e.g. end of term minus notice period).</summary>
    public const string Derived = "derived";

    /// <summary>No passage of the contract states it: inferred, so the user must confirm it.</summary>
    public const string NotStated = "notStated";
}

/// <summary>Another passage the value rests on (e.g. the end of the term behind a notice deadline).</summary>
public sealed record ReadingSource(string Key, object Label, string Value, int? Page, ReadingQuote? Quote, ReadingBox? Box);

/// <summary>Raffa's reasoning behind a field: what kind of reading it is, in words, and the passages it rests on.</summary>
public sealed record ReadingBasis(string Kind, ContractMessage Explanation, IReadOnlyList<ReadingSource> Sources);

/// <summary><c>GET /api/documents/{id}/reading</c>: where the reading of one document is.</summary>
public sealed record DocumentReadingView(
    Guid DocumentId,
    Guid? ContractId,
    string File,
    string? Supplier,
    string Status,
    int Stage,
    ContractMessage StageLabel,
    ContractMessage? Failure,
    IReadOnlyList<ReadingField> Fields);

/// <summary>The Contratti banner: a document being read or waiting for review.</summary>
/// <param name="Reading">Documents still being read (uploaded or processing) — a bulk upload shows them as one line.</param>
/// <param name="ToReview">Documents waiting for the user's check of their doubtful fields.</param>
public sealed record DocumentAttention(Guid DocumentId, string File, string Status, int Flagged, int Reading = 0, int ToReview = 0);

/// <summary>The five reading stages (prototype <c>upload()</c>).</summary>
public static class ReadingStages
{
    public static readonly IReadOnlyList<string> Labels =
    [
        "Uploading",
        "Classification",
        "Reading the text",
        "Sections and tables",
        "Extracting the facts",
    ];

    /// <summary>The 1-based stage for a pipeline stage label (<see cref="DocumentProcessingStageMap"/>).</summary>
    public static int FromPipeline(string pipelineStage) => pipelineStage switch
    {
        DocumentProcessingStageMap.Uploading => 1,
        DocumentProcessingStageMap.Classifying => 2,
        DocumentProcessingStageMap.OcrText => 3,
        DocumentProcessingStageMap.SectionsAndTables => 4,
        _ => 5,
    };

    public static ContractMessage Label(int stage) =>
        ContractMessage.Of(Labels[Math.Clamp(stage, 1, Labels.Count) - 1]);
}

/// <summary>English labels of the pipeline's field names (message ids).</summary>
public static class ReadingFieldLabels
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["annualSpend"] = "Annual spend",
        ["totalContractValue"] = "Total contract value",
        ["cancellationDeadline"] = "Notice deadline",
        ["endDate"] = "Contract end",
        ["startDate"] = "Start date",
        ["effectiveDate"] = "Start date",
        ["renewalTermMonths"] = "Renewal term",
        ["noticePeriodDays"] = "Notice period",
        ["autoRenewal"] = "Auto-renewal",
        ["paymentTerms"] = "Payment terms⟦field⟧",
        ["governingLaw"] = "Governing law",
        ["currency"] = "Currency",
        ["supplier"] = "Supplier",
        ["status"] = "Status",
    };

    public static object For(string fieldName) =>
        Labels.TryGetValue(fieldName, out var label) ? ContractMessage.Of(label) : fieldName;
}

/// <summary>Why a field is flagged (BACKEND-SPEC: review fields carry <c>flagReason</c>).</summary>
public static class ExtractionFlagReason
{
    public static bool IsCritical(string fieldName) =>
        ExtractionConfidencePolicy.CriticalFieldNames.Contains(fieldName, StringComparer.OrdinalIgnoreCase);

    public static ContractMessage For(string fieldName, double? confidence)
    {
        var threshold = (int)Math.Round(ExtractionConfidencePolicy.AutoAcceptThreshold * 100);
        if (confidence is not { } value)
        {
            return IsCritical(fieldName)
                ? ContractMessage.Of("Critical field read without a confidence score: it drives dates or spend, so confirm it against the quoted passage.")
                : ContractMessage.Of("Read without a confidence score: confirm it against the quoted passage.");
        }

        var pct = (int)Math.Round(value * 100);
        return IsCritical(fieldName)
            ? ContractMessage.Of(
                "Critical field read with {pct}% confidence, below the {threshold}% needed to accept it on its own: it drives dates or spend, so confirm it against the quoted passage.",
                ("pct", pct), ("threshold", threshold))
            : ContractMessage.Of(
                "Read with {pct}% confidence, below the {threshold}% needed to accept it on its own: confirm it against the quoted passage.",
                ("pct", pct), ("threshold", threshold));
    }

    /// <summary>The highlighted words inside the passage, or the span alone.</summary>
    public static ReadingQuote? Quote(string? passage, int? start, int? length, string? span)
    {
        if (!string.IsNullOrEmpty(passage) && start is { } s && length is { } l && s >= 0 && l > 0 && s + l <= passage.Length)
        {
            return new ReadingQuote(passage[..s], passage.Substring(s, l), passage[(s + l)..]);
        }

        if (!string.IsNullOrWhiteSpace(span))
        {
            return new ReadingQuote("", span, "");
        }

        return string.IsNullOrWhiteSpace(passage) ? null : new ReadingQuote("", passage, "");
    }

    public static ContractMessage FailureFor(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.RejectionReason switch
        {
            Admission.AdmissionRejectionReason.NoReadableText =>
                ContractMessage.Of("This file has no readable text (a scan without text?). Upload another version."),
            Admission.AdmissionRejectionReason.NotAContract =>
                ContractMessage.Of("This file does not look like a contract. Upload another file."),
            _ => ContractMessage.Of("The reading did not finish. Upload another version or try again."),
        };
    }
}
