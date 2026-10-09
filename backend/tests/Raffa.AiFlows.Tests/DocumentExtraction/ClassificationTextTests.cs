using Raffa.AiFlows.DocumentExtraction.Classification;
using Raffa.Documents.Contracts.Application.Extraction;

namespace Raffa.AiFlows.Tests.DocumentExtraction;

/// <summary>
/// <see cref="ClassificationText"/> replaced two identical private copies (the admission gate's and
/// the orchestrator's). Parity with the formula both copies used: every page's text, in order,
/// joined by a blank line. The text is what the classifier sees, so any drift changes the verdict.
/// </summary>
public sealed class ClassificationTextTests
{
    public static TheoryData<string[]> PageSets => new()
    {
        new string[] { },
        new[] { "MASTER SERVICES AGREEMENT" },
        new[] { "Page one text", "Page two text" },
        new[] { "Page one text", "", "Page three text" },
        new[] { "line one\nline two", "  padded  ", "tail\r\n" },
    };

    [Theory]
    [MemberData(nameof(PageSets))]
    public void Build_matches_the_previous_private_formula(string[] texts)
    {
        IReadOnlyList<DocumentPageText> pages = texts
            .Select((text, index) => new DocumentPageText(index + 1, text))
            .ToList();

        var expected = string.Join("\n\n", pages.Select(p => p.Text));

        Assert.Equal(expected, ClassificationText.Build(pages));
    }

    [Fact]
    public void Build_separates_pages_by_exactly_one_blank_line()
    {
        IReadOnlyList<DocumentPageText> pages = [new(1, "A"), new(2, "B")];

        Assert.Equal("A\n\nB", ClassificationText.Build(pages));
    }
}
