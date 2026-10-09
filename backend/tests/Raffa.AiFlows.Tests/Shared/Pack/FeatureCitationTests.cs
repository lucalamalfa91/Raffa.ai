using Raffa.AiFlows.Shared.Pack;
using Raffa.Chat.Application.Capabilities;

namespace Raffa.AiFlows.Tests.Shared.Pack;

/// <summary>
/// <see cref="FeatureCitation.For"/> produces the R-SYS-03 feature-card shape from a
/// <see cref="CapabilityCatalog"/> entry.
/// </summary>
public sealed class FeatureCitationTests
{
    [Fact]
    public void FeatureCitation_for_uses_the_raffa_corpus_and_route_as_subtitle_and_href()
    {
        var capability = CapabilityCatalog.Find("savings")!;

        var citation = FeatureCitation.For(capability);

        Assert.Equal("raffa", citation.Corpus);
        Assert.Equal(capability.Title, citation.Title);
        Assert.Equal(capability.RoutePattern, citation.Subtitle);
        Assert.Equal(capability.Description, citation.Snippet);
        Assert.Equal(capability.RoutePattern, citation.Href);
    }

    [Fact]
    public void FeatureCitation_matches_R_SYS_03_AC_1_for_reviewing_weak_facts()
    {
        var capability = CapabilityCatalog.Find("documents-attention")!;

        var citation = FeatureCitation.For(capability);

        Assert.Equal("Documents › Review", citation.Title);
        Assert.Equal("/documents?filter=attention", citation.Href);
    }

    [Fact]
    public void FeatureCitation_for_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => FeatureCitation.For(null!));
    }
}
