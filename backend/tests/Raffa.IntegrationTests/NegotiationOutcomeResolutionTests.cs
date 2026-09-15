using System.Net;
using System.Text.Json;
using Raffa.Audit.Infrastructure;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.Savings.Domain;
using Raffa.Savings.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Raffa.Suppliers.Products.Application;
using Raffa.Suppliers.Products.Domain;
using Raffa.Suppliers.Products.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.IntegrationTests;

/// <summary>
/// Proves the Definition of Done for task E19/F04/US01/T01 (outcome-resolves-the-opportunity;
/// ADR-028 §D5 clause 2, ratified by the w16 round-2 footer) — the three-way resolution
/// (<see cref="Raffa.Api.NegotiationOutcomePropagationService.ResolveSavingsOpportunityIdAsync"/>)
/// over real HTTP through the real <c>Raffa.Api</c> composition root, against a real, migrated
/// Postgres+RLS database spanning <c>Raffa.Quotes</c>, <c>Raffa.Savings</c> and — new to this task —
/// <c>Raffa.Suppliers.Products</c> (see <see cref="QuoteIntegrationFixture"/>'s own doc comment: this
/// is its first scenario to actually need that module's schema). Mirrors
/// <see cref="NegotiationOutcomePropagationEndToEndTests"/>'s own shape — one real host, no
/// hand-rolled container, no mocked service — and that class's own explicit-id assertions
/// (`:65`, `:114`) stay exactly as they were: clause 1 (<see cref="Raffa.Api
/// .NegotiationOutcomePropagationService.PropagateAsync"/>) is untouched by this task. This class
/// covers clause 2 (the no-id resolution) plus the precedence between the two routes.
///
/// <para>
/// Every scenario below seeds its own fresh <see cref="Guid.NewGuid()"/> tenant, so — like every
/// other suite sharing <see cref="QuoteIntegrationFixture"/>'s single Postgres instance — tests
/// never see each other's rows and can run in any order.
/// </para>
/// </summary>
public sealed class NegotiationOutcomeResolutionTests : IClassFixture<QuoteIntegrationFixture>
{
    private readonly QuoteIntegrationFixture _fixture;

    public NegotiationOutcomeResolutionTests(QuoteIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Explicit_id_links_verbatim_even_when_the_suppliers_name_would_otherwise_resolve_ambiguously()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();

        var supplierId = await SeedSupplierAsync(tenantId, "Salesforce, Inc.");
        // Two open opportunities for the same supplier: clause 2 alone would decline this as
        // ambiguous (AC-3). The explicit id below must still win outright -- clause 1, precedence
        // unchanged from task E05/F03/US02/T02 (AC-1).
        var otherOpportunityId = await SeedSavingsOpportunityAsync(
            tenantId, supplierId, SavingsOpportunityStatus.Identified);
        var chosenOpportunityId = await SeedSavingsOpportunityAsync(
            tenantId, supplierId, SavingsOpportunityStatus.Identified);

        var quoteId = await SeedQuoteAsync(tenantId, "Salesforce, Inc.");

        var body = await CaptureOutcomeAsync(client, tenantId, quoteId, savingsOpportunityId: chosenOpportunityId);

        Assert.True(body.GetProperty("savingsPropagated").GetBoolean());
        Assert.Equal(chosenOpportunityId, body.GetProperty("savingsOpportunityId").GetGuid());

        using var scope = _fixture.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var tenantScope = tenantContext.BeginScope(new TenantId(tenantId));
        var savingsDb = scope.ServiceProvider.GetRequiredService<SavingsDbContext>();

        var chosen = await savingsDb.SavingsOpportunities.SingleAsync(o => o.Id == new EntityId(chosenOpportunityId));
        Assert.Equal(SavingsOpportunityStatus.Realized, chosen.Status);

        var other = await savingsDb.SavingsOpportunities.SingleAsync(o => o.Id == new EntityId(otherOpportunityId));
        Assert.Equal(SavingsOpportunityStatus.Identified, other.Status); // untouched
    }

