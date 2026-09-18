using Raffa.Chat.Application.Pack;
using Raffa.Documents.Contracts.Application;
using Raffa.SharedKernel;

namespace Raffa.Api.Tests;

/// <summary>
/// Task E25/F02/US01/T01 (NW-55, us-01-citation-card-backend): unit-level proof for
/// <see cref="AskCopilotService.ResolveTenantClauseLinks"/> -- the helper
/// <c>BuildClausePackAsync</c> now calls to decide a tenant clause citation's
/// <see cref="PackItem.Href"/>/<see cref="PackItem.PreviewUrl"/>. Exercised directly (not through a
/// full <c>AskAsync</c>/HTTP round trip) because <c>InMemoryAskEngineFactory</c>'s own doc comment
/// records that <c>EmbeddingRetrievalService.SearchAsync</c>'s pgvector <c>CosineDistance</c> LINQ
/// does not translate under the EF Core InMemory provider this test project uses -- "a clause-intent
/// success path stays Raffa.IntegrationTests' job." <c>Raffa.Api/AssemblyInfo.cs</c>'s
/// <c>InternalsVisibleTo("Raffa.Api.Tests")</c> grant (the same one <c>WorkspaceRoleResolverTests</c>
/// already relies on) is what makes the internal <see cref="AskCopilotService"/> type and its
/// <see langword="internal"/> helper reachable here.
/// </summary>
public sealed class AskCopilotServiceTests
{
    // AC-1: "a tenant (corpus=tenant) clause citation carries a real previewUrl and an href
    // deep-linking to the viewer (/documents/:id/viewer?page&clause)".
    [Fact]
    public void Clause_with_a_known_document_and_page_resolves_the_viewer_deep_link_and_a_real_preview()
    {
        var documentId = new EntityId(Guid.NewGuid());
        var clauseId = new EntityId(Guid.NewGuid());
        var namedContractId = Guid.NewGuid();
        var clause = new Contract360Clause(
            ClauseId: clauseId,
            ClauseType: "Auto-renewal",
            RawText: "This Agreement shall automatically renew...",
            NormalizedValue: null,
            RiskLevel: null,
            SourceDocumentId: documentId,
            SourceSpan: "§8.4",
            SourcePage: 12,
            Confidence: 0.92);

        var (href, previewUrl) = AskCopilotService.ResolveTenantClauseLinks(clause, clauseId, namedContractId);

        Assert.Equal($"/documents/{documentId.Value}/viewer?page=12&clause={clauseId.Value}", href);
        Assert.Equal($"/api/documents/{documentId.Value}/preview?page=12", previewUrl);
    }

    // AC-3: "PackItem divides by corpus: PreviewUrl is set only for tenant pages" -- a clause that
    // resolved to no document/page (e.g. the hit's SourceType was not a Clause row at all) is not a
    // "page", so it keeps the pre-existing contract CTA and never fabricates a preview.
    [Fact]
    public void Clause_with_no_resolvable_document_falls_back_to_the_contract_route_with_no_preview()
    {
        var sourceId = new EntityId(Guid.NewGuid());
        var namedContractId = Guid.NewGuid();

        var (href, previewUrl) = AskCopilotService.ResolveTenantClauseLinks(null, sourceId, namedContractId);

        Assert.Equal($"/contracts/{namedContractId}", href);
        Assert.Null(previewUrl);
    }

    // A clause row that exists but was never page-anchored (SourcePage null, e.g. OCR-only text with
    // no page mapped yet) must not produce a half-built viewer link either -- same fallback as no
    // clause at all.
    [Fact]
    public void Clause_with_a_document_but_no_known_page_falls_back_to_the_contract_route_with_no_preview()
    {
        var documentId = new EntityId(Guid.NewGuid());
        var clauseId = new EntityId(Guid.NewGuid());
        var namedContractId = Guid.NewGuid();
        var clause = new Contract360Clause(
            ClauseId: clauseId,
            ClauseType: "Auto-renewal",
            RawText: "This Agreement shall automatically renew...",
            NormalizedValue: null,
            RiskLevel: null,
            SourceDocumentId: documentId,
            SourceSpan: null,
            SourcePage: null,
            Confidence: 0.92);

        var (href, previewUrl) = AskCopilotService.ResolveTenantClauseLinks(clause, clauseId, namedContractId);

        Assert.Equal($"/contracts/{namedContractId}", href);
        Assert.Null(previewUrl);
    }

    // No clause resolved and no named contract either (a portfolio-wide clause search) -- the
    // pre-existing "no href at all" shape must survive unchanged.
    [Fact]
    public void Clause_with_no_resolvable_document_and_no_named_contract_has_no_href_either()
    {
        var sourceId = new EntityId(Guid.NewGuid());

        var (href, previewUrl) = AskCopilotService.ResolveTenantClauseLinks(null, sourceId, null);

        Assert.Null(href);
        Assert.Null(previewUrl);
    }

    // AC-2: "a corpus=raffa/market citation carries a CTA href, never a page-preview previewUrl" --
    // PackItem itself (not just this file's construction sites) must be able to carry that shape; a
    // future PackCorpus.Raffa citation card is never handed a fabricated preview.
    [Fact]
    public void Raffa_corpus_pack_item_never_carries_a_page_preview()
    {
        var item = new PackItem(
            CitationKey: "capability:documents",
            Corpus: PackCorpus.Raffa,
            Title: "Documents",
            Subtitle: null,
            Page: null,
            Section: null,
            Snippet: "Upload contracts, review weak facts.",
            Href: "/documents",
            PreviewUrl: null,
            RecordId: null,
            Provenance: "Raffa feature",
            Values: []);

        Assert.Null(item.PreviewUrl);
        Assert.Equal("/documents", item.Href);
    }

