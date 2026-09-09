namespace Contigo.Documents.Contracts.Domain;

/// <summary>
/// Contract hierarchy document kinds (product spec §6.1: Supplier └── Contract Family ├── MSA
/// ├── Order Form ├── Amendment ├── SOW └── Renewal Letter). Shared by <see cref="Contract"/>
/// (what kind of contract this is) and <see cref="Document"/> (what kind of file was uploaded) —
/// in practice one uploaded file usually *is* one of these kinds.
///
/// <para>
/// Task E13/F04/US01/T01 (documents-admission, ADR-024 / <c>inputs/requirements.md</c> R-DOC-03)
/// widened the enum with the five "documents around a contract" the admission gate admits —
/// <see cref="Quote"/>, <see cref="Invoice"/>, <see cref="PriceList"/>, <see cref="Nda"/>,
/// <see cref="Dpa"/> — so they are stored as their own kind instead of being folded into
/// <see cref="Other"/>. The existing members keep their names (they are persisted as strings,
/// <c>DocumentConfiguration</c>/<c>ContractConfiguration</c> <c>HasConversion&lt;string&gt;()</c>, and
/// exposed verbatim as the OpenAPI <c>documentType</c> enum). <see cref="Other"/> is still a
/// legal value for a <see cref="Contract"/> row created before classification ran, but the gate
/// never <em>admits</em> an <see cref="Other"/> upload, so no document row is written with it any
/// more (R-DOC-03 "Other is never stored").
/// </para>
/// </summary>
public enum ContractDocumentType
{
    Msa,
    OrderForm,
    Amendment,
    Sow,
    RenewalLetter,
    Quote,
    Invoice,
    PriceList,
    Nda,
    Dpa,
    Other,
}