    [Fact]
    public async Task No_id_and_exactly_one_open_opportunity_for_the_resolved_supplier_links_it_and_a_second_client_agrees()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();

        // A supplier already known under a slightly different spelling than the quote uses --
        // SupplierNameNormalizer.Normalize collapses both to "salesforce" (AC-2's own worked
        // example: "Salesforce, Inc." and "salesforce" are one supplier). InProgress, not
        // Identified, to prove "open" is not just the initial status.
        var supplierId = await SeedSupplierAsync(tenantId, "Salesforce, Inc.");
        var opportunityId = await SeedSavingsOpportunityAsync(
            tenantId, supplierId, SavingsOpportunityStatus.InProgress);

        var quoteId = await SeedQuoteAsync(tenantId, "SALESFORCE INC");

        var body = await CaptureOutcomeAsync(client, tenantId, quoteId, savingsOpportunityId: null);

        Assert.True(body.GetProperty("savingsPropagated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("savingsPropagationError").ValueKind);
        // The persisted NegotiationOutcome.SavingsOpportunityId column -- and this response field,
        // which only ever echoes it -- record just what the caller supplied. Clause 2's own link is
        // expressed through the SavingsOpportunity row alone: no column, no contract delta
        // (backend/README.md "Negotiation Outcome" section; ADR-028 §D5).
        Assert.Equal(JsonValueKind.Null, body.GetProperty("savingsOpportunityId").ValueKind);

        using var scope = _fixture.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var tenantScope = tenantContext.BeginScope(new TenantId(tenantId));
        var savingsDb = scope.ServiceProvider.GetRequiredService<SavingsDbContext>();

        var opportunity = await savingsDb.SavingsOpportunities.SingleAsync(o => o.Id == new EntityId(opportunityId));
        Assert.Equal(SavingsOpportunityStatus.Realized, opportunity.Status);

        var realized = await savingsDb.RealizedSavingsRecords
            .SingleAsync(r => r.SavingsOpportunityId == new EntityId(opportunityId));
        Assert.Equal(85_000m, realized.Amount);

        // AC-6: a second, independent client -- no shared state with the one that captured the
        // outcome -- reads the same fact back.
        var secondClient = _fixture.CreateClient();
        var savingsResponse = await R1EndToEndTests.GetAsync(secondClient, "/api/savings", tenantId);
        var savingsBody = await R1EndToEndTests.ParseAsync(savingsResponse);
        var item = Assert.Single(
            savingsBody.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == opportunityId);
        Assert.Equal("Realized", item.GetProperty("status").GetString());
    }

    [Fact]
    public async Task No_id_and_zero_open_opportunities_for_the_resolved_supplier_records_the_outcome_unlinked()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();

        // The supplier is known (so the name *does* resolve) but tracks no opportunity at all --
        // still an honest decline (AC-3's zero-match branch), never a guess at a supplier that
        // merely exists.
        await SeedSupplierAsync(tenantId, "Salesforce, Inc.");
        var quoteId = await SeedQuoteAsync(tenantId, "Salesforce, Inc.");

        var body = await CaptureOutcomeAsync(client, tenantId, quoteId, savingsOpportunityId: null);

        AssertDeclined(body);
        await AssertNoPropagationAuditEntryAsync(tenantId, body.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task No_id_and_two_open_opportunities_for_the_resolved_supplier_records_the_outcome_unlinked()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();

        var supplierId = await SeedSupplierAsync(tenantId, "Salesforce, Inc.");
        await SeedSavingsOpportunityAsync(tenantId, supplierId, SavingsOpportunityStatus.Identified);
        await SeedSavingsOpportunityAsync(tenantId, supplierId, SavingsOpportunityStatus.InProgress);

        var quoteId = await SeedQuoteAsync(tenantId, "Salesforce, Inc.");

        var body = await CaptureOutcomeAsync(client, tenantId, quoteId, savingsOpportunityId: null);

        AssertDeclined(body);
        await AssertNoPropagationAuditEntryAsync(tenantId, body.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task No_id_and_the_quotes_supplier_name_is_unknown_records_the_outcome_unlinked_and_creates_no_supplier()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();

        var quoteId = await SeedQuoteAsync(tenantId, "Totally Unknown Corp");

        var body = await CaptureOutcomeAsync(client, tenantId, quoteId, savingsOpportunityId: null);

        AssertDeclined(body);

        using var scope = _fixture.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var tenantScope = tenantContext.BeginScope(new TenantId(tenantId));
        var suppliersDb = scope.ServiceProvider.GetRequiredService<SuppliersDbContext>();

        // AC-5: recording an outcome never creates a supplier row, even to resolve one --
        // SupplierResolver (which resolves *or creates*) is never on this path.
        Assert.Equal(0, await suppliersDb.Suppliers.CountAsync(s => s.TenantId == new TenantId(tenantId)));
    }

    [Fact]
    public async Task An_already_realized_opportunity_for_the_same_supplier_is_not_a_candidate()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();

        var supplierId = await SeedSupplierAsync(tenantId, "Salesforce, Inc.");
        // Noise: already realized -- must not count as an "open" candidate, and must not make the
        // match ambiguous against the one genuinely open opportunity below.
        var realizedNoiseId = await SeedSavingsOpportunityAsync(
            tenantId, supplierId, SavingsOpportunityStatus.Realized);
        var openOpportunityId = await SeedSavingsOpportunityAsync(
            tenantId, supplierId, SavingsOpportunityStatus.Identified);

        var quoteId = await SeedQuoteAsync(tenantId, "Salesforce, Inc.");

        var body = await CaptureOutcomeAsync(client, tenantId, quoteId, savingsOpportunityId: null);

        Assert.True(body.GetProperty("savingsPropagated").GetBoolean());

        using var scope = _fixture.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var tenantScope = tenantContext.BeginScope(new TenantId(tenantId));
        var savingsDb = scope.ServiceProvider.GetRequiredService<SavingsDbContext>();

        var realized = await savingsDb.RealizedSavingsRecords
            .SingleAsync(r => r.TenantId == new TenantId(tenantId));
        Assert.Equal(new EntityId(openOpportunityId), realized.SavingsOpportunityId);

        var noise = await savingsDb.SavingsOpportunities.SingleAsync(o => o.Id == new EntityId(realizedNoiseId));
        Assert.Equal(SavingsOpportunityStatus.Realized, noise.Status); // untouched
    }

    /// <summary>Fence 2 (ADR-028 w16 round-2 footer): a decline must not be visible anywhere in the
    /// Savings module's own read surface -- not just "no new row", but not even a byte moved on an
    /// existing one.</summary>
    [Fact]
    public async Task Declining_resolution_leaves_the_savings_list_payload_byte_identical()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();

        // An unrelated, pre-existing opportunity -- Fence 2 must leave this untouched too, not just
        // "add nothing new" to an empty list.
        var unrelatedSupplierId = await SeedSupplierAsync(tenantId, "Zurich Insurance");
        await SeedSavingsOpportunityAsync(tenantId, unrelatedSupplierId, SavingsOpportunityStatus.Identified);

        var beforeResponse = await R1EndToEndTests.GetAsync(client, "/api/savings", tenantId);
        Assert.Equal(HttpStatusCode.OK, beforeResponse.StatusCode);
        var beforeBody = await beforeResponse.Content.ReadAsStringAsync();

        // The quote's own supplier has never been resolved for this tenant at all -- an honest
        // decline (AC-3's zero-match branch).
        var quoteId = await SeedQuoteAsync(tenantId, "Totally Unknown Corp");
        var captureBody = await CaptureOutcomeAsync(client, tenantId, quoteId, savingsOpportunityId: null);
        AssertDeclined(captureBody);

        var afterResponse = await R1EndToEndTests.GetAsync(client, "/api/savings", tenantId);
        Assert.Equal(HttpStatusCode.OK, afterResponse.StatusCode);
        var afterBody = await afterResponse.Content.ReadAsStringAsync();

        Assert.Equal(beforeBody, afterBody);
    }

