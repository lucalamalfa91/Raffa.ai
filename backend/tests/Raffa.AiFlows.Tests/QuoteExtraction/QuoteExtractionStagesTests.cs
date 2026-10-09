using Raffa.AiFlows.QuoteExtraction.Orchestration;

namespace Raffa.AiFlows.Tests.QuoteExtraction;

public sealed class QuoteExtractionStagesTests
{
    /// <summary>The fixture gateway and the integration scripts key on this literal (the `extract`
    /// role identifies a call by its caller-owned stage name), so it is a contract.</summary>
    [Fact]
    public void The_line_items_stage_name_is_the_contract_literal()
    {
        Assert.Equal("QuoteLineItems", QuoteExtractionStages.StageName);
    }
}
