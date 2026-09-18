using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.Documents.Contracts.Tests;

public sealed class HungProcessingDetectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Processing_with_a_fresh_claim_is_not_hung()
    {
        var jobs = new[] { new ExtractionJobProgress(Now.AddMinutes(-1), Now.AddMinutes(-1), null) };

        Assert.False(HungProcessingDetector.IsHung(DocumentProcessingStatus.Processing, Now, jobs));
    }

    [Fact]
    public void Processing_with_no_heartbeat_for_three_minutes_is_not_hung()
    {
        var jobs = new[] { new ExtractionJobProgress(Now.AddMinutes(-3), Now.AddMinutes(-3), null) };

        Assert.Equal(TimeSpan.FromMinutes(15), HungProcessingDetector.InactivityWindow);
        Assert.False(HungProcessingDetector.IsHung(DocumentProcessingStatus.Processing, Now, jobs));
    }

    [Fact]
    public void Processing_with_a_heartbeat_during_a_long_extract_is_not_hung()
    {
        // Live ExtractAsync > 3 min: started_at moved by a durable pulse (Foundry retry / 30s watch).
        var jobs = new[] { new ExtractionJobProgress(Now.AddMinutes(-10), Now.AddMinutes(-1), null) };

        Assert.False(HungProcessingDetector.IsHung(DocumentProcessingStatus.Processing, Now, jobs));
    }

    [Fact]
    public void Processing_with_no_heartbeat_for_the_inactivity_window_is_hung()
    {
        var jobs = new[] { new ExtractionJobProgress(Now.AddMinutes(-15), Now.AddMinutes(-16), null) };

        Assert.True(HungProcessingDetector.IsHung(DocumentProcessingStatus.Processing, Now, jobs));
    }

    [Fact]
    public void A_later_stage_start_resets_the_hang_window()
    {
        var jobs = new[]
        {
            new ExtractionJobProgress(Now.AddMinutes(-10), Now.AddMinutes(-10), Now.AddMinutes(-8)),
            new ExtractionJobProgress(null, Now.AddMinutes(-1), null),
        };

        Assert.False(HungProcessingDetector.IsHung(DocumentProcessingStatus.Processing, Now, jobs));
        Assert.Equal(Now.AddMinutes(-1), HungProcessingDetector.LastProgressAt(jobs));
    }

    [Fact]
    public void Uploaded_and_terminal_statuses_are_never_hung()
    {
        var stale = new[] { new ExtractionJobProgress(Now.AddHours(-1), Now.AddHours(-1), null) };

        Assert.False(HungProcessingDetector.IsHung(DocumentProcessingStatus.Uploaded, Now, stale));
        Assert.False(HungProcessingDetector.IsHung(DocumentProcessingStatus.Completed, Now, stale));
        Assert.False(HungProcessingDetector.IsHung(DocumentProcessingStatus.Failed, Now, stale));
        Assert.False(HungProcessingDetector.IsHung(DocumentProcessingStatus.NeedsReview, Now, stale));
        Assert.False(HungProcessingDetector.IsHung(DocumentProcessingStatus.Rejected, Now, stale));
    }

    [Fact]
    public void Processing_with_no_job_timestamps_is_not_treated_as_hung()
    {
        Assert.False(HungProcessingDetector.IsHung(DocumentProcessingStatus.Processing, Now, []));
    }
}
