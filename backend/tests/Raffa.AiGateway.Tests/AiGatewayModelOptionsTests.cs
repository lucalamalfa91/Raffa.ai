using Raffa.AiGateway.Configuration;

namespace Raffa.AiGateway.Tests;

/// <summary>
/// Locks in ADR-004's candidate defaults (AC-1 "config-selected model IDs") so an accidental
/// change to a default model id is caught here rather than silently changing the gateway's
/// out-of-the-box behaviour.
/// </summary>
public class AiGatewayModelOptionsTests
{
    [Fact]
    public void Defaults_match_ADR_004_candidate_models()
    {
        var options = new AiGatewayModelOptions();

        Assert.Equal("gpt-4o-mini", options.Classify.ModelId);
        Assert.Equal("gpt-4o-mini", options.Extract.ModelId);
        Assert.Equal("text-embedding-3-small", options.Embed.ModelId);
        Assert.Equal("gpt-4o-mini", options.Answer.ModelId);
    }

    [Fact]
    public void Ocr_default_matches_ADR_017_candidate_model()
    {
        var options = new AiGatewayModelOptions();

        Assert.Equal("prebuilt-read", options.Ocr.ModelId);
    }

    [Fact]
    public void Section_name_is_stable_for_configuration_binding()
    {
        Assert.Equal("AiGateway:Models", AiGatewayModelOptions.SectionName);
    }
}
