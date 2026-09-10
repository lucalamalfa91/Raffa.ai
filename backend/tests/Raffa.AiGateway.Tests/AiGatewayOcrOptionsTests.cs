using Raffa.AiGateway.Configuration;

namespace Raffa.AiGateway.Tests;

/// <summary>
/// Locks in task E02/F01/US02/T02's ADR-017 page-budget default so an accidental change is caught
/// here, mirroring <see cref="AiGatewayComplianceOptionsTests"/>/<see cref="AiGatewayModelOptionsTests"/>.
/// </summary>
public class AiGatewayOcrOptionsTests
{
    [Fact]
    public void Default_max_pages_per_document_is_300()
    {
        var options = new AiGatewayOcrOptions();

        Assert.Equal(300, options.MaxPagesPerDocument);
    }

    [Fact]
    public void Section_name_is_stable_for_configuration_binding()
    {
        Assert.Equal("AiGateway:Ocr", AiGatewayOcrOptions.SectionName);
    }
}
