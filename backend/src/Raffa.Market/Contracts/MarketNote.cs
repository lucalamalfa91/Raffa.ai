namespace Raffa.Market.Contracts;

/// <summary>
/// One market-knowledge search hit (task objective, Projection 2: "`IMarketKnowledgeRetrieval`
/// ... → `MarketNote` hits: recordId, title, snippet, category, geography, updatedAt, provenance
/// label, score"). <see cref="Retrieval.MarketNoteComposer.Compose"/> builds the query-independent
/// fields (everything except <see cref="Score"/>) from one <see cref="MarketDeal"/>;
/// <see cref="Retrieval.IMarketKnowledgeRetrieval.SearchAsync"/> is what actually assigns
/// <see cref="Score"/> per query (a composed note has no query to score against, so
/// <see cref="Retrieval.MarketNoteComposer.Compose"/> leaves it at <c>0</c> — see that method's
/// own doc comment). This is the shared read-only market side of R-MKT-03's "two projections":
/// a tenant search never returns a <see cref="MarketNote"/> and this type never carries a
/// <c>tenant_id</c> (ADR-024 "three sources, one rule"; parent story AC-3).
/// </summary>
/// <param name="RecordId">The originating <see cref="MarketDeal.RecordId"/> — the citation key a
/// caller resolves back to one record (T02's <c>GET /api/market/records/{id}</c>).</param>
/// <param name="Title">Short, human-readable heading, e.g. <c>"Salesforce · Sales Cloud
/// Enterprise"</c> (optionally with a SKU/edition suffix).</param>
/// <param name="Snippet">The composed narrative — one paragraph per record, e.g. "Companies of
/// 500–2 000 employees closing Salesforce Sales Cloud in CH in 2026 paid P50 CHF 132 per
/// user/month, obtained 3–5 % uplift caps and 90-day notice…" (R-MKT-03 example, verbatim
/// requirements.md flavour).</param>
/// <param name="Category">Echoes <see cref="MarketDeal.Category"/> so a caller can filter/facet
/// without re-fetching the record.</param>
/// <param name="Geography">Echoes <see cref="MarketDeal.Geography"/>.</param>
/// <param name="UpdatedAt">Echoes <see cref="MarketDeal.UpdatedAt"/> — shown alongside
/// <see cref="Provenance"/> in the UX (R-MKT-04).</param>
/// <param name="Provenance">The UX provenance label — <see cref="MarketProvenance.Label"/>'s
/// output for the originating record, e.g. <c>"representative market data · mock feed · updated
/// 2026-06-20"</c>. Never omitted: R-MKT-04 requires every market number or note shown in Ask,
/// Renewals or Quote check to carry this label until the live API is wired.</param>
/// <param name="Score">Relevance score assigned by the retrieval implementation that produced
/// this hit (token-overlap ratio for <see cref="Retrieval.InMemoryMarketKnowledgeRetrieval"/> in
/// this task; a vector-similarity score once T02 swaps in the pgvector index) — not a property of
/// the record itself, only of one search's ranking of it.</param>
public sealed record MarketNote(
    string RecordId,
    string Title,
    string Snippet,
    string Category,
    string Geography,
    DateTimeOffset UpdatedAt,
    string Provenance,
    double Score);
