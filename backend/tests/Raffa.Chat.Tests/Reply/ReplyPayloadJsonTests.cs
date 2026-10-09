using Raffa.Chat.Application.Feedback;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Reply;

namespace Raffa.Chat.Tests.Reply;

public sealed class ReplyPayloadJsonTests
{
    [Fact]
    public void Round_trips_every_member_and_writes_nulls_explicitly()
    {
        // Hand-written: Chat owns the payload contract, not the capability-gap catalog that builds it.
        var payload = new ReplyPayload(
            new GapInfo("email-draft", "Scrivere un'email di negoziazione", "it"),
            new EmailDraft("Oggetto", "Corpo\ncon due righe"),
            new FeedbackOffer(
                "Vuoi segnalarlo al team Raffa.ai perché lo implementi?",
                "Sì", "No", "Avanti", "Indietro", "Invia", "Invio in corso…", "Grazie!",
                "Non sono riuscito a inviare la segnalazione. Riprova.",
                "Le risposte saranno pubbliche su GitHub.",
                [
                    new FeedbackQuestion(FeedbackQuestions.WhatKey, FeedbackQuestions.TextKind, "Cosa dovrebbe fare Raffa esattamente?", "prefill", null),
                    new FeedbackQuestion(FeedbackQuestions.FrequencyKey, FeedbackQuestions.ChoiceKind, "Quanto spesso ti servirebbe?", null,
                    [
                        new FeedbackChoice(FeedbackQuestions.FrequencyEveryRenewal, "ad ogni rinnovo"),
                        new FeedbackChoice(FeedbackQuestions.FrequencyWeekly, "ogni settimana"),
                        new FeedbackChoice(FeedbackQuestions.FrequencySometimes, "ogni tanto"),
                    ]),
                    new FeedbackQuestion(FeedbackQuestions.ImportanceKey, FeedbackQuestions.ChoiceKind, "Quanto è importante per il tuo lavoro?", null,
                    [
                        new FeedbackChoice(FeedbackQuestions.ImportanceBlocking, "bloccante"),
                        new FeedbackChoice(FeedbackQuestions.ImportanceVeryUseful, "molto utile"),
                        new FeedbackChoice(FeedbackQuestions.ImportanceNiceToHave, "comodo"),
                    ]),
                ]),
            null);

        var json = ReplyPayloadJson.Serialize(payload);
        var back = ReplyPayloadJson.Deserialize(json);

        Assert.Contains("\"feedbackResult\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"prefill\":\"", json, StringComparison.Ordinal);
        Assert.NotNull(back);
        Assert.Equal(payload.Gap, back!.Gap);
        Assert.Equal(payload.Draft, back.Draft);
        Assert.Equal(payload.FeedbackOffer!.Questions.Count, back.FeedbackOffer!.Questions.Count);
        Assert.Equal(payload.FeedbackOffer.Questions[1].Choices!.Select(c => c.Key), back.FeedbackOffer.Questions[1].Choices!.Select(c => c.Key));
        Assert.Null(back.FeedbackResult);
    }

    [Fact]
    public void A_blank_or_malformed_column_reads_as_no_payload()
    {
        Assert.Null(ReplyPayloadJson.Deserialize(null));
        Assert.Null(ReplyPayloadJson.Deserialize("   "));
        Assert.Null(ReplyPayloadJson.Deserialize("{not json"));
    }

    [Fact]
    public void Draft_maps_to_draft_on_the_wire() => Assert.Equal("draft", ReplyKind.Draft.ToApiValue());

    [Fact]
    public void An_external_action_needs_an_absolute_https_url()
    {
        var action = CopilotAction.External("Open issue #12 →", "https://github.com/lucalamalfa91/Raffa.ai/issues/12");

        Assert.Equal(CopilotActionKind.External, action.Kind);
        Assert.Throws<ArgumentException>(() => CopilotAction.External("x", "/renewals"));
        Assert.Throws<ArgumentException>(() => CopilotAction.External("x", "http://github.com/x"));
    }
}
