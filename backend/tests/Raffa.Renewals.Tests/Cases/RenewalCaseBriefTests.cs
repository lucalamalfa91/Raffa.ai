using Raffa.Renewals.Application.Cases;
using static Raffa.Renewals.Tests.Cases.CaseFixtures;

namespace Raffa.Renewals.Tests.Cases;

/// <summary>
/// A renewal started from a chat carries over what the chat settled (the levers): the case keeps it, shows it first,
/// and the interview questions say what the levers already suggest -- the person is not asked from scratch.
/// </summary>
public sealed class RenewalCaseBriefTests
{
    private const string Levers =
        "Pluriennale: valutare 36 mesi soltanto contro riduzione del prezzo unitario del 43,06%, prezzo bloccato.\n"
        + "Volumi: concordare la riduzione dei volumi al rinnovo senza penali.";

    private static List<string> Lines(RenewalCase c, string kind) =>
        RenewalCaseEngine.View(c, Ctx(1, Luca)).Cards
            .Where(card => card.K == kind)
            .SelectMany(card => card.Lines)
            .Select(l => l.Render())
            .ToList();

    [Fact]
    public void The_levers_from_chat_are_kept_and_shown_before_the_first_question()
    {
        var c = RenewalCaseEngine.Create(1, Ctx(1, Luca), owner: Marta, brief: Levers);

        Assert.Equal(Levers, c.Brief);
        var kinds = c.Feed.Select(f => f.K).ToList();
        Assert.True(kinds.IndexOf("say") > kinds.IndexOf("outlook"));
        Assert.True(kinds.IndexOf("say") < kinds.IndexOf("q"));
        Assert.Contains(c.Feed, f => f.K == "say" && f.Text is { Plain: { } plain } && plain == Levers);
    }

    [Fact]
    public void The_questions_say_what_the_levers_already_suggest()
    {
        var c = RenewalCaseEngine.Create(1, Ctx(1, Luca), owner: Marta, brief: Levers);

        // Question 1 (products) is open: the levers mention reducing volumes.
        Assert.Contains(Lines(c, "q"), l => l.Contains("reducing volumes", StringComparison.OrdinalIgnoreCase));

        // Question 2 (term) is open next: the levers rest on 36 months.
        c = RenewalCaseEngine.Act(c, "ans", new CaseActionPayload { Q = 0, Val = "reduce" }, Ctx(1, Marta));
        Assert.Contains(Lines(c, "q"), l => l.Contains("36-month", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_chat_the_case_is_what_it_always_was()
    {
        var c = RenewalCaseEngine.Create(1, Ctx(1, Luca), owner: Marta);

        Assert.Null(c.Brief);
        Assert.DoesNotContain(c.Feed, f => f.K == "say");
    }

    [Fact]
    public void The_levers_are_kept_once_and_capped()
    {
        var c = RenewalCaseEngine.Create(1, Ctx(1, Luca), owner: Marta, brief: new string('x', RenewalCaseEngine.BriefMaxChars * 2));
        var kept = c.Brief;

        RenewalCaseEngine.AttachBrief(c, Ctx(1, Luca), "another text");

        Assert.Equal(kept, c.Brief);
        Assert.True(c.Brief!.Length <= RenewalCaseEngine.BriefMaxChars + 1);
        Assert.Equal(1, c.Feed.Count(f => f.K == "say" && f.Text is { Plain: { } p } && p.StartsWith('x')));
    }

    [Fact]
    public void The_interview_wording_written_for_the_contract_replaces_the_standard_text_but_not_the_answers()
    {
        var tailoring = new CaseInterviewTailoring
        {
            Questions =
            [
                new CaseQuestionTailoring
                {
                    Index = 0,
                    Text = "Falcon: confermi le quantità o le riduci?",
                    Hint = "Dalla chat: riduzione dei volumi.",
                    Options = new Dictionary<string, string> { ["keep"] = "Mantieni Falcon come oggi" },
                },
            ],
        };
        var c = RenewalCaseEngine.Create(1, Ctx(1, Luca), owner: Marta, brief: Levers, tailoring: tailoring);

        var card = RenewalCaseEngine.View(c, Ctx(1, Luca)).Cards.First(x => x.K == "q");
        Assert.Equal("Falcon: confermi le quantità o le riduci?", card.Title.Render());
        Assert.Contains(card.Lines, l => l.Render() == "Dalla chat: riduzione dei volumi.");
        Assert.Contains(card.Chips, chip => chip.Label.Render() == "Mantieni Falcon come oggi" && chip.P.Val == "keep");
        // The other options keep their standard label and key.
        Assert.Contains(card.Chips, chip => chip.P.Val == "reduce" && chip.Label.Render() == "Reduce quantities");

        // The later questions have no tailoring: the standard text stands.
        c = RenewalCaseEngine.Act(c, "ans", new CaseActionPayload { Q = 0, Val = "keep" }, Ctx(1, Marta));
        var term = RenewalCaseEngine.View(c, Ctx(1, Marta)).Cards.Where(x => x.K == "q").Last();
        Assert.Equal("Term: how long should the renewed contract run?", term.Title.Render());
    }

    [Theory]
    [InlineData("possiamo ridurre anche il numero di licenze del 10 %")]
    [InlineData("we can reduce the number of licences by 10%")]
    [InlineData("Volumi: concordare la riduzione dei volumi al rinnovo.")]
    public void A_reduction_of_licences_or_volumes_set_in_chat_shows_under_the_products_question(string brief)
    {
        var c = RenewalCaseEngine.Create(1, Ctx(1, Luca), owner: Marta, brief: brief);

        Assert.Contains(Lines(c, "q"), l => l.Contains("reducing volumes", StringComparison.OrdinalIgnoreCase));
    }
}
