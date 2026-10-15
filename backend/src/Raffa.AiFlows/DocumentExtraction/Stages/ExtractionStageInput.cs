using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.AiFlows.DocumentExtraction.Stages;

/// <summary>What a stage reads: the page-marked text of the whole document. <paramref name="Context"/> is
/// empty unless context dependencies are enabled (the default). <paramref name="DocumentFirst"/> (F5-T03,
/// default off) asks for the prompt-cache-friendly layout: the document at the head, the context after it.</summary>
public sealed record ExtractionStageInput(
    string DocumentText,
    int PageCount,
    IReadOnlyDictionary<ExtractionStage, StageFacts> Context,
    bool DocumentFirst = false);
