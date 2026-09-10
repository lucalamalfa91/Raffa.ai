using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.Audit.Domain;
using Raffa.Audit.Infrastructure;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.Savings.Domain;
using Raffa.Savings.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.IntegrationTests;

/// <summary>
/// Proves the Definition of Done for task E05/F03/US02/T02 (outcome-propagation) — parent story
/// us-02-outcome-capture AC-2 ("Realized savings surface on the savings dashboard (cross-wave)")
/// — end to end, over real HTTP through the real <c>Raffa.Api</c> composition root, against a
/// real, migrated Postgres+RLS database that now spans both <c>Raffa.Quotes</c> and
/// <c>Raffa.Savings</c> (see <see cref="QuoteIntegrationFixture"/>'s own doc comment: this is
/// its first scenario to actually need <c>Raffa.Savings</c>'s own schema at all). Mirrors
/// <see cref="QuoteEndToEndTests"/>'s own shape — one real host, no hand-rolled container, no
/// mocked <c>SavingsOpportunityService</c>/<c>NegotiationOutcomePropagationService</c>.
///
/// <c>Raffa.Quotes.Tests.NegotiationOutcomeServiceTests
/// .CaptureAsync_persists_the_supplied_savings_opportunity_id_unvalidated</c>'s own doc comment
/// forward-references this class by name as the place AC-2's actual cross-module propagation (and
/// the "unknown id" not-found-equivalent path) is proved — this is that class.
///
/// Never asserts against <c>SavingsKpiQueryService</c>/`GET /api/savings/kpis` directly
/// (out of this task's own "Files to create or modify" scope): AC-2's "surface on the savings
/// dashboard" is proved instead by asserting the exact state that KPI query groups by —
/// <see cref="SavingsOpportunityStatus.Realized"/> (see that member's own doc comment: it is
/// spec §4.3's "savings realized" KPI bucket) plus the <see cref="RealizedSavings"/> row itself —
/// the same "prove the state a downstream reader depends on, not the downstream reader itself"
/// scoping <see cref="QuoteEndToEndTests"/>'s own tests already apply to their own downstream
/// consumers.
/// </summary>
public sealed class NegotiationOutcomePropagationEndToEndTests : IClassFixture<QuoteIntegrationFixture>
{
    private readonly QuoteIntegrationFixture _fixture;

