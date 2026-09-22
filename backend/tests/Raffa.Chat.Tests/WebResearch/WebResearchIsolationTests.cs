using System.Text.RegularExpressions;

namespace Raffa.Chat.Tests.WebResearch;

/// <summary>
/// ADR-030's structural isolation, held by a source scan rather than a convention: the research
/// role is called from exactly one place (<c>WebResearchComposer</c>) plus the gateway's own
/// decorators/doubles, and the <c>web</c> corpus is produced only there — never in the composition
/// root's pack builders, so a web source can never end up inside an <c>answer</c>-role pack.
/// </summary>
public sealed class WebResearchIsolationTests
{
    private static string BackendRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Raffa.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IEnumerable<string> SourceFiles(string project) =>
        Directory.EnumerateFiles(Path.Combine(BackendRoot(), "src", project), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [Fact]
    public void Only_the_composer_calls_the_research_role_outside_the_gateway()
    {
        var callers = SourceFiles("Raffa.Chat").Concat(SourceFiles("Raffa.Api"))
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"\.ResearchAsync\s*\("))
            .Select(path => Path.GetFileName(path))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["WebResearchComposer.cs"], callers);
    }

    [Fact]
    public void The_web_corpus_is_never_produced_by_the_composition_root()
    {
        var producers = SourceFiles("Raffa.Api")
            .Where(path => File.ReadAllText(path).Contains("PackCorpus.Web", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path))
            .ToList();

        Assert.Empty(producers);
    }

    [Fact]
    public void The_answer_composer_never_touches_the_research_role()
    {
        var answerComposer = File.ReadAllText(Path.Combine(BackendRoot(), "src", "Raffa.Chat", "Application", "Answering", "AnswerComposer.cs"));

        Assert.DoesNotContain("ResearchAsync", answerComposer, StringComparison.Ordinal);
        Assert.DoesNotContain("PackCorpus.Web", answerComposer, StringComparison.Ordinal);
    }
}