    [Fact]
    public async Task Resolution_never_links_an_opportunity_belonging_to_another_tenant()
    {
        var client = _fixture.CreateClient();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Both tenants independently do business with a same-named supplier -- each gets its own
        // Supplier row (the (tenant_id, normalized_name) unique index is per-tenant) and its own
        // open opportunity.
        var supplierIdA = await SeedSupplierAsync(tenantA, "Allianz");
        var opportunityIdA = await SeedSavingsOpportunityAsync(
            tenantA, supplierIdA, SavingsOpportunityStatus.Identified);

        var supplierIdB = await SeedSupplierAsync(tenantB, "Allianz");
        var opportunityIdB = await SeedSavingsOpportunityAsync(
            tenantB, supplierIdB, SavingsOpportunityStatus.Identified);

        var quoteId = await SeedQuoteAsync(tenantA, "Allianz");

        var body = await CaptureOutcomeAsync(client, tenantA, quoteId, savingsOpportunityId: null);

        Assert.True(body.GetProperty("savingsPropagated").GetBoolean());

        // Fresh scope per tenant check -- a shared DbContext instance could otherwise carry over a
        // connection already stamped with the first tenant's own RLS claim (ADR-009).
        using (var scopeA = _fixture.Services.CreateScope())
        {
            var tenantContextA = scopeA.ServiceProvider.GetRequiredService<ITenantContext>();
            using var tenantScopeA = tenantContextA.BeginScope(new TenantId(tenantA));
            var savingsDbA = scopeA.ServiceProvider.GetRequiredService<SavingsDbContext>();

            var opportunityA = await savingsDbA.SavingsOpportunities
                .SingleAsync(o => o.Id == new EntityId(opportunityIdA));
            Assert.Equal(SavingsOpportunityStatus.Realized, opportunityA.Status);
        }

        using (var scopeB = _fixture.Services.CreateScope())
        {
            var tenantContextB = scopeB.ServiceProvider.GetRequiredService<ITenantContext>();
            using var tenantScopeB = tenantContextB.BeginScope(new TenantId(tenantB));
            var savingsDbB = scopeB.ServiceProvider.GetRequiredService<SavingsDbContext>();

            // AC-7: an opportunity of tenant B is never linked to an outcome of tenant A.
            var opportunityB = await savingsDbB.SavingsOpportunities
                .SingleAsync(o => o.Id == new EntityId(opportunityIdB));
            Assert.Equal(SavingsOpportunityStatus.Identified, opportunityB.Status);

            var realizedForB = await savingsDbB.RealizedSavingsRecords
                .CountAsync(r => r.TenantId == new TenantId(tenantB));
            Assert.Equal(0, realizedForB);
        }
    }

