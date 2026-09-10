namespace Raffa.AiGateway.Contracts;

/// <summary>
/// One piece of already-retrieved, already-authorized evidence handed to the `answer` role.
/// ADR-011: the caller resolves tenant/role/object authorization and runs retrieval (with a
/// mandatory <c>tenant_id</c> filter) *before* any text reaches the gateway — the gateway itself
/// never queries a store, so it cannot leak cross-tenant content.
/// </summary>
/// <param name="DocumentId">Source document identifier, so the answer can cite back to it (spec §8.4).</param>
/// <param name="Page">Source page, when known (spec §8.4 "expose the source document and page/section").</param>
/// <param name="Section">Source section/clause label, when known.</param>
/// <param name="Text">The evidence text itself.</param>
public sealed record AiEvidenceSnippet(
    string DocumentId,
    int? Page,
    string? Section,
    string Text);
