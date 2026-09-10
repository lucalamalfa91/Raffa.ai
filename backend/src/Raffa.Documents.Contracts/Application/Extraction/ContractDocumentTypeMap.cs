using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Maps the AI Gateway's classify taxonomy (<see cref="AiDocumentType"/>) onto the contract
/// hierarchy one (<see cref="ContractDocumentType"/>). One place, used by both the admission gate
/// (<c>Application.Admission.DocumentAdmissionGate</c>) and <see cref="DocumentProcessingPipeline"/>'s
/// classify-in-pipeline path, so "what the model said" becomes "what we store" the same way on
/// every route. Task E13/F04/US01/T01 (documents-admission) widened the target enum so Quote /
/// Invoice / Price list / NDA / DPA are kept as their own kinds instead of collapsing into
/// <see cref="ContractDocumentType.Other"/>; only <see cref="AiDocumentType.Other"/> maps to
/// <see cref="ContractDocumentType.Other"/> now. <see cref="ContractDocumentType.RenewalLetter"/>
/// still has no classify-role counterpart (nothing in the classify taxonomy names it).
/// </summary>
public static class ContractDocumentTypeMap
{
    public static ContractDocumentType FromAi(AiDocumentType aiDocumentType) => aiDocumentType switch
    {
        AiDocumentType.Msa => ContractDocumentType.Msa,
        AiDocumentType.OrderForm => ContractDocumentType.OrderForm,
        AiDocumentType.Sow => ContractDocumentType.Sow,
        AiDocumentType.Amendment => ContractDocumentType.Amendment,
        AiDocumentType.Quote => ContractDocumentType.Quote,
        AiDocumentType.Invoice => ContractDocumentType.Invoice,
        AiDocumentType.PriceList => ContractDocumentType.PriceList,
        AiDocumentType.Nda => ContractDocumentType.Nda,
        AiDocumentType.Dpa => ContractDocumentType.Dpa,
        _ => ContractDocumentType.Other,
    };
}
