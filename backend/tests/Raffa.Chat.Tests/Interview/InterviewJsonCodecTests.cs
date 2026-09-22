using Raffa.Chat.Application.Interview;
using Raffa.Chat.Domain;
using Raffa.SharedKernel;

namespace Raffa.Chat.Tests.Interview;

public sealed class InterviewJsonCodecTests
{
    private static InterviewTurn SampleTurn() => new(
        "Before I answer, one quick check.",
        [
            new InterviewQuestion(
                "interpretation", "Which of these do you mean?", InterviewPresentation.Choice, true,
                [
                    new InterviewOption(
                        "total-spend", "Our total annual spend", null,
                        new InterviewResolution(AskIntent.StructuredFact, null, null, "What is our total annual spend across all contracts?")),
                    new InterviewOption(
                        "contract-1", "Salesforce · Msa (renews 2027-01-09)", null,
                        new InterviewResolution(AskIntent.RenewalStrategy, "11111111-1111-1111-1111-111111111111", "Salesforce", "How should I approach the renewal?")),
                ]),
            new InterviewQuestion(
                "consent", "Search the public web?", InterviewPresentation.Consent, false,
                [
                    new InterviewOption(
                        "allow", "Allow this search", "Nothing from your contracts leaves Raffa.",
                        new InterviewResolution(null, null, null, "saas renewal practice", new WebResearchRequest("saas renewal practice", "MarketPractice"))),
                    new InterviewOption("decline", "No, stay in Raffa", null, new InterviewResolution(null, null, null, "original question")),
                ]),
        ]);

    [Fact]
    public void A_turn_round_trips_with_every_resolution_intact()
    {
        var json = InterviewJsonCodec.SerializeTurn(SampleTurn());

        var record = InterviewJsonCodec.TryDecodeTurn(json);

        Assert.NotNull(record);
        Assert.Equal(InterviewJsonCodec.Version, record.Version);
        Assert.Null(record.ConsumedAt);
        Assert.Equal(SampleTurn(), record.ToTurn());
        Assert.Equal("Salesforce", record.FindQuestion("interpretation")!.Options[1].ResolvesTo.SupplierName);
        Assert.Equal("MarketPractice", record.FindQuestion("consent")!.Options[0].ResolvesTo.WebResearch!.Purpose);
        Assert.Contains("\"presentation\":\"consent\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("consumedAt", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Marking_an_option_consumed_stamps_the_record_once()
    {
        var record = InterviewJsonCodec.TryDecodeTurn(InterviewJsonCodec.SerializeTurn(SampleTurn()))!;
        var at = new DateTimeOffset(2026, 9, 22, 15, 0, 0, TimeSpan.Zero);

        var consumed = InterviewJsonCodec.TryDecodeTurn(InterviewJsonCodec.MarkConsumed(record, "allow", at));

        Assert.NotNull(consumed);
        Assert.Equal(at, consumed.ConsumedAt);
        Assert.Equal("allow", consumed.ConsumedOptionKey);
        Assert.Equal(record.Questions, consumed.Questions);
    }

    [Fact]
    public void An_answer_reference_round_trips()
    {
        var messageId = EntityId.New();

        var json = InterviewJsonCodec.SerializeAnswer(messageId, "interpretation", "total-spend", freeText: false);
        var answer = InterviewJsonCodec.TryDecodeAnswer(json);

        Assert.NotNull(answer);
        Assert.Equal(messageId.Value, answer.AnswerTo);
        Assert.Equal("interpretation", answer.QuestionKey);
        Assert.Equal("total-spend", answer.OptionKey);
        Assert.False(answer.FreeText);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"version\":1,\"prompt\":\"x\",\"questions\":[]}")]
    public void Anything_that_is_not_an_interview_decodes_to_null(string? json)
    {
        Assert.Null(InterviewJsonCodec.TryDecodeTurn(json));
    }
}
