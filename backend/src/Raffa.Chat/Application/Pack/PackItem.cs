namespace Raffa.Chat.Application.Pack;

/// <summary>
/// The three corpora ADR-024 admits into a context pack, plus the calculator-derived facts that
/// belong to none of them (task E13/F06/US01/T01, ask-engine coding objective point 3). Mirrors
/// <c>Raffa.Chat.Application.Capabilities.FeatureCitation.RaffaCorpus</c>'s own literal for
/// the fourth, feature-citation case — see <see cref="PackItem.Corpus"/>.
/// </summary>
public static class PackCorpus
{
    /// <summary>This tenant's own validated contracts (facts and clause chunks).</summary>
    public const string Tenant = "tenant";

    /// <summary>The shared, tenant-agnostic market-intelligence feed (benchmark bands, market
    /// notes) — never this tenant's own data (ADR-011 amendment).</summary>
    public const string Market = "market";

    /// <summary>A Raffa feature/capability citation (R-SYS-03) — same value as
    /// <see cref="Capabilities.FeatureCitation.RaffaCorpus"/>.</summary>
    public const string Raffa = "raffa";

    /// <summary>A value this engine's own calculators derived (a criticality score, a negotiation
    /// target) rather than read off a single tenant/market row — mirrors
    /// <c>Raffa.Insights.Contracts.InsightsCitationKeys.Calc</c>'s identical "name the
    /// computation" convention.</summary>
    public const string Calc = "calc";

    /// <summary>A public web source the research role actually read (ADR-030) — produced only by
    /// <c>Application.WebResearch.WebResearchComposer</c>, never by the composition root's pack
    /// builders, never mixed into an <c>answer</c>-role pack, never indexed. Always rendered as
    /// "unverified": nothing in this corpus was checked against the tenant's contracts.</summary>
    public const string Web = "web";
}

/// <summary>
/// One citable item in the context pack the composition root assembles for the `answer` role
/// (task E13/F06/US01/T01, ask-engine coding objective point 3; ADR-024 "context pack"). "The
/// pack, not the model, decides what is citable" (R-ASK-04): every claim the model is allowed to
/// make must trace back to one of these, and <c>Guards.GroundingGuard</c> rejects any citation key
/// the model invents that is not in this list.
///
/// <para>
/// <see cref="Raffa.Chat"/> owns only this DTO shape and <see cref="PackBudget"/> — assembling
/// real <see cref="PackItem"/> rows from the tenant store, the market feed and the calculators is
/// the composition root's job (<c>Raffa.Api.AskCopilotService</c>), the one project allowed to
/// reference every module (ADR-002).
/// </para>
/// </summary>
/// <param name="CitationKey">Stable key the model cites (as an entry of
/// <c>Raffa.AiGateway.Contracts.AiAnswerResult.CitationKeys</c>) and the answer's own inline
/// <c>[n]</c> markers resolve to, in pack order (`1` = the pack's first item, ...). Never
/// re-derived from the answer — always assigned by the composition root when the pack is built.</param>
/// <param name="Corpus">One of <see cref="PackCorpus"/>'s four values.</param>
/// <param name="Title">Human-readable citation-card title (R-WEB-04), e.g. <c>"Salesforce · MSA
/// 2024"</c>.</param>
/// <param name="Subtitle">Secondary line, e.g. <c>"p.12 §8.4"</c> or a market provenance label —
/// <see langword="null"/> when the title alone is enough.</param>
/// <param name="Page">Source page, when this item resolves to one specific tenant document page
/// (R-EVD-01: "citations resolve to Clause.SourcePage/SourceSpan when the hit is a clause, else to
/// the page"). Always <see langword="null"/> for <see cref="PackCorpus.Market"/>/
/// <see cref="PackCorpus.Raffa"/>/<see cref="PackCorpus.Calc"/> items.</param>
/// <param name="Section">Source section/clause label, when known — same scope as
/// <paramref name="Page"/>.</param>
/// <param name="Snippet">The citable text itself — a clause excerpt, a market note narrative, a
/// capability description, or a calculator's own explanation string.</param>
/// <param name="Href">Deep link for this item's own citation card — a document/contract route for
/// <see cref="PackCorpus.Tenant"/>, a capability route for <see cref="PackCorpus.Raffa"/>
/// (R-SYS-03), <see langword="null"/> for <see cref="PackCorpus.Market"/> (a market record opens a
/// side panel by id, not a route — R-EVD-02). <see cref="PackCorpus.Calc"/> items also carry a real
/// <c>/contracts/{id}</c> route when they are scoped to one contract (task E28/F03/US01/T01,
/// NW-83) — never the <see langword="null"/> this doc comment used to claim.</param>
/// <param name="PreviewUrl">First-page preview image route (R-DOC-08), when this item resolves to
/// a tenant document; <see langword="null"/> otherwise.</param>
/// <param name="RecordId">The market record id (R-EVD-02: "a market citation opens a side panel
/// with the record"), set only for <see cref="PackCorpus.Market"/> items.</param>
/// <param name="Provenance">Human-readable provenance string — a validated-contract note for
/// tenant items, <c>Raffa.Market.Contracts.MarketProvenance.Label</c>'s own "representative
/// market data · mock feed · updated ..." for market items (R-MKT-04), a fixed "Raffa feature"
/// label for raffa items, "deterministic calculator" for calc items. Never omitted — every pack
/// item must be able to say where it came from (Appendix C rule 2).</param>
/// <param name="Values">Normalized, guardable facts this item asserts (see
/// <see cref="PackValue"/>) — empty when this item is purely textual (a clause excerpt, a feature
/// description) with no number/date a numeric guard needs to check.</param>
/// <param name="ContractId">This item's own contract, when the composition root scoped it to
/// exactly one — every <see cref="PackCorpus.Tenant"/>/<see cref="PackCorpus.Calc"/> item built
/// from a named contract (task E28/F03/US01/T01, NW-83; ADR-024 w19 cl. 17 "no citation without a
/// pack source"). <see langword="null"/> for a cross-contract aggregate, a
/// <see cref="PackCorpus.Market"/> item, a <see cref="PackCorpus.Raffa"/> feature citation, or a
/// tenant hit from the NW-81 "similar types" peer slice, which must never be attributed to the
/// contract in scope (R-ASK-04). Echoed verbatim onto
/// <see cref="Raffa.Chat.Application.Reply.ReplyCitation.ContractId"/> — never re-derived from
/// <see cref="CitationKey"/>, which is an internal lookup token, not an id.</param>
/// <param name="DocumentId">This item's own source document, when the citation resolves to one
/// real tenant document — a clause's own <c>Contract360Clause.SourceDocumentId</c>, or an embedded
/// chunk whose own source is the whole document (<c>Embedding.SourceType == "Document"</c>, today's
/// only real indexing path — see <c>AskCopilotService.BuildClausePackItem</c>). <see langword="null"/>
/// for every other corpus and for a peer hit. Echoed verbatim onto
/// <see cref="Raffa.Chat.Application.Reply.ReplyCitation.DocumentId"/> so the Ask card can fetch
/// an authenticated page preview — never <see cref="CitationKey"/> itself, which used to stand in
/// for it.</param>
public sealed record PackItem(
    string CitationKey,
    string Corpus,
    string Title,
    string? Subtitle,
    int? Page,
    string? Section,
    string Snippet,
    string? Href,
    string? PreviewUrl,
    string? RecordId,
    string Provenance,
    IReadOnlyList<PackValue> Values,
    string? ContractId = null,
    string? DocumentId = null);
