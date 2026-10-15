using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.AiFlows.DocumentExtraction.Stages;

/// <summary>Supplier, currency, governing law, status.</summary>
internal sealed class MetadataStage() : ScalarFactsStage(ExtractionStage.Metadata, ExtractionStageFields.Metadata);
