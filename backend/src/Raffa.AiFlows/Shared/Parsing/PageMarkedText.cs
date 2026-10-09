using System.Text;
using Raffa.Documents.Contracts.Application.Extraction;

namespace Raffa.AiFlows.Shared.Parsing;

/// <summary>
/// Concatenates the parsed pages of a document into the text the `extract` role reads, with an
/// explicit <c>[[PAGE n]]</c> marker in front of each page (ADR-017 "Implications for the
/// decomposition": "must persist a page map so evidence source.page / section still resolve") so a
/// structured-output model can report which page a fact came from by reading these markers,
/// without a separate call per page. Quote extraction uses it today; it is the same convention as
/// <c>StagedExtractionService.BuildPageMarkedText</c> (private to the Documents.Contracts module,
/// which will adopt this helper when that service is split). The text is part of the extraction
/// input hash, so the format is a contract: <c>PageMarkedTextTests</c> pins it, including its
/// parity with the document-extraction copy.
/// </summary>
public static class PageMarkedText
{
    public static string Build(IReadOnlyList<DocumentPageText> pages)
    {
        var builder = new StringBuilder();

        foreach (var page in pages)
        {
            builder.Append("[[PAGE ").Append(page.PageNumber).Append("]]\n");
            builder.Append(page.Text);
            builder.Append("\n\n");
        }

        return builder.ToString();
    }
}
