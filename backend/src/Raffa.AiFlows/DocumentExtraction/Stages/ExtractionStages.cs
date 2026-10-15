namespace Raffa.AiFlows.DocumentExtraction.Stages;

/// <summary>The default stage set, in pipeline order: what composition registers, and what tests that
/// build the orchestrator by hand pass.</summary>
internal static class ExtractionStages
{
    public static IReadOnlyList<IExtractionStage> Default { get; } =
    [
        new MetadataStage(),
        new CommercialTermsStage(),
        new DatesAndRenewalTermsStage(),
        new LineItemsStage(),
        new LegalClausesStage(),
        new ObligationsStage(),
        new RiskStage(),
    ];
}
