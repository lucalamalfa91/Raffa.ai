using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.AiFlows.DocumentExtraction.Stages;

/// <summary>Annual spend, total contract value, payment terms.</summary>
internal sealed class CommercialTermsStage()
    : ScalarFactsStage(ExtractionStage.CommercialTerms, ExtractionStageFields.CommercialTerms);
