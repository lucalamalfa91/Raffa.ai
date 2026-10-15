using Raffa.Documents.Contracts.Domain;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// The typed output of one extraction stage: what <see cref="IExtractionRunStore.ApplyAsync"/> accepts.
/// A <see cref="System.Text.Json.JsonException"/> is raised by whoever parses the model's payload, never
/// by the store.
/// </summary>
public abstract record StageFacts
{
    private StageFacts()
    {
    }

    public abstract ExtractionStage Stage { get; }

    /// <summary>Facts of one of the three scalar stages (Metadata, CommercialTerms, DatesAndRenewalTerms).</summary>
    public sealed record Scalars : StageFacts
    {
        public Scalars(ExtractionStage stage, IReadOnlyList<ExtractedFact> facts)
        {
            if (stage is not (ExtractionStage.Metadata or ExtractionStage.CommercialTerms or ExtractionStage.DatesAndRenewalTerms))
            {
                throw new ArgumentOutOfRangeException(nameof(stage), stage, "Not a scalar-facts stage.");
            }

            Stage = stage;
            Facts = facts;
        }

        public override ExtractionStage Stage { get; }

        public IReadOnlyList<ExtractedFact> Facts { get; }
    }

    public sealed record LineItems(IReadOnlyList<ExtractedLineItemFact> Items) : StageFacts
    {
        public override ExtractionStage Stage => ExtractionStage.LineItems;
    }

    public sealed record Clauses(IReadOnlyList<ExtractedClauseFact> Items) : StageFacts
    {
        public override ExtractionStage Stage => ExtractionStage.LegalClauses;
    }

    public sealed record Obligations(IReadOnlyList<ExtractedObligationFact> Items) : StageFacts
    {
        public override ExtractionStage Stage => ExtractionStage.Obligations;
    }

    public sealed record Risks(IReadOnlyList<ExtractedRiskFact> Items) : StageFacts
    {
        public override ExtractionStage Stage => ExtractionStage.Risk;
    }
}
