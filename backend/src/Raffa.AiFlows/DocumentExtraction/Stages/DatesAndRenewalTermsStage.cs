using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.AiFlows.DocumentExtraction.Stages;

/// <summary>Start/end/effective dates, cancellation deadline, auto-renewal, renewal term, notice period.</summary>
internal sealed class DatesAndRenewalTermsStage()
    : ScalarFactsStage(ExtractionStage.DatesAndRenewalTerms, ExtractionStageFields.DatesAndRenewalTerms);
