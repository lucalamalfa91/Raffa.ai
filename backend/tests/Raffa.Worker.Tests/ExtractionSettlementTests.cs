using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Messaging;

namespace Raffa.Worker.Tests;

/// <summary>Task E16/F02/US02/T01 AC-6 / ADR-027 §C6 (fix 2026-09-14): the three DeliveryCount branches
/// and "row present completes", as the task's own test table required and the wave never wrote.</summary>
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
    [InlineData(2)]
    public void An_absent_row_below_three_deliveries_abandons_for_redelivery(long deliveryCount)
    {
        Assert.Equal(ExtractionSettlement.Action.Abandon, ExtractionSettlement.Decide(ExtractionHandleOutcome.JobNotFound, deliveryCount));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(8)]
    public void An_absent_row_from_three_or_more_deliveries_dead_letters_as_job_not_found(long deliveryCount)
    {
        Assert.Equal(ExtractionSettlement.Action.DeadLetter, ExtractionSettlement.Decide(ExtractionHandleOutcome.JobNotFound, deliveryCount));
        Assert.Equal("job-not-found", ExtractionSettlement.JobNotFoundReason);
    }
}
