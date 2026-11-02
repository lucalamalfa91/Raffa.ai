using Raffa.SharedKernel;
using Raffa.SharedKernel.Renewals;

namespace Raffa.Renewals.Tests.Cases;

public sealed class ScriptedCompanyRulesAssistantTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private readonly ScriptedCompanyRulesAssistant _assistant = new();

    [Theory]
    [InlineData("90", 90)]
    [InlineData("90 days", 90)]
    [InlineData("about 3 months before", 90)]
    [InlineData("2 settimane", 14)]
    [InlineData("4 mesi", 120)]
    [InlineData("1 day", 14)]
    [InlineData("5000 days", 365)]
    public void Days_are_read_from_the_answer_and_kept_within_bounds(string answer, int expected) =>
        Assert.Equal(expected, ScriptedCompanyRulesAssistant.ParseDays(answer));

    [Theory]
    [InlineData("")]
    [InlineData("as late as possible")]
    [InlineData("0")]
    public void No_number_means_no_window(string answer) =>
        Assert.Null(ScriptedCompanyRulesAssistant.ParseDays(answer));

    [Fact]
    public async Task The_interview_asks_one_question_per_turn_then_proposes_the_rules()
    {
        var turns = new List<RuleTurn>();
        string[] answers = ["60 days", "above CHF 100k, the CFO", "no auto-renewal", "Veeam is strategic", "nothing else"];
        foreach (var answer in answers)
        {
            var ask = await _assistant.InterviewAsync(Tenant, string.Empty, turns, "en");
            Assert.False(ask.Done);
            Assert.Null(ask.Proposal);
            Assert.NotEmpty(ask.Message);
            turns.Add(new RuleTurn("raffa", ask.Message));
            turns.Add(new RuleTurn("user", answer));
        }

        var done = await _assistant.InterviewAsync(Tenant, string.Empty, turns, "en");
        Assert.True(done.Done);
        Assert.Equal(60, done.StartWindowDays);
        Assert.Contains("60 days before its notice deadline", done.Proposal);
        Assert.Contains("above CHF 100k, the CFO", done.Proposal);
        Assert.Contains("Veeam is strategic", done.Proposal);
    }

    [Fact]
    public async Task The_first_question_is_in_the_users_language()
    {
        var it = await _assistant.InterviewAsync(Tenant, string.Empty, [], "it");
        Assert.Contains("giorni", it.Message);
        var en = await _assistant.InterviewAsync(Tenant, string.Empty, [], "en");
        Assert.Contains("days", en.Message);
    }

    [Fact]
    public async Task A_policy_is_kept_as_it_is_without_guessing_a_window()
    {
        var d = await _assistant.DistillAsync(Tenant, "policy.txt", "  Renew 90 days before notice.  ", string.Empty, "en");
        Assert.Equal("Renew 90 days before notice.", d.Text);
        Assert.Null(d.StartWindowDays);
    }
}
