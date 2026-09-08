namespace Contigo.AiEval;

/// <summary>
/// Placeholder for task E13/F01/US01/T01 (v2-scaffold). <c>Contigo.AiEval</c> is the future
/// golden-set evaluation harness for the Ask engine (&gt;= 40 questions, 0 numeric-guard
/// interventions — gap G-GOLDEN-SET, <c>reports/audit/ask-v2-gaps.md</c>, task F06/T02); this
/// task only creates the project and its <c>Contigo.Chat</c> / <c>Contigo.AiGateway</c> /
/// <c>Contigo.SharedKernel</c> references so that later task lands in an existing project
/// instead of also touching <c>Contigo.slnx</c>. This test proves the project itself
/// compiles, its three references resolve, and it runs under <c>dotnet test Contigo.slnx</c>.
/// Replace with the real golden set when F06/T02 lands.
/// </summary>
public class PlaceholderTests
{
    [Fact]
    public void Test_assembly_is_named_after_this_project()
    {
        var assembly = typeof(PlaceholderTests).Assembly;

        Assert.Equal("Contigo.AiEval", assembly.GetName().Name);
    }
}
