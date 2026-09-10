using Raffa.Chat.Application.Pack;

namespace Raffa.Chat.Application.Reply;

/// <summary>
/// One `citations[]` entry of the ADR-024 §6 reply contract — `inputs/requirements.md` §6's own
/// field list verbatim: <c>{ n, corpus, title, subtitle, snippet, documentId?, contractId?, page?,
/// section?, previewUrl?, href?, recordId? }`. Built from a <see cref="PackItem"/> once
/// <c>Guards.GroundingGuard</c> has already proven the citing <c>citationKey</c> is real
/// (<see cref="CopilotReplyBuilder.BuildCitations"/>) — never constructed ad hoc from model output.
/// </summary>
/// <param name="N">1-based position — the same number the answer's own inline <c>[n]</c> marker
/// names, in the order the model returned <c>citationKeys</c> (not the pack's own item order).</param>
/// <param name="Corpus">Echoes <see cref="PackItem.Corpus"/>.</param>
/// <param name="Title">Echoes <see cref="PackItem.Title"/>.</param>
/// <param name="Subtitle">Echoes <see cref="PackItem.Subtitle"/>.</param>
/// <param name="Snippet">Echoes <see cref="PackItem.Snippet"/>.</param>
/// <param name="DocumentId">Set only for a <see cref="PackCorpus.Tenant"/> item that resolves to
/// one source document.</param>
/// <param name="ContractId">Set only for a <see cref="PackCorpus.Tenant"/> item scoped to one
/// contract.</param>
/// <param name="Page">Echoes <see cref="PackItem.Page"/>.</param>
/// <param name="Section">Echoes <see cref="PackItem.Section"/>.</param>
/// <param name="PreviewUrl">Echoes <see cref="PackItem.PreviewUrl"/>.</param>
/// <param name="Href">Echoes <see cref="PackItem.Href"/>.</param>
/// <param name="RecordId">Echoes <see cref="PackItem.RecordId"/>.</param>
public sealed record ReplyCitation(
    int N,
    string Corpus,
    string Title,
    string? Subtitle,
    string Snippet,
    string? DocumentId,
    string? ContractId,
    int? Page,
    string? Section,
    string? PreviewUrl,
    string? Href,
    string? RecordId);
