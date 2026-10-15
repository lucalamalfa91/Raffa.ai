using Raffa.AiFlows.Shared.ContractContext;
using static Raffa.AiFlows.Ask.Interview.InterviewInputsFactory;
using static Raffa.AiFlows.Ask.Packs.ContractResolution;
using static Raffa.AiFlows.Ask.Packs.PackItemFactory;
using Raffa.AiFlows.Shared.Audit;
using Raffa.AiFlows.CapabilityGaps.Investigation;
using Raffa.AiFlows.Negotiation.DataCheck;
using Raffa.AiFlows.WebResearch.Orchestration;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Raffa.AiFlows.Ask.Answering;
using Raffa.AiFlows.Ask.Gate;
using Raffa.AiFlows.Ask.Interview;
using Raffa.AiFlows.Ask.Planning;
using Raffa.AiFlows.Ask.Reply;
using Raffa.AiFlows.Ask.Routing;
using Raffa.AiFlows.CapabilityGaps.Drafting;
using Raffa.AiFlows.Negotiation.Orchestration;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Planning;
using Raffa.AiFlows.Shared.Reply;
using Raffa.AiFlows.Shared.Routing;
using Raffa.AiGateway.Telemetry;
using Raffa.Benchmark;
using Raffa.Benchmark.Contracts;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Interview;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Application.WebResearch;
using Raffa.Chat.Domain;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.Insights.Application;
using Raffa.Insights.Contracts;
using Raffa.Insights.Criticality;
using Raffa.Insights.Strategy;
using Raffa.Market.Contracts;
using Raffa.Market.Retrieval;
using Raffa.Renewals.Application;
using Raffa.Savings.Application;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;
using Raffa.Suppliers.Products.Application;
using Raffa.AiFlows.Ask.Packs;
using Raffa.AiFlows.Ask.Orchestration;

namespace Raffa.AiFlows.Ask.Packs;

