using Raffa.Documents.Contracts.Application.Extraction;

namespace Raffa.AiFlows.DocumentExtraction.Classification;

/// <summary>
/// The representative text the <c>classify</c> gateway role reads: every page, in order, separated
/// by a blank line, so a multi-page contract's type is judged on all of it and not on a cover page
/// alone. One place for the text the admission gate and the document-processing orchestrator both
/// hand to the classifier (the two used to carry identical private copies), so the two routes can
/// never classify different text.
/// </summary>
internal static class ClassificationText
{
    public static string Build(IReadOnlyList<DocumentPageText> pages) =>
        string.Join("\n\n", pages.Select(p => p.Text));
}
