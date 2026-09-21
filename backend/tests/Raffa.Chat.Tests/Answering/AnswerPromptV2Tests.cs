using Raffa.Chat.Application.Answering;

namespace Raffa.Chat.Tests.Answering;

/// <summary>
/// The persona prompt is versioned twice — the C# constant the composer sends and the markdown a
/// person reviews. This test is what keeps them from drifting apart: the markdown must carry the
/// constant's body verbatim and name the same version.
/// </summary>
public sealed class AnswerPromptV2Tests
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
        var versionTag = AnswerPromptV2.Version.Replace("answer-", string.Empty, StringComparison.Ordinal);
        var path = RepoRelative(Path.Combine("src", "Raffa.Chat", "Prompts", "answer", $"{versionTag}.md"));

        Assert.True(File.Exists(path), $"Expected the versioned prompt file at {path}.");

        var markdown = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        var body = AnswerPromptV2.SystemPrompt.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains($"({versionTag})", markdown, StringComparison.Ordinal);
        Assert.Contains(body, markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_keeps_the_grounding_laws_and_the_consultant_structure()
    {
        Assert.Contains("Never invent a number", AnswerPromptV2.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Cosa chiedere al fornitore", AnswerPromptV2.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Never tables", AnswerPromptV2.SystemPrompt, StringComparison.Ordinal);
        Assert.Equal("answer-v2.2", AnswerPromptV2.Version);
    }
}
