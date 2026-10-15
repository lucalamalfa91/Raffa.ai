using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// F5 PR-1: the three scalar allow-lists moved out of <c>ExtractionOrchestrator</c>. Their order is the
/// order of the schema <c>enum</c> the model sees, so it is pinned together with the content.
/// </summary>
public sealed class ExtractionStageFieldsTests
{
    [Fact]
    public void The_metadata_list_equals_the_original_literal_in_order()
    {
        Assert.Equal(new[] { "supplier", "currency", "governingLaw", "status" }, ExtractionStageFields.Metadata);
        Assert.Equal(ExtractionFieldNames.Supplier, ExtractionStageFields.Metadata[0]);
    }

    [Fact]
    public void The_commercial_terms_list_equals_the_original_literal_in_order()
    {
        Assert.Equal(new[] { "annualSpend", "totalContractValue", "paymentTerms" }, ExtractionStageFields.CommercialTerms);
    }

    [Fact]
    public void The_dates_list_equals_the_original_literal_in_order()
    {
        Assert.Equal(
            new[] { "startDate", "endDate", "effectiveDate", "cancellationDeadline", "autoRenewal", "renewalTermMonths", "noticePeriodDays" },
            ExtractionStageFields.DatesAndRenewalTerms);
    }

    [Fact]
    public void For_resolves_the_scalar_stages_and_refuses_the_list_stages()
    {
        Assert.Same(ExtractionStageFields.Metadata, ExtractionStageFields.For(ExtractionStage.Metadata));
        Assert.Same(ExtractionStageFields.CommercialTerms, ExtractionStageFields.For(ExtractionStage.CommercialTerms));
        Assert.Same(ExtractionStageFields.DatesAndRenewalTerms, ExtractionStageFields.For(ExtractionStage.DatesAndRenewalTerms));

        foreach (var stage in new[] { ExtractionStage.LineItems, ExtractionStage.LegalClauses, ExtractionStage.Obligations, ExtractionStage.Risk })
        {
            Assert.Throws<InvalidOperationException>(() => ExtractionStageFields.For(stage));
        }
    }
}
