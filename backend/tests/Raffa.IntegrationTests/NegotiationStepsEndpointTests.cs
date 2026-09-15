using System.Net;
using System.Net.Http.Json;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.IntegrationTests;

/// <summary>
/// Proves the Definition of Done for task E19/F03/US01/T01 (us-01-step-ticks-api) at the HTTP
/// layer: the <c>ICallerContext</c> ladder (401 / 400 / 404 unknown-contract / 404 non-member /
/// 200) on <c>GET</c>/<c>PUT /api/contracts/{id}/negotiation-steps</c>, the whole-set <c>PUT</c>'s
/// idempotent semantics, and the cross-tenant negative ADR-009 w16 clause 2c requires. Reuses
/// <see cref="R4IntegrationFixture"/> — it already migrates
/// <see cref="Raffa.Identity.Workspace.Infrastructure.IdentityWorkspaceDbContext"/> (membership,
/// for <see cref="ImplicitTenantAdminStartupFilter"/>) and <see cref="DocumentsContractsDbContext"/>
/// against a real, unprivileged-role Postgres connection — the same "one real host, no hand-rolled
/// container" shape <see cref="R4CrossTenantIsolationTests"/> already reuses it for. A distinct
/// fixture is not warranted here: this task adds no new module dependency, so there is nothing a
/// fresh fixture would migrate that this one does not already.
/// </summary>
public sealed class NegotiationStepsEndpointTests : IClassFixture<R4IntegrationFixture>
{
    private readonly R4IntegrationFixture _fixture;

    public NegotiationStepsEndpointTests(R4IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    private static string StepsUrl(Guid contractId) => $"/api/contracts/{contractId}/negotiation-steps";

    /// <summary>Seeds a <see cref="Contract"/> row directly through the fixture's own DI container —
    /// this module has no "create contract" HTTP writer (extraction is the only path), the same gap
    /// <c>Raffa.Documents.Contracts.Tests.ContractCorrectionServiceTests.SeedContractAsync</c>'s own
    /// doc comment documents; a directly-seeded row stands in for "whatever the AI extraction
    /// originally wrote", which is all a negotiation-step tick needs to attach to.</summary>
    private static async Task<Guid> SeedContractAsync(R4IntegrationFixture fixture, Guid tenantId)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var tenant = new TenantId(tenantId);
        using var tenantScope = tenantContext.BeginScope(tenant);

        var contract = new Contract
        {
            TenantId = tenant,
            Type = ContractDocumentType.Msa,
            Status = "needs_review",
            Currency = "USD",
            AnnualSpend = 100_000m,
            AutoRenewal = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Contracts.Add(contract);
        await db.SaveChangesAsync();
        return contract.Id.Value;
    }

    /// <summary>No <see cref="R1EndToEndTests"/> overload covers <c>PUT</c> yet (no prior task
    /// needed one) — same shape as that class's own <c>PatchAsync</c>: <c>X-Tenant-Id</c> only, so
    /// <see cref="ImplicitTenantAdminStartupFilter"/> grants the implicit Admin membership a
    /// "member" scenario needs with no explicit seed.</summary>
    private static async Task<HttpResponseMessage> PutStepsAsync(
        HttpClient client, Guid contractId, Guid tenantId, IEnumerable<string> steps)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, StepsUrl(contractId))
        {
            Content = JsonContent.Create(new { steps }),
        };
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Ladder_no_token_401_non_guid_id_400_unknown_contract_404_non_member_404_member_200()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();
        var contractId = await SeedContractAsync(_fixture, tenantId);

        // 401: no X-User-Id and no X-Tenant-Id at all -- ICallerIdentity.Resolve() finds nobody
        // before the tenant header is ever looked at (ImplicitTenantAdminStartupFilter's own doc
        // comment: "a request with no tenant header is left alone", so this stays genuinely
        // anonymous, not auto-admitted).
        using (var noAuthRequest = new HttpRequestMessage(HttpMethod.Get, StepsUrl(contractId)))
        {
            var noAuthResponse = await client.SendAsync(noAuthRequest);
            Assert.Equal(HttpStatusCode.Unauthorized, noAuthResponse.StatusCode);
        }

        // 400: a real caller (implicit admin) and a well-formed tenant, but the route id is not a
        // GUID -- ICallerContext resolves first, then the handler's own Guid.TryParse fails.
        using (var badIdRequest = new HttpRequestMessage(
            HttpMethod.Get, "/api/contracts/not-a-guid/negotiation-steps"))
        {
            badIdRequest.Headers.Add("X-Tenant-Id", tenantId.ToString());
            var badIdResponse = await client.SendAsync(badIdRequest);
            Assert.Equal(HttpStatusCode.BadRequest, badIdResponse.StatusCode);
        }

        // 404: well-formed tenant, well-formed but unknown contract id.
        var unknownContractResponse = await R1EndToEndTests.GetAsync(client, StepsUrl(Guid.NewGuid()), tenantId);
        Assert.Equal(HttpStatusCode.NotFound, unknownContractResponse.StatusCode);

