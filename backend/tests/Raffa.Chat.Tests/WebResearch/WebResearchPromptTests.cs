using Raffa.Chat.Application.WebResearch;

namespace Raffa.Chat.Tests.WebResearch;

/// <summary>Same drift proof as <c>AnswerPromptV2Tests</c>, for the research persona (ADR-030).</summary>
public sealed class WebResearchPromptTests
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
    public void Markdown_file_carries_the_constant_verbatim_and_the_same_version()
    {
        var versionTag = WebResearchPrompt.Version.Replace("research-", string.Empty, StringComparison.Ordinal);
        var path = RepoRelative(Path.Combine("src", "Raffa.Chat", "Prompts", "research", $"{versionTag}.md"));

        Assert.True(File.Exists(path), $"Expected the versioned prompt file at {path}.");

        var markdown = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        var body = WebResearchPrompt.SystemPrompt.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains($"({versionTag})", markdown, StringComparison.Ordinal);
        Assert.Contains(body, markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Persona_refuses_off_topic_never_writes_urls_and_declares_itself_unverified()
    {
        Assert.Equal("research-v1", WebResearchPrompt.Version);
        Assert.Contains("set offTopic to true", WebResearchPrompt.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Never write a URL inside summaryMarkdown", WebResearchPrompt.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("unverified", WebResearchPrompt.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("never see the buyer's contracts", WebResearchPrompt.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Never give legal advice", WebResearchPrompt.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_persona_markdown_file_carries_the_constant_verbatim_and_the_same_version()
    {
        var versionTag = WebResearchPrompt.OpenVersion.Replace("research-", string.Empty, StringComparison.Ordinal);
        var path = RepoRelative(Path.Combine("src", "Raffa.Chat", "Prompts", "research", $"{versionTag}.md"));

        Assert.True(File.Exists(path), $"Expected the versioned prompt file at {path}.");

        var markdown = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        var body = WebResearchPrompt.OpenSystemPrompt.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains($"({versionTag})", markdown, StringComparison.Ordinal);
        Assert.Contains(body, markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_persona_keeps_every_law_but_the_scope_and_refuses_only_the_personal()
    {
        Assert.Equal("research-open-v1", WebResearchPrompt.OpenVersion);
        Assert.Contains("Research any topic with a plausible link to the team's work", WebResearchPrompt.OpenSystemPrompt, StringComparison.Ordinal);
        Assert.Contains("set offTopic", WebResearchPrompt.OpenSystemPrompt, StringComparison.Ordinal);
        Assert.Contains("recipes", WebResearchPrompt.OpenSystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Never write a URL inside summaryMarkdown", WebResearchPrompt.OpenSystemPrompt, StringComparison.Ordinal);
        Assert.Contains("unverified", WebResearchPrompt.OpenSystemPrompt, StringComparison.Ordinal);
        Assert.Contains("never see the team's contracts", WebResearchPrompt.OpenSystemPrompt, StringComparison.Ordinal);
        Assert.Contains("never give legal", WebResearchPrompt.OpenSystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Research only these procurement purposes", WebResearchPrompt.OpenSystemPrompt, StringComparison.Ordinal);
    }
}
