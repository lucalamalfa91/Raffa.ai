using Raffa.AiGateway.Agents;

namespace Raffa.AiGateway.Tests.Agents;

/// <summary>Plan LANG-04 plumbing: versioned per-language response directives (IT/EN/FR/ES/DE),
/// opt-in per definition, default off, appended by the runner without touching any prompt text.</summary>
public sealed class LanguageDirectiveTests
{
    private sealed record LangInput(string Question, string? Language) : ILanguageAware;

    // Pinned per version: a text edit without a version bump fails here (T-06 drift rule).
    private static readonly Dictionary<string, string> Pinned = new()
    {
        ["auto"] = "e77ab6ab2b9ac62192a8e49a512b2818c9da3e9479453c5feff62277e956c906",
        ["it"] = "00f1710d4efc36b27f6256b6e48ead5ffef637e7ee0ed88058e736d1aeff2e91",
        ["en"] = "8ebbd3e7f9b8114e88cf14b69c3c2b33f7b1e47e80aa604b54ba598759bd0c86",
        ["fr"] = "46e31cb672fe5ec6a29fa92203db25d25b03f321369dd2bf43287d8732100723",
        ["es"] = "2daa7df0d8b7885c8a95e0474e0ae9010efbdb96f4706f8f43946899884a0490",
        ["de"] = "1bcdc8786568c15930ce1d2f50c5820aa897e4337f145612cb328446f8d69485",
    };

    [Fact]
    public void The_directive_set_is_versioned_and_pinned_for_all_five_languages_plus_auto()
    {
        Assert.Equal("lang-v1", LanguageDirectives.Version);
        Assert.Equal(["it", "en", "fr", "es", "de"], LanguageDirectives.Supported);
        Assert.Equal(["auto", "it", "en", "fr", "es", "de"], LanguageDirectives.Keys);
        foreach (var key in LanguageDirectives.Keys)
        {
            Assert.Equal(Pinned[key], LanguageDirectives.Hash(key == "auto" ? null : key));
        }
    }

    [Theory]
    [InlineData("it", "Italian")]
    [InlineData("en", "English")]
    [InlineData("fr", "French")]
    [InlineData("es", "Spanish")]
    [InlineData("de", "German")]
    [InlineData("IT-it", "Italian")]
    [InlineData("de_CH", "German")]
    public void Each_supported_language_gets_its_own_directive(string code, string name)
    {
        Assert.Contains(name, LanguageDirectives.For(code), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pt")]
    [InlineData("auto")]
    public void An_unknown_language_gets_the_language_of_the_question_directive(string? code)
    {
        Assert.Equal("auto", LanguageDirectives.Resolve(code));
        Assert.Contains("language of the user's question", LanguageDirectives.For(code), StringComparison.Ordinal);
    }

    [Fact]
    public void Definitions_do_not_opt_in_by_default_and_opting_in_changes_neither_prompt_nor_hash()
    {
        var plain = AgentTestKit.Definition();
        var opted = plain.WithResponseLanguageDirective();

        Assert.False(plain.ResponseLanguageDirective);
        Assert.True(opted.ResponseLanguageDirective);
        Assert.Equal(plain.Prompt, opted.Prompt);
        Assert.Equal(plain.PromptHash, opted.PromptHash);
        Assert.Equal(plain.SystemPrompt, opted.SystemPrompt);
        Assert.Equal(plain.SystemPrompt, plain.SystemPromptFor("fr"));
        Assert.Equal(plain.Name, opted.Name);
        Assert.Equal(plain.Deadline, opted.Deadline);
        Assert.Equal(plain.FailurePolicy, opted.FailurePolicy);
    }

    [Fact]
    public void Opting_in_keeps_the_other_options_of_the_definition()
    {
        var original = AgentDefinition.ForOutput<EchoOutput>(
            "keeps", "k-v1", "Prompt.", TimeSpan.FromSeconds(3), AgentFailurePolicy.Skip,
            retryOnVerifierViolation: false, skipOnDeadline: true, languageDirective: "Static.");

        var opted = original.WithResponseLanguageDirective();

        Assert.False(opted.RetryOnVerifierViolation);
        Assert.True(opted.SkipOnDeadline);
        Assert.Equal("Static.", opted.LanguageDirective);
        Assert.Equal(original.JsonSchema, opted.JsonSchema);
    }

    [Theory]
    [InlineData("it")]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    public async Task The_runner_sends_the_directive_of_the_inputs_language_after_the_prompt(string language)
    {
        var gateway = new ScriptedGateway();
        var definition = AgentTestKit.Definition().WithResponseLanguageDirective();
        var step = new AgentStep<LangInput, EchoOutput>(definition);

        await new StepRunner(gateway).RunAsync(step, new LangInput("q", language));

        Assert.Equal(
            definition.SystemPrompt + Environment.NewLine + Environment.NewLine + LanguageDirectives.For(language),
            gateway.AnalyzeRequests.Single().SystemPrompt);
    }

    [Fact]
    public async Task An_input_without_a_language_gets_the_auto_directive_and_a_non_opted_agent_gets_nothing()
    {
        var gateway = new ScriptedGateway();
        var opted = AgentTestKit.Definition("opted").WithResponseLanguageDirective();

        await new StepRunner(gateway).RunAsync(AgentTestKit.Step(opted), AgentTestKit.Input);
        await new StepRunner(gateway).RunAsync(AgentTestKit.Step(AgentTestKit.Definition("plain")), AgentTestKit.Input);

        Assert.EndsWith(LanguageDirectives.For(null), gateway.AnalyzeRequests[0].SystemPrompt, StringComparison.Ordinal);
        Assert.Equal(AgentTestKit.Definition("plain").SystemPrompt, gateway.AnalyzeRequests[1].SystemPrompt);
    }

    [Fact]
    public async Task A_verifier_retry_keeps_the_language_directive()
    {
        var gateway = new ScriptedGateway();
        var step = new AgentStep<LangInput, EchoOutput>(
            AgentTestKit.Definition().WithResponseLanguageDirective(),
            Verifier.From<LangInput, EchoOutput>("once", (_, _) => StepVerdict.Reject("bad.")));

        await new StepRunner(gateway).RunAsync(step, new LangInput("q", "de"));

        Assert.Equal(2, gateway.AnalyzeRequests.Count);
        Assert.All(gateway.AnalyzeRequests, r => Assert.Contains(LanguageDirectives.For("de"), r.SystemPrompt, StringComparison.Ordinal));
        Assert.Contains("bad.", gateway.AnalyzeRequests[1].SystemPrompt, StringComparison.Ordinal);
    }
}
