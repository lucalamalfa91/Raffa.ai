using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.AiGateway.Tests.TestSupport;

namespace Raffa.AiGateway.Tests;

public class FixtureAiGatewayAnswerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static FixtureAiGateway CreateGateway() =>
        new(new AiGatewayModelOptions(), new FixedClock(Now));

    [Fact]
    public async Task Answer_abstains_with_no_evidence_instead_of_fabricating()
    {
        var gateway = CreateGateway();

        var result = await gateway.AnswerAsync(
            new AiAnswerRequest("What liability do we have with AWS?", Evidence: []));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.CanDetermine);
        Assert.Null(result.Value.Answer);
        Assert.Empty(result.Value.Citations);
    }

    [Fact]
    public async Task Answer_grounds_the_answer_in_the_given_evidence_and_returns_citations()
    {
        var gateway = CreateGateway();
        var evidence = new AiEvidenceSnippet(
            "doc-123", Page: 12, Section: "8.4", Text: "Liability is capped at 12 months' fees.");

        var result = await gateway.AnswerAsync(
            new AiAnswerRequest("What liability do we have with AWS?", Evidence: [evidence]));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.CanDetermine);
        Assert.NotNull(result.Value.Answer);
        Assert.Contains("capped at 12 months", result.Value.Answer);

        var citation = Assert.Single(result.Value.Citations);
        Assert.Equal("doc-123", citation.DocumentId);
        Assert.Equal(12, citation.Page);
        Assert.Equal("8.4", citation.Section);
    }

    [Fact]
    public async Task Answer_fails_without_a_question()
    {
        var gateway = CreateGateway();

        var result = await gateway.AnswerAsync(new AiAnswerRequest("", Evidence: []));

        Assert.True(result.IsFailure);
    }
}
