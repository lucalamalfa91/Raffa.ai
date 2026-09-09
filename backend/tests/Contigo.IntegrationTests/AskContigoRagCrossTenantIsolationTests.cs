using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Contigo.AiGateway;
using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Contracts;
using Contigo.AiGateway.Fixtures;
using Contigo.Documents.Contracts.Application;
using Contigo.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Contigo.IntegrationTests;

/// <summary>
/// Proves the Definition of Done for task E13/F06/US01/T01 (ask-engine) and its parent story
/// us-01-ask-engine AC-9: "Another tenant's contracts never appear in evidence (existing isolation
/// test extended to the messages endpoint)" — end to end, through the real
/// `POST /api/chat/query` alias and `POST /api/conversations/{id}/messages`
/// (<c>Contigo.Api.ChatEndpointExtensions</c>/<c>ConversationsEndpointExtensions</c>, both
/// delegating into <c>Contigo.Api.AskCopilotService</c>), against a real
/// Postgres+pgvector+RLS Testcontainer (<see cref="R0IntegrationFixture"/>, extended by this task
/// to also migrate Contigo.Chat's and Contigo.Suppliers.Products' own tables — see that fixture's
/// own doc comment) — the same "one real host, no hand-rolled container" shape
/// <see cref="R0CrossTenantIsolationTests"/> already uses for the R0 path.
///
/// <b>Question choice</b>: every question below deliberately names no capitalized supplier token
/// ("what liability coverage do we have on file" — lower-case, matching this suite's own house
/// style) so <c>Contigo.Chat.Application.Gate.DomainGate</c>'s own supplier-name check never fires
/// — this test seeds raw tenant-scoped embedding chunks directly (the same shape
/// <c>Contigo.Documents.Contracts.Tests.EmbeddingRetrievalServiceTests</c> already covers on its
/// own), not a full Contract/Supplier row, so a named-supplier question would incorrectly resolve
/// `needs_document` instead of reaching retrieval — proving nothing about isolation.
///
/// <b>AC-1</b> (auth-before-retrieval) / <b>AC-3</b> (unauthorized documents never enter the LLM
/// context): tenant B's indexed content is never returned for tenant A's turn. This holds even
/// though <c>Contigo.AiGateway.Fixtures.FixtureAiGateway</c>'s embeddings are SHA-256 pseudo-vectors
/// with no real semantic ordering (see that type's own doc comment) — tenant isolation comes from
/// <c>EmbeddingRetrievalService.SearchAsync</c>'s own <c>tenant_id</c> predicate plus the
/// `embedding` table's RLS policy, never from vector quality, so the proof does not depend on the
/// fixture gateway becoming smarter later.
///
/// <b>AC-2</b> (citations, or an explicit abstain): a tenant with indexed content gets citations
/// pointing only at its own seeded evidence; a tenant with nothing indexed gets an honest
/// `kind: "abstain"` (spec §8.4 "no evidence, no claim") rather than a fabricated answer.
/// </summary>
public sealed class AskContigoRagCrossTenantIsolationTests : IClassFixture<R0IntegrationFixture>
{
    private readonly R0IntegrationFixture _fixture;

    public AskContigoRagCrossTenantIsolationTests(R0IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Each_tenant_only_ever_sees_its_own_citations()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var tenantADocumentId = EntityId.New();
        var tenantBDocumentId = EntityId.New();

        await IndexChunkAsync(
            tenantA, "Document", tenantADocumentId,
            "Tenant A's liability cap is CHF 1000000 under the AWS master services agreement.");
        await IndexChunkAsync(
            tenantB, "Document", tenantBDocumentId,
            "Tenant B's liability cap is CHF 2000000 under its own AWS master services agreement.");

        var client = _fixture.CreateClient();

        var tenantABody = await ParseAsync(await PostChatQueryAsync(client, tenantA, "alice@tenant-a.example"));
        Assert.Equal("answer", tenantABody.GetProperty("kind").GetString());

        var tenantACitations = tenantABody.GetProperty("citations").EnumerateArray().ToList();
        Assert.NotEmpty(tenantACitations);
        Assert.All(
            tenantACitations,
            citation => Assert.Contains(tenantADocumentId.ToString(), citation.GetProperty("documentId").GetString()));
        Assert.DoesNotContain(
            tenantACitations,
            citation => citation.GetProperty("documentId").GetString()!.Contains(tenantBDocumentId.ToString()));

        // Other direction — proves the isolation above is real, not a coincidence of seed order.
        var tenantBBody = await ParseAsync(await PostChatQueryAsync(client, tenantB, "bob@tenant-b.example"));
        var tenantBCitations = tenantBBody.GetProperty("citations").EnumerateArray().ToList();
        Assert.NotEmpty(tenantBCitations);
        Assert.All(
            tenantBCitations,
            citation => Assert.Contains(tenantBDocumentId.ToString(), citation.GetProperty("documentId").GetString()));
        Assert.DoesNotContain(
            tenantBCitations,
            citation => citation.GetProperty("documentId").GetString()!.Contains(tenantADocumentId.ToString()));
    }

