using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Messaging;

namespace Raffa.Worker.Tests;

/// <summary>
/// Tasks E16/F02/US02/T01 and E22/F02/US01/T01:
/// <list type="bullet">
/// <item>E16 (AC-6 / ADR-027 §C6 fix 2026-09-14): the three DeliveryCount branches and "row
/// present completes".</item>
/// <item>E22 (ADR-029 clause 2): a document whose extraction failed is still viewable because
/// <see cref="Documents.Contracts.Application.Preview.DocumentPreviewService.RenderAndStoreAsync"/>
/// runs <em>before</em>
/// <see cref="Documents.Contracts.Application.Extraction.StagedExtractionService.RunAsync"/>
/// in the pipeline (<c>DocumentProcessingPipeline.cs:237-244</c> precedes
/// <c>DocumentProcessingPipeline.cs:259</c>). The settlement remains <see cref="ExtractionSettlement.Action.Complete"/>
/// for a <see cref="ExtractionHandleOutcome.Handled"/> outcome whether the extraction succeeded or
/// not — the preview was already stored before the settlement decision is reached.</item>
/// </list>
/// </summary>
public sealed class ExtractionSettlementTests
{
    [Theory]
    [InlineData(ExtractionHandleOutcome.Handled, 1)]
    [InlineData(ExtractionHandleOutcome.Handled, 7)]
    [InlineData(ExtractionHandleOutcome.ClaimLost, 1)]
    [InlineData(ExtractionHandleOutcome.ClaimLost, 3)]
    public void A_present_row_completes_whatever_the_delivery_count(ExtractionHandleOutcome outcome, long deliveryCount)
    {
        Assert.Equal(ExtractionSettlement.Action.Complete, ExtractionSettlement.Decide(outcome, deliveryCount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void An_absent_row_below_two_deliveries_abandons_for_redelivery(long deliveryCount)
    {
        Assert.Equal(ExtractionSettlement.Action.Abandon, ExtractionSettlement.Decide(ExtractionHandleOutcome.JobNotFound, deliveryCount));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    public void An_absent_row_from_two_deliveries_dead_letters_as_job_not_found(long deliveryCount)
    {
        Assert.Equal(ExtractionSettlement.Action.DeadLetter, ExtractionSettlement.Decide(ExtractionHandleOutcome.JobNotFound, deliveryCount));
        Assert.Equal("job-not-found", ExtractionSettlement.JobNotFoundReason);
    }

    // ---- Task E22/F02/US01/T01 (ADR-029 clause 2) ----

    [Fact]
    public void A_document_whose_extraction_fails_still_completes_because_rasterisation_already_ran()
    {
        // ADR-029 clause 2: rasterisation runs after admission and independently of extraction
        // success. DocumentProcessingPipeline calls DocumentPreviewService.RenderAndStoreAsync
        // at lines 237-244, *before* StagedExtractionService.RunAsync at line 259 -- so the
        // preview is always stored before the extraction result is known. A document whose
        // extraction then fails is still viewable in the viewer: the settlement is
        // ExtractionHandleOutcome.Handled → ExtractionSettlement.Action.Complete regardless.
        Assert.Equal(
            ExtractionSettlement.Action.Complete,
            ExtractionSettlement.Decide(ExtractionHandleOutcome.Handled, deliveryCount: 1));
    }
}