        // 404: a real, validated caller who is not a member of this tenant -- never 403 (Rule B1:
        // a 403 here would be a tenant-existence oracle). ImplicitTenantAdminStartupFilter never
        // touches a request that already presents X-User-Id, so this stranger gets no membership.
        using (var strangerRequest = new HttpRequestMessage(HttpMethod.Get, StepsUrl(contractId)))
        {
            strangerRequest.Headers.Add("X-Tenant-Id", tenantId.ToString());
            strangerRequest.Headers.Add("X-User-Id", "stranger@raffa.test");
            var strangerResponse = await client.SendAsync(strangerRequest);
            Assert.Equal(HttpStatusCode.NotFound, strangerResponse.StatusCode);
        }

        // 200: implicit admin (X-Tenant-Id only) is a real member of this tenant -- an untouched
        // contract returns an empty array, not 404 (parent story AC-1).
        var memberResponse = await R1EndToEndTests.GetAsync(client, StepsUrl(contractId), tenantId);
        Assert.Equal(HttpStatusCode.OK, memberResponse.StatusCode);
        var emptySteps = await memberResponse.Content.ReadFromJsonAsync<string[]>();
        Assert.Empty(emptySteps!);
    }

    [Fact]
    public async Task Put_writes_the_whole_set_idempotently_and_an_absent_key_unticks_it()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();
        var contractId = await SeedContractAsync(_fixture, tenantId);

        var firstPut = await PutStepsAsync(client, contractId, tenantId, ["Notify", "RequestRevisedPricing"]);
        Assert.Equal(HttpStatusCode.OK, firstPut.StatusCode);
        Assert.Equal(["Notify", "RequestRevisedPricing"], await firstPut.Content.ReadFromJsonAsync<string[]>() ?? []);

        // Idempotent: PUTting the identical set again is still 200 with the same resulting set.
        var secondPut = await PutStepsAsync(client, contractId, tenantId, ["Notify", "RequestRevisedPricing"]);
        Assert.Equal(HttpStatusCode.OK, secondPut.StatusCode);
        Assert.Equal(["Notify", "RequestRevisedPricing"], await secondPut.Content.ReadFromJsonAsync<string[]>() ?? []);

        // A key absent from the body unticks it: dropping "RequestRevisedPricing" deletes its row.
        var thirdPut = await PutStepsAsync(client, contractId, tenantId, ["Notify"]);
        Assert.Equal(HttpStatusCode.OK, thirdPut.StatusCode);
        Assert.Equal(["Notify"], await thirdPut.Content.ReadFromJsonAsync<string[]>() ?? []);

        var readBack = await R1EndToEndTests.GetAsync(client, StepsUrl(contractId), tenantId);
        Assert.Equal(["Notify"], await readBack.Content.ReadFromJsonAsync<string[]>() ?? []);
    }

    [Fact]
    public async Task Put_rejects_an_unknown_step_name_and_writes_nothing()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();
        var contractId = await SeedContractAsync(_fixture, tenantId);

        var badPut = await PutStepsAsync(client, contractId, tenantId, ["Notify", "Bogus"]);
        Assert.Equal(HttpStatusCode.BadRequest, badPut.StatusCode);

        var afterBadPut = await R1EndToEndTests.GetAsync(client, StepsUrl(contractId), tenantId);
        Assert.Empty((await afterBadPut.Content.ReadFromJsonAsync<string[]>())!);
    }

    [Fact]
    public async Task A_second_tenants_ticks_never_appear_for_the_same_contract_id()
    {
        var client = _fixture.CreateClient();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var contractId = await SeedContractAsync(_fixture, tenantA);

        var putForA = await PutStepsAsync(client, contractId, tenantA, ["Notify", "CounterWithMarketBenchmark"]);
        Assert.Equal(HttpStatusCode.OK, putForA.StatusCode);

        // Tenant B naming tenant A's contract id: the explicit tenant_id filter plus Postgres RLS
        // both deny it -- zero rows and a 404, never tenant A's ticks and never a 500 (ADR-009 w16
        // clause 2c; the RLS half is proven independently by TenantRlsMigrationCheckTests's dynamic
        // discovery, this is the HTTP-shaped proof the task text also asks for).
        var getAsB = await R1EndToEndTests.GetAsync(client, StepsUrl(contractId), tenantB);
        Assert.Equal(HttpStatusCode.NotFound, getAsB.StatusCode);

        var putAsB = await PutStepsAsync(client, contractId, tenantB, ["Notify"]);
        Assert.Equal(HttpStatusCode.NotFound, putAsB.StatusCode);

        // Sanity, both directions (mirrors R1CrossTenantIsolationTests/.../R4CrossTenantIsolationTests'
        // own convention): tenant A's own ticks survive tenant B's attempt untouched.
        var getAsA = await R1EndToEndTests.GetAsync(client, StepsUrl(contractId), tenantA);
        Assert.Equal(HttpStatusCode.OK, getAsA.StatusCode);
        Assert.Equal(
            ["Notify", "CounterWithMarketBenchmark"], await getAsA.Content.ReadFromJsonAsync<string[]>() ?? []);
    }
}