    [Fact]
    public async Task A_tenant_with_no_indexed_content_gets_an_honest_abstain_not_a_fabricated_answer()
    {
        var emptyTenant = TenantId.New();
        var client = _fixture.CreateClient();

        var response = await PostChatQueryAsync(client, emptyTenant, "carol@empty-tenant.example");

        var body = await ParseAsync(response);

        Assert.Equal("abstain", body.GetProperty("kind").GetString());
        Assert.Equal(0, body.GetProperty("citations").GetArrayLength());
    }

    /// <summary>
    /// Parent story AC-9's own "extended to the messages endpoint" — the identical proof as
    /// <see cref="Each_tenant_only_ever_sees_its_own_citations"/>, but through
    /// `POST /api/conversations` + `POST /api/conversations/{id}/messages` (not the
    /// `POST /api/chat/query` alias both of that test's calls already exercise via
    /// <c>ChatEndpointExtensions</c>'s own delegation into the identical
    /// <c>ConversationsEndpointExtensions.AskAndAppendAsync</c> implementation).
    /// </summary>
    [Fact]
    public async Task Messages_endpoint_extends_the_same_cross_tenant_isolation()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var tenantADocumentId = EntityId.New();
        var tenantBDocumentId = EntityId.New();

        await IndexChunkAsync(
            tenantA, "Document", tenantADocumentId,
            "Tenant A's notice period is 90 days under its own master services agreement.");
        await IndexChunkAsync(
            tenantB, "Document", tenantBDocumentId,
            "Tenant B's notice period is 60 days under its own master services agreement.");

        var client = _fixture.CreateClient();

        var conversationId = await CreateConversationAsync(client, tenantA, "alice@tenant-a.example");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{conversationId}/messages")
        {
            Content = JsonContent.Create(new { question = "what notice period do we have on file" }),
        };
        request.Headers.Add("X-Tenant-Id", tenantA.Value.ToString());
        request.Headers.Add("X-User-Id", "alice@tenant-a.example");

        var response = await client.SendAsync(request);
        var body = await ParseAsync(response);

