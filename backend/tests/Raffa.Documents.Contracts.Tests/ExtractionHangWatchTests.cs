using Raffa.Documents.Contracts.Application.Extraction;

namespace Raffa.Documents.Contracts.Tests;

public sealed class ExtractionHangWatchTests
{
    [Fact]
    public void CancelAfter_matches_the_processing_inactivity_window()
    {
        using var watch = new ExtractionHangWatch();
        using var outer = new CancellationTokenSource();
        var token = watch.Start(outer.Token);

        Assert.Equal(TimeSpan.FromMinutes(15), HungProcessingDetector.InactivityWindow);
        Assert.False(token.IsCancellationRequested);

        watch.Heartbeat();
        Assert.False(token.IsCancellationRequested);
    }

    [Fact]
    public void Heartbeat_before_start_is_a_no_op()
    {
        using var watch = new ExtractionHangWatch();
        watch.Heartbeat();
        Assert.Equal(CancellationToken.None, watch.Token);
    }
}
