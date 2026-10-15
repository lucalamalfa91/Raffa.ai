using Raffa.Documents.Contracts.Application.Extraction;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// F5 PR-1: production job rows carry <c>ExtractionJob.InputHash</c> computed as
/// <c>SHA256("staged-v1\n" + pageMarkedText)</c>. If the algorithm, the page marker format or the
/// pipeline version drifts, no stored checkpoint would ever resume again. The pinned hex was computed
/// once, outside the code (<c>printf 'staged-v1\n[[PAGE 1]]\nabc\n\n' | sha256sum</c>).
/// </summary>
public sealed class ExtractionInputHashTests
{
    private const string PinnedHash = "C0C0318D64180F4857661DB0864D1EE721991D24E243A36FC3888536018337BD";

    [Fact]
    public void The_pipeline_version_is_a_contract()
    {
        Assert.Equal("staged-v1", StagedExtractionService.PipelineVersion);
    }

    [Fact]
    public void The_page_marked_text_format_is_a_contract()
    {
        Assert.Equal(
            "[[PAGE 1]]\nabc\n\n",
            StagedExtractionService.BuildPageMarkedText([new DocumentPageText(1, "abc")]));
    }

    [Fact]
    public void The_input_hash_of_a_known_text_equals_the_pinned_value()
    {
        Assert.Equal(PinnedHash, StagedExtractionService.ComputeInputHash("[[PAGE 1]]\nabc\n\n"));
        Assert.Equal(
            PinnedHash,
            StagedExtractionService.ComputeInputHash(
                StagedExtractionService.BuildPageMarkedText([new DocumentPageText(1, "abc")])));
    }
}
