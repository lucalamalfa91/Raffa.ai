using Contigo.AiGateway.Contracts;
using Contigo.Documents.Contracts.Application.Extraction;
using Contigo.Documents.Contracts.Domain;

namespace Contigo.Documents.Contracts.Application.Admission;

/// <summary>How <see cref="DocumentAdmissionGate.EvaluateAsync"/> ended.</summary>
public enum AdmissionOutcome
{
    /// <summary>A contract-related document with enough readable text: persist and process it.</summary>
    Admitted,

    /// <summary>Not a contract, or no readable text: HTTP 422, nothing persisted, one audit row.</summary>
    Rejected,

    /// <summary>The gate could not reach a verdict (parse/OCR or classify failed outright): the
    /// caller reports the error; nothing is persisted and nothing is audited as a rejection —
    /// "we could not read it" is not "it is not a contract".</summary>
    Failed,
}

/// <summary>The <c>reason</c> member of the HTTP 422 body (<c>inputs/requirements.md</c> §6, ADR-024).</summary>
public enum AdmissionRejectionReason
{
    /// <summary>Classified as <see cref="ContractDocumentType.Other"/>, or a contract kind reported below
    /// <see cref="DocumentAdmissionOptions.AdmissionThreshold"/> (R-DOC-03 AC-1).</summary>
    NotAContract,

    /// <summary>Parse/OCR yielded fewer than <see cref="DocumentAdmissionOptions.MinReadableChars"/>
    /// non-whitespace characters; the classify role never ran (R-DOC-03 AC-2).</summary>
    NoReadableText,
}

public static class AdmissionRejectionReasonExtensions
{
    /// <summary>The wire literal (OpenAPI <c>uploadDocument</c> 422 <c>reason</c> enum).</summary>
    public static string ToApiValue(this AdmissionRejectionReason reason) => reason switch
    {
        AdmissionRejectionReason.NotAContract => "not_a_contract",
        AdmissionRejectionReason.NoReadableText => "no_readable_text",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown admission rejection reason."),
    };
}

/// <summary>
/// The classify-role verdict the gate already obtained, carried into
/// <see cref="DocumentProcessingPipeline.ProcessAsync(Contigo.SharedKernel.TenantId, Contigo.SharedKernel.EntityId, IReadOnlyList{DocumentPageText}, DocumentClassification, CancellationToken)"/>
/// so an admitted document is classified exactly once (task E13/F04/US01/T01: "reusing the pages
/// and classification already computed ... so the model is not called twice").
/// </summary>
public sealed record DocumentClassification(
    ContractDocumentType DocumentType,
    double Confidence,
    AiCallMetadata Metadata);

/// <summary>
/// The gate's verdict on one upload, plus everything the caller needs to either build the 422
/// body (<see cref="DetectedType"/>, <see cref="Confidence"/>, <see cref="Reason"/>,
/// <see cref="DocumentAdmissionGate.Hint"/>) or continue into persistence without re-parsing or
/// re-classifying (<see cref="Pages"/>, <see cref="Classification"/>).
/// </summary>
public sealed record AdmissionDecision
{
    public required AdmissionOutcome Outcome { get; init; }

    /// <summary>The parsed/OCR'd pages (empty when parsing itself failed).</summary>
    public IReadOnlyList<DocumentPageText> Pages { get; init; } = [];

    /// <summary>Non-whitespace characters across <see cref="Pages"/> — what
    /// <see cref="DocumentAdmissionOptions.MinReadableChars"/> is compared against.</summary>
    public int ReadableChars { get; init; }

    /// <summary>What the classify role said, mapped onto the contract taxonomy;
    /// <see cref="ContractDocumentType.Other"/> when it never ran.</summary>
    public ContractDocumentType DetectedType { get; init; } = ContractDocumentType.Other;

    /// <summary>The classify role's own confidence (0–1); 0 when it never ran (OpenAPI: "Always present,
    /// even for <c>no_readable_text</c>").</summary>
    public double Confidence { get; init; }

    /// <summary>Set only when <see cref="Outcome"/> is <see cref="AdmissionOutcome.Rejected"/>.</summary>
    public AdmissionRejectionReason? Reason { get; init; }

    /// <summary>Set only when <see cref="Outcome"/> is <see cref="AdmissionOutcome.Admitted"/>.</summary>
    public DocumentClassification? Classification { get; init; }

    /// <summary>Set only when <see cref="Outcome"/> is <see cref="AdmissionOutcome.Failed"/>.</summary>
    public string? Error { get; init; }

    public bool IsAdmitted => Outcome == AdmissionOutcome.Admitted;

    public static AdmissionDecision Admit(
        IReadOnlyList<DocumentPageText> pages, int readableChars, DocumentClassification classification) => new()
    {
        Outcome = AdmissionOutcome.Admitted,
        Pages = pages,
        ReadableChars = readableChars,
        DetectedType = classification.DocumentType,
        Confidence = classification.Confidence,
        Classification = classification,
    };

    public static AdmissionDecision RejectNoReadableText(IReadOnlyList<DocumentPageText> pages, int readableChars) => new()
    {
        Outcome = AdmissionOutcome.Rejected,
        Pages = pages,
        ReadableChars = readableChars,
        DetectedType = ContractDocumentType.Other,
        Confidence = 0,
        Reason = AdmissionRejectionReason.NoReadableText,
    };

    public static AdmissionDecision RejectNotAContract(
        IReadOnlyList<DocumentPageText> pages, int readableChars, ContractDocumentType detectedType, double confidence) => new()
    {
        Outcome = AdmissionOutcome.Rejected,
        Pages = pages,
        ReadableChars = readableChars,
        DetectedType = detectedType,
        Confidence = confidence,
        Reason = AdmissionRejectionReason.NotAContract,
    };

    public static AdmissionDecision Fail(string error) => new()
    {
        Outcome = AdmissionOutcome.Failed,
        Error = error,
    };
}