        Assert.Equal("answer", body.GetProperty("kind").GetString());
        var citations = body.GetProperty("citations").EnumerateArray().ToList();
        Assert.NotEmpty(citations);
        Assert.All(
            citations,
            citation => Assert.Contains(tenantADocumentId.ToString(), citation.GetProperty("documentId").GetString()));
        Assert.DoesNotContain(
            citations,
            citation => citation.GetProperty("documentId").GetString()!.Contains(tenantBDocumentId.ToString()));
    }

    /// <summary>
    /// Parent story AC-7: "regenerated once, then downgraded to abstain with the pack's own facts;
    /// audit records <c>abstainGuardIntervened=true</c>" — the one gap the review of this task's
    /// first pass found unproven: <c>Contigo.Api.AskCopilotService.WriteAuditAsync</c> computed
    /// <c>Contigo.Chat.Application.Answering.AnswerComposerResult.GuardIntervened</c> correctly but
    /// never read it, so every <c>chat.abstained</c> row was indistinguishable from an empty-pack or
    /// gateway-failure abstain. No test elsewhere in this task's own file set could have caught
    /// this: every other in-domain answer here (and every <c>Contigo.Chat.Tests/Guards/*</c> unit
    /// test) drives <c>Contigo.AiGateway.Fixtures.FixtureAiGateway.AnswerFromPack</c>'s real,
    /// deterministic echo, which copies pack values verbatim and therefore can never itself violate
    /// <c>Contigo.Chat.Application.Guards.GroundingGuard</c>/<c>NumericGuard</c> — so this test
    /// wires in <see cref="GuardViolatingAiGateway"/>, a thin decorator (same shape as
    /// <see cref="ScriptedR1AiGateway"/>: delegate every role to the real fixture, override one)
    /// that rewrites a pack-grounded answer's own citation key to one no pack item has, guaranteed
    /// to fail grounding on both the first attempt and <c>Guards.RegenerateOnce</c>'s one retry —
    /// the real end-to-end "regenerate once, then downgrade" path, not a unit-level stand-in.
    /// </summary>
    [Fact]
    public async Task Guard_intervention_on_the_in_domain_path_is_recorded_in_the_audit_entry()
    {
        var tenantId = TenantId.New();
        var documentId = EntityId.New();

        await IndexChunkAsync(
            tenantId, "Document", documentId,
            "The notice period is 45 days under this agreement.");

        var recordingAuditWriter = new RecordingAuditWriter();

        var client = _fixture.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                // Same "append an AddSingleton override after the host's own registration" shape
                // R1IntegrationFixture already uses for IAiGateway — the last registration wins for
                // a single GetRequiredService<IAiGateway>() call.
                services.AddSingleton<IAiGateway>(
                    new GuardViolatingAiGateway(
                        new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions())));

                // Swaps the real Postgres-backed AuditWriter for an in-memory spy so this test can
                // assert on the exact Detail string a real run would have persisted, without also
                // re-deriving RLS/tenant-scope plumbing just to read one row back (same rationale
                // Contigo.Chat.Tests.TestSupport.RecordingAuditWriter's own doc comment gives).
                services.AddSingleton<IAuditWriter>(recordingAuditWriter);
            });
        }).CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/query")
        {
            Content = JsonContent.Create(new { question = "what notice period do we have on file" }),
        };
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        request.Headers.Add("X-User-Id", "dave@guard-test.example");

        var response = await client.SendAsync(request);
        var body = await ParseAsync(response);

        // The end-user-visible half of AC-7: an honest abstain, never the ungrounded citation key
        // GuardViolatingAiGateway injected.
        Assert.Equal("abstain", body.GetProperty("kind").GetString());
        Assert.Equal(0, body.GetProperty("citations").GetArrayLength());

        // The audit-visible half of AC-7 — this is the assertion the prior pass's review found
        // nothing could prove. `/api/chat/query` is a thin alias that also creates a conversation
        // and appends both messages (task coding objective point 8), so recordingAuditWriter holds
        // more than this one row (conversation.created, two conversation.message.appended) —
        // narrowed to the one AskCopilotService itself writes.
        var auditEntry = Assert.Single(
            recordingAuditWriter.Written,
            entry => entry.TenantId == tenantId && entry.Action == "chat.abstained");
        Assert.Contains("abstainGuardIntervened=True", auditEntry.Detail);
    }

    /// <summary>
    /// Seeds one embedded chunk directly through the real <see cref="EmbeddingRetrievalService"/>
    /// (resolved from the host's own DI container, not a hand-rolled one) rather than over HTTP:
    /// indexing is not an HTTP-exposed capability in this wave (see `backend/README.md`'s "Ask
    /// Contigo" section) — this test proves the read/answer side, not the write/indexing side,
    /// which <c>Contigo.Documents.Contracts.Tests.EmbeddingRetrievalServiceTests</c> already covers
    /// on its own.
    /// </summary>
    private async Task IndexChunkAsync(TenantId tenantId, string sourceType, EntityId sourceId, string chunkText)
    {
        using var scope = _fixture.Services.CreateScope();
        var embeddingRetrievalService = scope.ServiceProvider.GetRequiredService<EmbeddingRetrievalService>();

        var result = await embeddingRetrievalService.IndexChunkAsync(tenantId, sourceType, sourceId, 0, chunkText);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
    }

    private static async Task<HttpResponseMessage> PostChatQueryAsync(HttpClient client, TenantId tenantId, string userId)
    {
        // Must await inside this `using` block (not return the un-awaited Task): disposing
        // `request` before SendAsync's work actually completes would race its Content stream.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/query")
        {
            Content = JsonContent.Create(new { question = "what liability coverage do we have on file" }),
        };
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        request.Headers.Add("X-User-Id", userId);
        return await client.SendAsync(request);
    }

    private static async Task<Guid> CreateConversationAsync(HttpClient client, TenantId tenantId, string userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/conversations")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        request.Headers.Add("X-User-Id", userId);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> ParseAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Test-only <see cref="IAiGateway"/> decorator for
    /// <see cref="Guard_intervention_on_the_in_domain_path_is_recorded_in_the_audit_entry"/> — see
    /// that test's own doc comment for why the real <see cref="FixtureAiGateway"/> alone can never
    /// exercise <c>Guards.RegenerateOnce</c>'s retry-then-downgrade path. Delegates every role to
    /// <paramref name="inner"/> unchanged (the same "compose, override one role" shape
    /// <see cref="ScriptedR1AiGateway"/> already establishes) and only rewrites a pack-grounded,
    /// <c>CanDetermine=true</c> answer's own <see cref="AiAnswerResult.CitationKeys"/> to a key no
    /// pack item actually has — <c>Contigo.Chat.AbstainGuard.Enforce(AiAnswerResult,
    /// IReadOnlyList&lt;PackItem&gt;)</c> fails any such key deterministically, on both the first
    /// attempt and the retry (this decorator does not distinguish the two — it always rewrites),
    /// which is exactly the "even the retry still violates a guard" branch
    /// <c>Guards.RegenerateOnce.DowngradeToAbstain</c> exists for.
    /// </summary>
    private sealed class GuardViolatingAiGateway(IAiGateway inner) : IAiGateway
    {
        private const string UngroundedCitationKey = "guard-test:not-in-pack";

        public Task<Result<AiClassificationResult>> ClassifyAsync(
            AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            inner.ClassifyAsync(request, cancellationToken);

        public Task<Result<AiExtractionResult>> ExtractAsync(
            AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            inner.ExtractAsync(request, cancellationToken);

        public Task<Result<AiEmbeddingResult>> EmbedAsync(
            AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            inner.EmbedAsync(request, cancellationToken);

        public async Task<Result<AiAnswerResult>> AnswerAsync(
            AiAnswerRequest request, CancellationToken cancellationToken = default)
        {
            var result = await inner.AnswerAsync(request, cancellationToken).ConfigureAwait(false);

            if (result.IsFailure || string.IsNullOrWhiteSpace(request.PackJson) || !result.Value.CanDetermine)
            {
                // Nothing to sabotage: a legacy evidence-only call, or the pack was already empty
                // (the fixture's own honest "nothing to answer from" abstain) — unrelated to the
                // guard-intervention path this decorator exists to force.
                return result;
            }

            return Result<AiAnswerResult>.Success(result.Value with { CitationKeys = [UngroundedCitationKey] });
        }

        public Task<Result<AiOcrResult>> OcrAsync(
            AiOcrRequest request, CancellationToken cancellationToken = default) =>
            inner.OcrAsync(request, cancellationToken);
    }

    /// <summary>
    /// Fake <see cref="IAuditWriter"/> that records every entry written — same shape and name as
    /// <c>Contigo.Chat.Tests.TestSupport.RecordingAuditWriter</c> (that project's own doc comment:
    /// "a lightweight in-memory spy is enough to prove [the caller] writes the right entry without
    /// standing up a real Postgres-backed AuditWriter"), declared locally here rather than shared
    /// across test projects since <c>Contigo.IntegrationTests</c> does not reference either of the
    /// other projects' <c>TestSupport</c> folders.
    /// </summary>
    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Written { get; } = [];

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Written.Add(entry);
            return Task.CompletedTask;
        }
    }
}
