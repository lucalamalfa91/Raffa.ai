using Raffa.AiGateway.Configuration;

namespace Raffa.AiGateway.Tests;

/// <summary>
/// Locks in ADR-011's "no-training is not an opt-in preference" default so an accidental flip to
/// <see langword="false"/> in <see cref="AiGatewayComplianceOptions"/>'s own default is caught
/// here, mirroring <see cref="AiGatewayModelOptionsTests"/>'s coverage of
/// <see cref="AiGatewayModelOptions"/>'s defaults.
/// </summary>
public class AiGatewayComplianceOptionsTests
{
    [Fact]
    public void Default_is_no_training_true()
    {
        var options = new AiGatewayComplianceOptions();

        Assert.True(options.NoTraining);
    }

    [Fact]
    public void Section_name_is_stable_for_configuration_binding()
    {
        Assert.Equal("AiGateway:Compliance", AiGatewayComplianceOptions.SectionName);
    }
}
