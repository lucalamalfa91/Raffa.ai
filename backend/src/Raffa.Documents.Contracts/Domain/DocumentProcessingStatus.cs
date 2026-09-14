namespace Raffa.Documents.Contracts.Domain;

/// <summary>Document lifecycle status (product spec §7.1 asynchronous ingestion pipeline).</summary>
public enum DocumentProcessingStatus
{
    Uploaded,
    Processing,
    NeedsReview,
    Completed,
    Failed,

    /// <summary>
    /// Task E16/F02/US01/T01 (async-processing-schema, ADR-027 §D6): the admission gate refused
    /// this document on content grounds (<see
    /// cref="Raffa.Documents.Contracts.Application.Admission.AdmissionRejectionReason"/>) after
    /// the format/size checks already passed in-request. Terminal — no further pipeline stage
    /// runs, the blob is deleted, and this row is <em>excluded</em> from the "all documents" count
    /// while still being listed (never counted, never askable, removable by the existing
    /// <c>DELETE</c>). Persisted as the enum name into <c>processing_status
    /// character varying(30)</c>, which has no CHECK constraint and no default
    /// (<c>documents-contracts.sql:110</c>) — so this member needed no migration of its own; only
    /// the three nullable rejection columns on <see cref="Document"/> did.
    /// </summary>
    Rejected,
}
