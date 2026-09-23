using Raffa.Chat.Application.Gaps;

namespace Raffa.Chat.Tests.Gaps;

/// <summary>The investigator's prompt is versioned twice — the constant the agent sends and the
/// markdown a person reviews (<c>Prompts/gaps/v1.md</c>). Same drift test as
/// <c>DraftingAgentsPromptTests</c>.</summary>
public sealed class CapabilityInvestigatorPromptTests
{
    private static string RepoRelative(string path)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Raffa.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, path);
    }

    [Fact]
    public void Markdown_file_carries_the_prompt_the_abilities_and_the_schema_verbatim()
    {
        var versionTag = CapabilityInvestigatorAgent.Version.Replace("gaps-", string.Empty, StringComparison.Ordinal);
        var path = RepoRelative(Path.Combine("src", "Raffa.Chat", "Prompts", "gaps", $"{versionTag}.md"));

        Assert.True(File.Exists(path), $"Expected the versioned prompt file at {path}.");

        var markdown = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains($"## {CapabilityInvestigatorAgent.Name} ({versionTag})", markdown, StringComparison.Ordinal);
        Assert.Contains(CapabilityInvestigatorAgent.Prompt.Replace("\r\n", "\n", StringComparison.Ordinal), markdown, StringComparison.Ordinal);
        Assert.Contains(CapabilityInvestigatorAgent.Schema.Replace("\r\n", "\n", StringComparison.Ordinal), markdown, StringComparison.Ordinal);
        Assert.All(CapabilityInvestigatorAgent.AskAbilities, ability => Assert.Contains("- " + ability, markdown, StringComparison.Ordinal));
    }

    [Fact]
    public void Prompt_keeps_the_privacy_law_and_the_bias_towards_answering()
    {
        var prompt = CapabilityInvestigatorAgent.Prompt;

        Assert.Contains("become a public GitHub issue", prompt, StringComparison.Ordinal);
        Assert.Contains("Never copy into a feature field a", prompt, StringComparison.Ordinal);
        Assert.Contains("When in doubt between", prompt, StringComparison.Ordinal);
        Assert.Contains("choose \"question\"", prompt, StringComparison.Ordinal);
        Assert.Contains("Never claim that the feature exists or that it will be built", prompt, StringComparison.Ordinal);
    }
}
