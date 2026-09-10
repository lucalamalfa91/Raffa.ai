namespace Raffa.AiGateway.Contracts;

/// <summary>
/// A source pointer backing one claim in an <see cref="AiAnswerResult"/> (spec §8.3 "Answer +
/// citations"; §8.4 "no evidence, no claim" — the user must be able to open the original evidence
/// directly).
/// </summary>
/// <param name="DocumentId">Cited document identifier.</param>
/// <param name="Page">Cited page, when known.</param>
/// <param name="Section">Cited section/clause label, when known.</param>
public sealed record AiCitation(
    string DocumentId,
    int? Page,
    string? Section);