    public NegotiationOutcomePropagationEndToEndTests(QuoteIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Capturing_an_outcome_against_a_real_savings_opportunity_realizes_it_and_writes_the_propagated_audit_entry()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();

        var quoteId = await SeedQuoteAsync(tenantId);
        var savingsOpportunityId = await SeedSavingsOpportunityAsync(tenantId);

        var body = await CaptureOutcomeAsync(client, tenantId, quoteId, savingsOpportunityId);

        // AC-2: the same 201 response that already proves AC-1 (task T01) also reports the
        // cross-module write as having succeeded — NegotiationsEndpointExtensions' own
        // "savingsPropagated/savingsPropagationError on the same 201, never a distinct HTTP error"
        // contract.
        Assert.True(body.GetProperty("savingsPropagated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("savingsPropagationError").ValueKind);
        Assert.Equal(savingsOpportunityId, body.GetProperty("savingsOpportunityId").GetGuid());
        Assert.Equal(85_000m, body.GetProperty("realizedSaving").GetDecimal());
        var negotiationOutcomeId = body.GetProperty("id").GetGuid();

        using var scope = _fixture.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var tenantScope = tenantContext.BeginScope(new TenantId(tenantId));

        // AC-2 "surface on the savings dashboard": SavingsKpiQueryService's own "savings realized"
        // KPI bucket groups by exactly this status (SavingsOpportunityStatus.Realized's own doc
        // comment) — proving the status flip here is proving the dashboard will show it.
        var savingsDb = scope.ServiceProvider.GetRequiredService<SavingsDbContext>();
        var opportunity = await savingsDb.SavingsOpportunities
            .SingleAsync(o => o.Id == new EntityId(savingsOpportunityId));
        Assert.Equal(SavingsOpportunityStatus.Realized, opportunity.Status);

        var realized = await savingsDb.RealizedSavingsRecords
            .SingleAsync(r => r.SavingsOpportunityId == new EntityId(savingsOpportunityId));
        Assert.Equal(85_000m, realized.Amount);
        Assert.Equal(opportunity.Currency, realized.Currency);

        // The distinct negotiation_outcome.propagated entry NegotiationOutcomePropagationService
        // writes in addition to (never instead of) SavingsOpportunityService.UpdateAsync's own
        // savings_opportunity.realized entry — the one fact neither existing entry captures alone:
        // which NegotiationOutcome caused which SavingsOpportunity to realize.
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var auditEvent = await auditDb.AuditEvents.SingleAsync(
            e => e.Action == "negotiation_outcome.propagated"
                && e.ResourceId == negotiationOutcomeId.ToString());
        Assert.Equal(new TenantId(tenantId), auditEvent.TenantId);
        Assert.Contains(savingsOpportunityId.ToString(), auditEvent.Detail!);
    }

    [Fact]
    public async Task Capturing_an_outcome_against_an_unknown_savings_opportunity_id_still_persists_the_outcome()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();

        var quoteId = await SeedQuoteAsync(tenantId);
        var unknownSavingsOpportunityId = Guid.NewGuid();

        var body = await CaptureOutcomeAsync(client, tenantId, quoteId, unknownSavingsOpportunityId);

        // NegotiationOutcomePropagationService's own "never fails an already-durable capture"
        // contract: an unknown id is reported honestly on the response, but the 201 above already
        // proves the capture itself went through — never a 4xx/5xx for this call.
        Assert.False(body.GetProperty("savingsPropagated").GetBoolean());
        Assert.Equal(
            "Savings opportunity not found.", body.GetProperty("savingsPropagationError").GetString());
        var negotiationOutcomeId = body.GetProperty("id").GetGuid();

        using var scope = _fixture.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var tenantScope = tenantContext.BeginScope(new TenantId(tenantId));

        var quotesDb = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        var persisted = await quotesDb.NegotiationOutcomes
            .SingleAsync(o => o.Id == new EntityId(negotiationOutcomeId));
        Assert.Equal(new EntityId(unknownSavingsOpportunityId), persisted.SavingsOpportunityId);

        // No propagated audit entry for a link that was never actually made.
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var propagated = await auditDb.AuditEvents
            .Where(e => e.Action == "negotiation_outcome.propagated"
                && e.ResourceId == negotiationOutcomeId.ToString())
            .ToListAsync();
        Assert.Empty(propagated);
    }

    /// <summary>
    /// Posts the spec §12.2 worked example (Original 520k / Target 420k / Final 435k -> 85k
    /// realized saving) with <paramref name="savingsOpportunityId"/> attached, and returns the
    /// parsed 201 response body — same "raw JsonElement, not a typed DTO" reasoning as
    /// <c>QuoteEndToEndTests.UploadQuoteAsync</c>'s own doc comment.
    /// </summary>
    private static async Task<JsonElement> CaptureOutcomeAsync(
        HttpClient client, Guid tenantId, Guid quoteId, Guid savingsOpportunityId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/negotiations/outcomes")
        {
            Content = JsonContent.Create(new
            {
                quoteId,
                originalQuoteTotal = 520_000m,
                targetPrice = 420_000m,
                finalPrice = 435_000m,
                negotiationDurationDays = 24,
                leversUsed = new[] { "Term", "QuarterEnd" },
                savingsOpportunityId,
            }),
        };
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    /// <summary>Same shape as <c>Raffa.Quotes.Tests.NegotiationOutcomeServiceTests
    /// .SeedQuoteAsync</c>, adapted to seed through the fixture's own DI-resolved
    /// <see cref="QuotesDbContext"/> (a real, RLS-scoped connection under the fixture's dedicated
    /// unprivileged app role) rather than a hand-constructed one.</summary>
    private async Task<Guid> SeedQuoteAsync(Guid tenantId)
    {
        using var scope = _fixture.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var tenantScope = tenantContext.BeginScope(new TenantId(tenantId));
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

        var quoteId = EntityId.New();
        db.Quotes.Add(new Quote
        {
            Id = quoteId,
            TenantId = new TenantId(tenantId),
            FileName = "quote.pdf",
            MimeType = "application/pdf",
            StoragePath = $"{tenantId:D}/quote.pdf",
            Checksum = "deadbeef",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return quoteId.Value;
    }

    /// <summary>Seeds a real, tenant-owned <see cref="SavingsOpportunity"/> the same way
    /// <c>SavingsOpportunityService.CreateAsync</c> would ("identify" — no owner yet), so
    /// <c>NegotiationOutcomePropagationService</c>'s own call to
    /// <c>SavingsOpportunityService.UpdateAsync</c> has a real row to finalize.</summary>
    private async Task<Guid> SeedSavingsOpportunityAsync(Guid tenantId)
    {
        using var scope = _fixture.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var tenantScope = tenantContext.BeginScope(new TenantId(tenantId));
        var db = scope.ServiceProvider.GetRequiredService<SavingsDbContext>();

        var opportunityId = EntityId.New();
        var now = DateTimeOffset.UtcNow;
        db.SavingsOpportunities.Add(new SavingsOpportunity
        {
            Id = opportunityId,
            TenantId = new TenantId(tenantId),
            Type = "Software licensing",
            CurrentSpend = 520_000m,
            Currency = "CHF",
            EstimatedSavingsLow = 60_000m,
            EstimatedSavingsHigh = 100_000m,
            Confidence = 0.75,
            Status = SavingsOpportunityStatus.Identified,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        return opportunityId.Value;
    }
}
