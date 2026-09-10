using Raffa.SharedKernel;
using Pgvector;

namespace Raffa.Documents.Contracts.Domain;

/// <summary>
/// A pgvector embedding stored next to the relational facts it was derived from (ADR-003) — no
/// separate vector store. <see cref="SourceType"/> / <see cref="SourceId"/> are a loose
/// pointer (Document or Clause content today) rather than a single FK, mirroring
/// <see cref="CorrectionHistory"/>. <see cref="ChunkText"/> keeps the literal embedded text so
/// Ask Raffa can cite it (spec §8.3); retrieval must still apply tenant authorization before
/// this row ever reaches an LLM context (Appendix C rule 4 — enforced by us-03 RLS, not here).
/// </summary>
public sealed class Embedding : TenantScopedEntity
{
    /// <summary>Fixed at schema time per ADR-004 ("small dimension preferred for cost/size"):
    /// matches Foundry's `text-embedding-3-small`, the smaller of the two named embed-role
    /// candidates. Recorded as an assumption in force in reports/open-questions.md.</summary>
    public const int VectorDimensions = 1536;

    /// <summary>Discriminator for the polymorphic source, e.g. "Document", "Clause".</summary>
    public required string SourceType { get; set; }
    public required EntityId SourceId { get; set; }
    public int ChunkIndex { get; set; }

    /// <summary>
    /// 1-based page this chunk was read from, when the chunk maps to a page of a parsed document
    /// (task E13/F04/US01/T02; <c>inputs/requirements.md</c> R-EVD-01 "embedding rows store page
    /// number and section label", R-DOC-07 AC-2). <see langword="null"/> for a chunk with no page
    /// concept (a clause excerpt indexed from a row that has no <c>SourcePage</c>, a market note),
    /// never a fabricated <c>1</c> — an Ask citation prints "p.N" only when this is really known.
    /// </summary>
    public int? Page { get; set; }

    /// <summary>
    /// Section label for this chunk when one is known (a clause type / source span, R-EVD-01);
    /// <see langword="null"/> otherwise. Free text, not an enum: it is quoted into a citation
    /// subtitle, never matched on.
    /// </summary>
    public string? Section { get; set; }
    public required string ChunkText { get; set; }
    public required Vector Vector { get; set; }

    /// <summary>Foundry embedding model id/version that produced this vector (brief §8 logging).</summary>
    public required string Model { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
