namespace Raffa.AiGateway.Contracts;

/// <summary>
/// The classification taxonomy the AI Gateway's `classify` role recognizes
/// (us-01-ai-gateway-classification AC-1: "MSA/Order Form/SOW/Amendment/Quote/Invoice/Price
/// List/NDA/DPA/Other"). Deliberately distinct from
/// <c>Raffa.Documents.Contracts.Domain.ContractDocumentType</c> — that enum is the narrower
/// "what CONTRACT hierarchy kind is this" (product spec §6.1: Supplier → Contract Family → MSA /
/// Order Form / Amendment / SOW / Renewal Letter); this one is the broader "what did the
/// classifier recognize", including uploads that are not contracts at all (Invoice, Price List,
/// NDA, DPA). Domain code maps between the two as needed; the AI Gateway itself stays
/// domain-agnostic (module-map: AI Gateway is provider-facing infra, consumed by both
/// Documents/Contracts and Chat).
/// </summary>
public enum AiDocumentType
{
    Msa,
    OrderForm,
    Sow,
    Amendment,
    Quote,
    Invoice,
    PriceList,
    Nda,
    Dpa,
    Other,
}
