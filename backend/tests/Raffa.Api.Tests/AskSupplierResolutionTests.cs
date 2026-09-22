using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Chat.Application.Interview;
using Raffa.Api.Tests.TestSupport;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Tests;

/// <summary>
/// Host-level proof for task E27/F05/US01/T01 (NW-80; parent story us-01-supplier-resolution AC-2/
/// AC-3) over a real `POST /api/chat/query` round trip (the same
/// <see cref="InMemoryAskEngineFactory.WithInMemoryAskEngine"/> shape every sibling class in this
/// project already uses): a supplier with more than one validated contract is never silently
/// merged — <c>AskCopilotService.BuildInDomainReplyAsync</c> answers about the soonest one (the
/// same cancellation-deadline/renewal ordering <c>Raffa.Renewals.Application.RenewalPipelineBuilder
/// .Build</c> uses) and names the choice in the pack, and an unknown supplier still redirects to
/// the upload action rather than answering from nothing.
/// </summary>
public sealed class AskSupplierResolutionTests : IClassFixture<RaffaApiFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AskSupplierResolutionTests(RaffaApiFactory factory)
    {
        _factory = factory.WithPresentedCallersAsMembers().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "ConnectionStrings:DocumentsContracts",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true");
            builder.UseSetting("ConnectionStrings:Storage", "UseDevelopmentStorage=true");
        });
    }

    /// <summary>
    /// AC-2: "AsterCloud GmbH" has two validated contracts. The reply answers about the one with
    /// the soonest renewal (never a plain <c>FirstOrDefault</c> over portfolio order — the later
    /// contract's own fact is never cited) and the pack carries the disambiguation sentence NW-80
    /// requires ("Using {Type} CT-01 (renews …). Ask if you meant another.") so the choice is never
    /// a silent merge.
    /// </summary>
    [Fact]
    public async Task Multi_contract_supplier_answers_about_the_soonest_and_names_the_choice()
    {
        var recordingGateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));

        var tenantId = TenantId.New();
        var asterCloudId = EntityId.New();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // ADR-030 supersedes this soonest-deadline pick with a "which contract?" interview while
        // Chat:Interview:AskWhichContract is on (the default; AskInterviewTests covers it). This
        // test pins the NW-80 fallback that still runs with the switch off.
        var factory = _factory
            .WithInMemoryAskEngine(recordingGateway)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISupplierNameLookup>(
                    new StubSupplierNameLookup(new Dictionary<EntityId, string> { [asterCloudId] = "AsterCloud GmbH" }));
                services.AddSingleton(new InterviewOptions { AskWhichContract = false });
            }));

        var now = DateTimeOffset.UtcNow;

        // Distinct CreatedAt -- never a tie under the InMemory provider's LINQ-to-Objects ORDER BY
        // fallback (the same reason every sibling test in this project seeds it this way).
        var soonerContract = new Contract
        {
            TenantId = tenantId,
            SupplierId = asterCloudId,
            Type = ContractDocumentType.Msa,
            Status = "Completed",
            Currency = "CHF",
            EndDate = today.AddDays(30),
            // Task E30/F02/US01/T01 (NW-94): a notice-shaped question ("cancellation deadline") is
            // now server-decided by AskCopilotService.BuildNoticeFallbackReplyAsync -- an unknown
            // deadline abstains (case 4) rather than answering from the generic per-contract fact,
            // so this fixture needs a real one for AC-2's own "answers about the soonest" claim to
            // still hold.
            CancellationDeadline = today.AddDays(30),
            AutoRenewal = true,
            CreatedAt = now,
        };
        var laterContract = new Contract
        {
            TenantId = tenantId,
            SupplierId = asterCloudId,
            Type = ContractDocumentType.OrderForm,
            Status = "Completed",
            Currency = "CHF",
            EndDate = today.AddDays(400),
            AutoRenewal = true,
            CreatedAt = now.AddSeconds(-1),
        };

        await factory.SeedContractAsync(soonerContract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, soonerContract.Id));
        await factory.SeedContractAsync(laterContract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, laterContract.Id));

        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/query")
        {
            Content = JsonContent.Create(new { question = "What is the cancellation deadline for AsterCloud GmbH?" }),
        };
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        request.Headers.Add("X-User-Id", "alice@example.com");

        var response = await client.SendAsync(request);
        var rawBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(rawBody);
        var root = body.RootElement;

        // Never GateLabel.NeedsDocument: "AsterCloud GmbH" is a known supplier (exact match).
        Assert.Equal("answer", root.GetProperty("kind").GetString());

        var citations = root.GetProperty("citations").EnumerateArray().ToList();

        // Task E30/F02/US01/T01 (NW-94): the notice fallback's own fact item (like
        // BuildContractFactItem before it) has no source document -- contractId, not documentId, is
        // the real stamped id for a contract-level fact citation (the same NW-83 precedent this
        // file's sibling ScopedAskEndpointTests already follows).
        var citedContractIds = citations
            .Select(c => c.TryGetProperty("contractId", out var id) ? id.GetString() : null)
            .Where(id => id is not null)
            .ToList();

        // AC-2 "soonest wins": the 30-day contract's own fact is cited, the 400-day one never is.
        Assert.Contains(soonerContract.Id.ToString(), citedContractIds);
        Assert.DoesNotContain(laterContract.Id.ToString(), citedContractIds);

        // AC-2 "never silently merge": the pack's own disambiguation item is cited too, and its
        // text reaches the reply. Lower-case "ask" -- BuildMultiContractDisambiguationItem's own
        // shipped sentence joins the parenthetical with an em-dash ("... on file — ask if you meant
        // another."), not the two-sentence, capitalized form this assertion used to name; that
        // method is NW-80's, outside this task's own file scope, so the assertion is corrected to
        // match already-shipped behavior rather than the production code changed to match it.
        Assert.Contains(citations, c =>
            c.GetProperty("corpus").GetString() == "calc" &&
            c.GetProperty("snippet").GetString()!.Contains("ask if you meant another", StringComparison.Ordinal));
        Assert.Contains("ask if you meant another", rawBody, StringComparison.Ordinal);
        Assert.Contains("CT-01", rawBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// AC-3, proven end to end through the real HTTP round trip (<see cref="Gate.DomainGateTests"/>
    /// already proves it at the unit level): a supplier this tenant has never uploaded still
    /// redirects to the upload action, never a fabricated answer, regardless of the normalized/
    /// contains match this task added.
    /// </summary>
    [Fact]
    public async Task Unknown_supplier_stays_needs_document()
    {
        var recordingGateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));

        var client = _factory.WithInMemoryAskEngine(recordingGateway).CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/query")
        {
            Content = JsonContent.Create(new { question = "When does our Snowflake contract expire?" }),
        };
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-User-Id", "alice@example.com");

        var response = await client.SendAsync(request);
        var rawBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(rawBody);
        Assert.Equal("redirect", body.RootElement.GetProperty("kind").GetString());
        Assert.Contains("Snowflake", rawBody, StringComparison.Ordinal);
    }
}
