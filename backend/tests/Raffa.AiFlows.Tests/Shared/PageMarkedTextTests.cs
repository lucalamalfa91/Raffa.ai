using System.Reflection;
using Raffa.AiFlows.Shared.Parsing;
using Raffa.Documents.Contracts.Application.Extraction;

namespace Raffa.AiFlows.Tests.Shared;

/// <summary>
/// The <c>[[PAGE n]]</c> text is the extraction input: its format feeds the model prompt and (for
/// documents) the resume <c>InputHash</c>. <see cref="PageMarkedText"/> serves quote extraction
/// today and must stay equal to the copy document extraction still keeps in
/// <c>StagedExtractionService</c>, until that service adopts it.
/// </summary>
public sealed class PageMarkedTextTests
{
    private static readonly IReadOnlyList<DocumentPageText> Pages =
    [
        new(1, "Quote Q-1\nLicence A  10 x 100.00"),
        new(2, ""),
        new(12, "Page twelve with   spaces\r\nand a CRLF"),
    ];

    [Fact]
    public void Build_puts_a_page_marker_and_a_blank_line_around_every_page()
    {
        var text = PageMarkedText.Build(Pages);

        Assert.Equal(
            "[[PAGE 1]]\nQuote Q-1\nLicence A  10 x 100.00\n\n"
            + "[[PAGE 2]]\n\n\n"
            + "[[PAGE 12]]\nPage twelve with   spaces\r\nand a CRLF\n\n",
            text);
    }

    [Fact]
    public void Build_of_no_pages_is_empty()
    {
        Assert.Equal(string.Empty, PageMarkedText.Build([]));
    }

    [Fact]
    public void Build_is_identical_to_the_copy_document_extraction_still_keeps()
    {
        var staged = typeof(StagedExtractionService).GetMethod(
            "BuildPageMarkedText", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(staged);

        var fromStaged = (string)staged.Invoke(null, [Pages])!;

        Assert.Equal(fromStaged, PageMarkedText.Build(Pages));
        Assert.Equal(staged.Invoke(null, [(IReadOnlyList<DocumentPageText>)[]]), PageMarkedText.Build([]));
    }
}
