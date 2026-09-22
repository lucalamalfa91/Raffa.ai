namespace Raffa.AiGateway.Contracts;

/// <summary>
/// Input of the <c>research</c> role (ADR-030): the one Foundry call that may reach the public
/// web. Deliberately carries <b>no</b> context pack, evidence or tenant text — only a query the
/// caller already sanitised (<c>Raffa.Chat.Application.WebResearch.WebQuerySanitizer</c>), the
/// research purpose, the answer language and the source cap. The isolation from tenant data is in
/// this type's shape, not in a convention.
/// </summary>
/// <param name="Query">The sanitised search query (supplier names allowed; never an amount,
/// percentage, date or clause text).</param>
/// <param name="Purpose">One of the procurement research purposes (market practice, supplier news,
/// benchmark range, negotiation tactics) the persona is allowed to serve.</param>
/// <param name="Language">"it" or "en" — the language of the summary.</param>
/// <param name="MaxSources">Upper bound on the sources returned.</param>
/// <param name="SystemPrompt">The versioned research persona.</param>
/// <param name="PromptVersion">Logged as <see cref="AiCallMetadata.PromptVersion"/>.</param>
public sealed record AiResearchRequest(
    string Query,
    string Purpose,
    string Language,
    int MaxSources,
    string SystemPrompt,
    string PromptVersion);

/// <summary>One public web source the research role actually read (a tool citation, never a
/// URL the model typed from memory).</summary>
public sealed record AiWebSource(string Url, string Title, string Snippet, DateTimeOffset? PublishedAt = null);

/// <summary>
/// Output of the <c>research</c> role. <see cref="OffTopic"/> is <see langword="true"/> when the
/// persona refused a query outside procurement; <see cref="Sources"/> is then empty and
/// <see cref="SummaryMarkdown"/> blank. A summary cites its sources with <c>[n]</c> markers only;
/// the caller's <c>WebGuard</c> is what proves every marker and every URL points at one of them.
/// </summary>
public sealed record AiResearchResult(
    string SummaryMarkdown,
    IReadOnlyList<AiWebSource> Sources,
    bool OffTopic,
    AiCallMetadata Metadata);