/// <summary>
/// The Clause intent's pack (scoped and tenant-wide clause RAG), the named-contract clause
/// excerpts and the tenant clause links. Moved verbatim from
/// <see cref="Raffa.AiFlows.Ask.Orchestration.AskCopilotService"/> (F1 Ask split, PR-2).
/// </summary>
public sealed class ClausePackBuilder(
    Contract360QueryService contract360QueryService,
    EmbeddingRetrievalService embeddingRetrievalService,
    ContractContextReader context)
{
    private const int ClauseTopK = 5;

    /// <summary>Task E28/F02/US01/T01 (NW-81 AC-2 "lower K"): the labelled "similar types" peer
    /// slice always asks for fewer rows than <see cref="ClauseTopK"/> -- it is corroborating
    /// evidence from a different, merely similar-type contract, never this contract's own primary
    /// evidence.</summary>
    private const int ClausePeerTopK = 2;

    /// <summary>
    /// Task E28/F02/US01/T01 (NW-81; ADR-024 w19 cl. 15; parent story us-01-rag-contract-filter
    /// AC-1/AC-2/AC-3): when a contract is named, this used to call
    /// <see cref="EmbeddingRetrievalService.SearchAsync"/> -- a cosine top-K over <b>every</b>
    /// embedding this tenant owns -- and relied on <paramref name="namedContractItem"/> only to
    /// resolve a citation's clause/link, never to filter the search itself. That is the exact NW-81
    /// defect: a tenant with 37 contracts could get another supplier's MSA back for a question about
    /// one named contract (Q2/Q3 "do not mix 37 contracts"). This now calls
    /// <see cref="EmbeddingRetrievalService.SearchByContractAsync"/>, which returns two slices --
    /// this contract's own chunks, and a lower-K, separately labelled "similar types" peer slice
    /// from other validated contracts of the same
    /// <see cref="Raffa.Documents.Contracts.Domain.ContractDocumentType"/> (AC-1/AC-2) --
    /// and builds the pack from those instead. Market notes are never reached from here at all
    /// (AC-3): <see cref="IMarketKnowledgeRetrieval"/> is only ever called from
    /// <see cref="MarketComparePackBuilder.BuildMarketComparePackAsync"/>, a different intent branch.
    /// </summary>
    internal async Task<IReadOnlyList<PackItem>> BuildClausePackAsync(
        TenantId tenantId, string question, PortfolioListItem? namedContractItem, CancellationToken cancellationToken)
    {
        if (namedContractItem is null)
        {
            // No contract is in scope for this turn (a fully generic clause question) -- there is
            // no "this contract" or "similar types" to split against, so this is the one path that
            // still searches the whole tenant corpus. ADR-024 w19's own NW-79 amendment still names
            // this the "unscoped Clause RAG" fallback; NW-81 only requires filtering a turn that
            // already names a contract.
            try
            {
                var tenantWideResult = await embeddingRetrievalService
                    .SearchAsync(tenantId, question, ClauseTopK, cancellationToken)
                    .ConfigureAwait(false);

                return tenantWideResult.IsFailure
                    ? []
                    : tenantWideResult.Value
                        .Select(hit => BuildClausePackItem(hit, clause: null, namedContractId: null, isPeer: false))
                        .ToList();
            }
            catch (InvalidOperationException)
            {
                // EF InMemory cannot translate Vector.CosineDistance (see InMemoryAskEngineFactory).
                return [];
            }
        }

        var contractId = new EntityId(namedContractItem.ContractId);

        Result<EmbeddingContractScopedSearchResult> searchResult;
        try
        {
            searchResult = await embeddingRetrievalService
                .SearchByContractAsync(
                    new EmbeddingSearchQuery(tenantId, question, ClauseTopK, contractId, ClausePeerTopK),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // EF InMemory cannot translate Vector.CosineDistance (see InMemoryAskEngineFactory).
            return [];
        }

        if (searchResult.IsFailure)
        {
            return [];
        }

        var contract360 = await contract360QueryService
            .GetByIdAsync(tenantId, contractId, cancellationToken)
            .ConfigureAwait(false);

        var items = new List<PackItem>();

        foreach (var hit in searchResult.Value.ThisContract)
        {
            var clause = contract360?.Clauses.FirstOrDefault(c => c.ClauseId == hit.SourceId);
            items.Add(BuildClausePackItem(hit, clause, namedContractItem.ContractId, isPeer: false));
        }

        // AC-2: labelled separately, and never resolved against this contract's own Contract360
        // clauses or link -- a peer hit belongs to a different, unresolved contract by construction
        // (SearchByContractAsync's own peer slice already excludes contractId itself), so it must
        // never be citable as if it were this contract's own evidence (R-ASK-04).
        foreach (var hit in searchResult.Value.SimilarTypes)
        {
            items.Add(BuildClausePackItem(hit, clause: null, namedContractId: null, isPeer: true));
        }

        return items;
    }

    /// <summary>
    /// One clause-pack <see cref="PackItem"/> from a raw <see cref="EmbeddingSearchResult"/> hit
    /// (task E28/F02/US01/T01, NW-81 AC-1/AC-2). <paramref name="isPeer"/> marks a hit from the
    /// "similar types" peer slice: its title and provenance are labelled as a different, merely
    /// similar-type contract so neither the model nor a reader can mistake it for
    /// <paramref name="namedContractId"/>'s own evidence -- callers pass <see langword="null"/> for
    /// both <paramref name="clause"/> and <paramref name="namedContractId"/> on a peer hit, which
    /// also (via <see cref="ResolveTenantClauseLinks"/>'s own fallback) leaves it with no href: a
    /// peer citation names a fact pattern, never a clickable route into a contract the pack never
    /// resolved.
    ///
    /// <para>
    /// <b>Real ids, not the citation key (task E28/F03/US01/T01, NW-83; ADR-024 w19 cl. 17 "no
    /// citation without a pack source")</b>: <c>ContractId</c> is <paramref name="namedContractId"/>
    /// verbatim -- already <see langword="null"/> for a peer or an unscoped (tenant-wide) hit, so
    /// this never re-derives it. <c>DocumentId</c> is the resolved clause's own
    /// <see cref="Contract360Clause.SourceDocumentId"/>, or -- closing the gap
    /// <see cref="ResolveTenantClauseLinks"/>'s own doc comment used to name -- the hit's own source
    /// id when <see cref="EmbeddingSearchResult.SourceType"/> is <c>"Document"</c>, which is what
    /// <c>DocumentProcessingPipeline.IndexForRetrievalAsync</c> stamps on <em>every</em> chunk it
    /// indexes today (no clause-level embedding exists yet, so this is the common case, not the
    /// edge case). Both are peer-gated through the local <c>hitSourceType</c>/<c>hitPage</c>
    /// variables below, not just <paramref name="namedContractId"/> -- a peer's own document is a
    /// real, resolvable id and would otherwise leak through this fallback despite the href already
    /// being suppressed for it.
    /// </para>
    ///
    /// <para>
    /// <c>internal</c>, not <c>private</c> -- the same test-reachability precedent
    /// <see cref="ResolveTenantClauseLinks"/> already establishes: <c>Raffa.Api.Tests</c> asserts
    /// directly on the returned <see cref="PackItem"/>'s <c>ContractId</c>/<c>DocumentId</c>/
    /// <c>Page</c>/<c>Href</c> (task E28/F03/US01/T01's own Definition of Done line) without first
    /// standing up a full <c>AskAsync</c>/HTTP round trip through a real embedding search.
    /// </para>
    /// </summary>
    internal static PackItem BuildClausePackItem(
        EmbeddingSearchResult hit, Contract360Clause? clause, Guid? namedContractId, bool isPeer)
    {
        // NW-81's own peer-isolation rule, extended to ids: gating the raw hit fields here (not
        // just namedContractId, already null for a peer at the call site) is what stops
        // ResolveTenantClauseLinks' Document-sourced tier from resolving a peer's own document into
        // a clickable href, and DocumentId below from resolving it into an id, either.
        var hitSourceType = isPeer ? null : hit.SourceType;
        var hitPage = isPeer ? null : hit.Page;

        var (href, previewUrl) = ResolveTenantClauseLinks(clause, hit.SourceId, namedContractId, hitSourceType, hitPage);

        var baseTitle = clause is not null ? $"{clause.ClauseType} clause" : $"{hit.SourceType} excerpt";
        var subtitle = clause?.SourcePage is { } page
            ? $"p.{page}" + (clause.SourceSpan is { } span ? $" §{span}" : string.Empty)
            : null;

        var documentId = clause?.SourceDocumentId?.Value.ToString()
            ?? (string.Equals(hitSourceType, "Document", StringComparison.Ordinal) ? hit.SourceId.Value.ToString() : null);

        return new PackItem(
            $"fact:{hit.SourceId}:chunk[{hit.ChunkIndex}]",
            PackCorpus.Tenant,
            isPeer ? $"Similar contract — {baseTitle}" : baseTitle,
            isPeer ? "similar contract, not this one" : subtitle,
            clause?.SourcePage ?? hit.Page,
            clause?.SourceSpan ?? hit.Section ?? $"chunk {hit.ChunkIndex}",
            hit.ChunkText,
            href,
            previewUrl,
            null,
            isPeer ? "similar validated contract" : "validated contract",
            [],
            namedContractId?.ToString(),
            documentId);
    }

    /// <summary>
    /// Task E25/F02/US01/T01 (NW-55; ADR-024; ADR-018 w17 clause 9 viewer route): the tenant
    /// citation card's real deep-link and page preview -- <c>/documents/:documentId/viewer?page=
    /// &amp;clause=</c> and the existing <c>/api/documents/{id}/preview?page=</c> route -- replacing
    /// the bare <c>/contracts/{id}</c> CTA whenever this hit actually resolves to one real page of
    /// one real document. Same shape, and the same fallback rule, as the Contract 360 client's own
    /// <c>contract360ViewModel.ts</c> <c>resolveViewerHref</c> ("No document or no sourcePage -&gt;
    /// no link"): only when <paramref name="clause"/> carries both a
    /// <see cref="Contract360Clause.SourceDocumentId"/> and a <see cref="Contract360Clause.SourcePage"/>
    /// does this return the viewer pair.
    ///
    /// <para>
    /// <b>Three tiers (task E28/F03/US01/T01, NW-83; parent story us-01-citation-ids-backend AC-2)</b>:
    /// (1) <paramref name="clause"/> resolves to a real document page -- the viewer, clause
    /// included, exactly as before this task. (2) No <c>Clause</c> row, but
    /// <paramref name="hitSourceType"/> is <c>"Document"</c> and <paramref name="hitPage"/> is known
    /// -- the same viewer, page only (no clause to highlight). This is the honest gap this method's
    /// own doc comment used to name ("the Document-sourced-chunk branch is not attempted here") --
    /// closed now because it is, today, the <em>only</em> shape a real indexed chunk has
    /// (<c>DocumentProcessingPipeline.IndexForRetrievalAsync</c> indexes every page under
    /// <c>Embedding.SourceType == "Document"</c>; no clause-level embedding exists yet). (3) Neither
    /// resolves -- the pre-existing <c>/contracts/{id}</c> CTA, now carrying whichever locator
    /// survives (a clause id we know but could not look up, else a known page) so the 360 screen can
    /// still land the reader near the right evidence -- never a preview (AC-3: previewUrl is tenant
    /// <em>pages</em> only).
    /// </para>
    /// </summary>
    /// <param name="sourceId">The cited chunk's own source id (<see cref="EmbeddingSearchResult.SourceId"/>)
    /// -- the clause id when <paramref name="clause"/> resolved it, else the hit's own source id --
    /// echoed into the viewer's optional <c>?clause=</c> query parameter (ADR-018) or (tier 2) the
    /// viewer route itself.</param>
    /// <param name="namedContractId">The named contract's id, when the caller asked about one
    /// contract by name -- the tier-3 fallback CTA target.</param>
    /// <param name="hitSourceType">The raw hit's own <see cref="EmbeddingSearchResult.SourceType"/>
    /// ("Document"/"Clause"), or <see langword="null"/> when the caller withholds it (a peer hit, or
    /// a pre-NW-83 caller) -- defaulted so every existing call site keeps compiling unchanged.</param>
    /// <param name="hitPage">The raw hit's own <see cref="EmbeddingSearchResult.Page"/>, under the
    /// same withholding rule as <paramref name="hitSourceType"/>.</param>
    internal static (string? Href, string? PreviewUrl) ResolveTenantClauseLinks(
        Contract360Clause? clause, EntityId sourceId, Guid? namedContractId,
        string? hitSourceType = null, int? hitPage = null)
    {
        // Tier 1: a real Clause row anchored to a document page (AC-1/AC-2) -- unchanged from
        // NW-55.
        if (clause is { SourceDocumentId: { } sourceDocumentId, SourcePage: { } sourcePage })
        {
            return (
                $"/documents/{sourceDocumentId.Value}/viewer?page={sourcePage}&clause={sourceId.Value}",
                $"/api/documents/{sourceDocumentId.Value}/preview?page={sourcePage}");
        }

        // Tier 2 ("else viewer ?page= + evidence highlight"): a Document-sourced chunk with a known
        // page -- today's common real-indexing shape (see this method's own doc comment).
        if (clause is null && hitPage is { } page && string.Equals(hitSourceType, "Document", StringComparison.Ordinal))
        {
            return (
                $"/documents/{sourceId.Value}/viewer?page={page}",
                $"/api/documents/{sourceId.Value}/preview?page={page}");
        }

        // Tier 3 ("else 360 ?clause=/?page="): neither resolves -- fall back to the contract route,
        // carrying whichever locator survives.
        if (namedContractId is not { } contractId)
        {
            return (null, null);
        }

        if (clause is null && string.Equals(hitSourceType, "Clause", StringComparison.Ordinal))
        {
            return ($"/contracts/{contractId}?clause={sourceId.Value}", null);
        }

        return (hitPage is { } fallbackPage ? $"/contracts/{contractId}?page={fallbackPage}" : $"/contracts/{contractId}", null);
    }

    /// <summary>
    /// Page excerpts for a named-contract structured fact card, taken from already-extracted
    /// <see cref="Contract360Result.Clauses"/> — the actual document sentence, not the portfolio
    /// paraphrase. Does not call embedding search (InMemory CosineDistance cannot translate).
    /// </summary>
    internal async Task<IReadOnlyList<PackItem>> BuildNamedContractExcerptItemsAsync(
        PortfolioListItem namedContractItem, CancellationToken cancellationToken)
    {
        var contract360 = await context.GetContract360Async(namedContractItem.ContractId, cancellationToken).ConfigureAwait(false);

        if (contract360 is null)
        {
            return [];
        }

        var items = new List<PackItem>();
        foreach (var clause in contract360.Clauses)
        {
            if (string.IsNullOrWhiteSpace(clause.RawText) || clause.SourceDocumentId is null)
            {
                continue;
            }

            var (href, previewUrl) = ResolveTenantClauseLinks(
                clause, clause.ClauseId, namedContractItem.ContractId);
            var page = clause.SourcePage;
            var subtitle = page is { } knownPage
                ? $"p.{knownPage}" + (clause.SourceSpan is { } span ? $" §{span}" : string.Empty)
                : clause.SourceSpan;

            items.Add(new PackItem(
                $"fact:{clause.ClauseId}:clause",
                PackCorpus.Tenant,
                $"{clause.ClauseType} clause",
                subtitle,
                page,
                clause.SourceSpan,
                clause.RawText,
                href,
                previewUrl,
                null,
                "validated contract",
                [],
                namedContractItem.ContractId.ToString(),
                clause.SourceDocumentId.Value.ToString()));
        }

        return items;
    }
}