    // ----- Task E28/F03/US01/T01 (NW-83; ADR-024 w19 cl. 17 "no citation without a pack source") -----
    // DoD: "a notice/clause citation carries a non-null contractId+documentId+page and a viewer
    // href". BuildClausePackItem (made internal for the same test-reachability reason as
    // ResolveTenantClauseLinks above) is exercised directly -- see that method's own doc comment.

    // Tier 1: the hit resolved to a real Clause row anchored to a document page.
    [Fact]
    public void Clause_pack_item_for_a_resolved_clause_stamps_real_contract_document_page_and_href()
    {
        var documentId = new EntityId(Guid.NewGuid());
        var clauseId = new EntityId(Guid.NewGuid());
        var namedContractId = Guid.NewGuid();
        var clause = new Contract360Clause(
            ClauseId: clauseId,
            ClauseType: "Auto-renewal",
            RawText: "This Agreement shall automatically renew...",
            NormalizedValue: null,
            RiskLevel: null,
            SourceDocumentId: documentId,
            SourceSpan: "§8.4",
            SourcePage: 12,
            Confidence: 0.92);
        var hit = new EmbeddingSearchResult(
            EmbeddingId: EntityId.New(),
            SourceType: "Clause",
            SourceId: clauseId,
            ChunkIndex: 0,
            ChunkText: "This Agreement shall automatically renew...",
            Distance: 0.1);

        var item = AskCopilotService.BuildClausePackItem(hit, clause, namedContractId, isPeer: false);

        Assert.Equal(namedContractId.ToString(), item.ContractId);
        Assert.Equal(documentId.Value.ToString(), item.DocumentId);
        Assert.Equal(12, item.Page);
        Assert.Equal($"/documents/{documentId.Value}/viewer?page=12&clause={clauseId.Value}", item.Href);
    }

    // Tier 2, and today's realistic shape: DocumentProcessingPipeline.IndexForRetrievalAsync indexes
    // every page under Embedding.SourceType == "Document" -- no clause-level embedding exists yet,
    // so no Clause row ever resolves for a real hit. Closes the "Document-sourced-chunk branch is
    // not attempted here" gap NW-55's own doc comment named.
    [Fact]
    public void Clause_pack_item_for_a_document_sourced_hit_still_stamps_a_real_document_page_and_viewer_href()
    {
        var documentId = new EntityId(Guid.NewGuid());
        var namedContractId = Guid.NewGuid();
        var hit = new EmbeddingSearchResult(
            EmbeddingId: EntityId.New(),
            SourceType: "Document",
            SourceId: documentId,
            ChunkIndex: 3,
            ChunkText: "Either party may terminate this Agreement upon 90 days written notice.",
            Distance: 0.2,
            Page: 4,
            Section: "9. Termination");

        var item = AskCopilotService.BuildClausePackItem(hit, clause: null, namedContractId, isPeer: false);

        Assert.Equal(namedContractId.ToString(), item.ContractId);
        Assert.Equal(documentId.Value.ToString(), item.DocumentId);
        Assert.Equal(4, item.Page);
        Assert.Equal("9. Termination", item.Section);
        Assert.Equal($"/documents/{documentId.Value}/viewer?page=4", item.Href);
        Assert.Equal($"/api/documents/{documentId.Value}/preview?page=4", item.PreviewUrl);
    }

    // NW-81's own peer-isolation rule (R-ASK-04), extended to ids: a "similar types" peer hit must
    // never be attributed to the contract in scope, or made clickable into its own document, even
    // when its own embedding is Document-sourced with a known page exactly like the test above.
    [Fact]
    public void Clause_pack_item_for_a_peer_hit_never_carries_an_id_or_href_even_when_document_sourced()
    {
        var documentId = new EntityId(Guid.NewGuid());
        var hit = new EmbeddingSearchResult(
            EmbeddingId: EntityId.New(),
            SourceType: "Document",
            SourceId: documentId,
            ChunkIndex: 1,
            ChunkText: "A similar-type contract's own clause text.",
            Distance: 0.3,
            Page: 2);

        var item = AskCopilotService.BuildClausePackItem(hit, clause: null, namedContractId: null, isPeer: true);

        Assert.Null(item.ContractId);
        Assert.Null(item.DocumentId);
        Assert.Null(item.Href);
        Assert.Null(item.PreviewUrl);
    }

    [Fact]
    public void Document_sourced_hit_with_a_known_page_resolves_the_viewer_and_a_real_preview()
    {
        var documentId = new EntityId(Guid.NewGuid());
        var namedContractId = Guid.NewGuid();

        var (href, previewUrl) = AskCopilotService.ResolveTenantClauseLinks(
            null, documentId, namedContractId, hitSourceType: "Document", hitPage: 2);

        Assert.Equal($"/documents/{documentId.Value}/viewer?page=2", href);
        Assert.Equal($"/api/documents/{documentId.Value}/preview?page=2", previewUrl);
    }
}