    /// <summary>The shared shape of every declined capture's response (AC-3): still a 201, still
    /// unlinked, and -- the round-2 footer's corrected rule -- <c>savingsPropagated</c> stays
    /// honestly <see langword="null"/> rather than <see langword="false"/>, because no link was even
    /// attempted by <c>PropagateAsync</c> (Fence 2).</summary>
    private static void AssertDeclined(JsonElement body)
    {
        Assert.Equal(JsonValueKind.Null, body.GetProperty("savingsPropagated").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("savingsPropagationError").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("savingsOpportunityId").ValueKind);
    }

    private async Task AssertNoPropagationAuditEntryAsync(Guid tenantId, Guid negotiationOutcomeId)
    {
        using var scope = _fixture.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var tenantScope = tenantContext.BeginScope(new TenantId(tenantId));
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var propagated = await auditDb.AuditEvents
            .Where(e => e.Action == "negotiation_outcome.propagated"
                && e.ResourceId == negotiationOutcomeId.ToString())
            .ToListAsync();
        Assert.Empty(propagated);
    }

    /// <summary>
    /// Posts the spec §12.2 worked example (Original 520k / Target 420k / Final 435k -&gt; 85k
    /// realized saving) -- same numbers <see cref="NegotiationOutcomePropagationEndToEndTests"/>
    /// uses, so this class's own assertions on the realized amount agree with that class's -- and
    /// returns the parsed 201 response body. <paramref name="savingsOpportunityId"/> is the request
    /// field itself: <see langword="null"/> exercises clause 2 (no id supplied) exactly the way an
    /// omitted JSON property would (<c>System.Text.Json</c> treats an explicit JSON <c>null</c> the
    /// same as an absent property for a nullable record parameter).
    /// </summary>
    private static async Task<JsonElement> CaptureOutcomeAsync(
        HttpClient client, Guid tenantId, Guid quoteId, Guid? savingsOpportunityId)
    {
        var response = await R1EndToEndTests.PostAsync(client, "/api/negotiations/outcomes", tenantId, new
        {
            quoteId,
            originalQuoteTotal = 520_000m,
            targetPrice = 420_000m,
            finalPrice = 435_000m,
            negotiationDurationDays = 24,
            leversUsed = new[] { "Term", "QuarterEnd" },
            savingsOpportunityId,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return await R1EndToEndTests.ParseAsync(response);
    }

    /// <summary>Same shape as <c>NegotiationOutcomePropagationEndToEndTests.SeedQuoteAsync</c>, with
    /// <paramref name="supplierName"/> added -- the fact
    /// <see cref="Raffa.Api.NegotiationOutcomePropagationService.ResolveSavingsOpportunityIdAsync"/>
    /// itself reads and normalizes.</summary>
    private async Task<Guid> SeedQuoteAsync(Guid tenantId, string? supplierName)
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
            Supplier = supplierName,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return quoteId.Value;
    }

    /// <summary>Seeds a real, tenant-owned <see cref="Supplier"/> the same way
    /// <c>Raffa.Suppliers.Products.Application.SupplierResolver.ResolveAsync</c> would on a first
    /// sighting of <paramref name="name"/> -- <see cref="SupplierNameNormalizer.Normalize"/> is the
    /// exact function under test on the resolution's own read side
    /// (<c>ISupplierNameLookup.FindByNormalizedNameAsync</c>), so the seed must produce the same
    /// <c>NormalizedName</c> a real first-sighting would have.</summary>
    private async Task<Guid> SeedSupplierAsync(Guid tenantId, string name)
    {
        using var scope = _fixture.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var tenantScope = tenantContext.BeginScope(new TenantId(tenantId));
        var db = scope.ServiceProvider.GetRequiredService<SuppliersDbContext>();

        var now = DateTimeOffset.UtcNow;
        var supplier = new Supplier
        {
            TenantId = new TenantId(tenantId),
            Name = name,
            NormalizedName = SupplierNameNormalizer.Normalize(name),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        return supplier.Id.Value;
    }

    /// <summary>Same shape as <c>NegotiationOutcomePropagationEndToEndTests.SeedSavingsOpportunityAsync</c>,
    /// with <paramref name="supplierId"/> and <paramref name="status"/> added -- the two facts the
    /// resolution's own open-opportunities-by-supplier query reads.</summary>
    private async Task<Guid> SeedSavingsOpportunityAsync(
        Guid tenantId, Guid supplierId, SavingsOpportunityStatus status)
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
            SupplierId = new EntityId(supplierId),
            Type = "Software licensing",
            CurrentSpend = 520_000m,
            Currency = "CHF",
            EstimatedSavingsLow = 60_000m,
            EstimatedSavingsHigh = 100_000m,
            Confidence = 0.75,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        return opportunityId.Value;
    }
}
