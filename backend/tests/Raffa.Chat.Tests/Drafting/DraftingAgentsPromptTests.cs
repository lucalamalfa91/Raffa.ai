using Raffa.Chat.Application.Drafting;

namespace Raffa.Chat.Tests.Drafting;

/// <summary>The two drafting prompts are versioned twice — the C# constants the workflow sends
/// and the markdown a person reviews (<c>Prompts/draft/v1.md</c>). Same drift test as
/// <c>AnswerPromptV2Tests</c>.</summary>
public sealed class DraftingAgentsPromptTests
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
    public void Markdown_file_carries_both_constants_verbatim_and_the_same_version()
    {
        var versionTag = DraftingAgents.Version.Replace("draft-", string.Empty, StringComparison.Ordinal);
        var path = RepoRelative(Path.Combine("src", "Raffa.Chat", "Prompts", "draft", $"{versionTag}.md"));

        Assert.True(File.Exists(path), $"Expected the versioned prompt file at {path}.");

        var markdown = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains($"## {DraftingAgents.OfferPlannerName} ({versionTag})", markdown, StringComparison.Ordinal);
        Assert.Contains($"## {DraftingAgents.NegotiationWriterName} ({versionTag})", markdown, StringComparison.Ordinal);
        Assert.Contains(DraftingAgents.OfferPlannerPrompt.Replace("\r\n", "\n", StringComparison.Ordinal), markdown, StringComparison.Ordinal);
        Assert.Contains(DraftingAgents.NegotiationWriterPrompt.Replace("\r\n", "\n", StringComparison.Ordinal), markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompts_forbid_inline_markers_and_links()
    {
        foreach (var prompt in new[] { DraftingAgents.OfferPlannerPrompt, DraftingAgents.NegotiationWriterPrompt })
        {
            Assert.Contains("Never write an inline citation marker", prompt, StringComparison.Ordinal);
            Assert.Contains("Plain text only", prompt, StringComparison.Ordinal);
            Assert.Contains("Never invent a key", prompt, StringComparison.Ordinal);
        }

        Assert.Contains("[1]", DraftingAgents.BuildRetryInstruction("a violation"), StringComparison.Ordinal);
    }
}
