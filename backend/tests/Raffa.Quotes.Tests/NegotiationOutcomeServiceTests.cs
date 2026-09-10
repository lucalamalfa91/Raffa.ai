using Raffa.Quotes.Application.Outcome;
using Raffa.Quotes.Application.Strategy;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.Quotes.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Quotes.Tests;

/// <summary>
/// Proves the Definition of Done for task E05/F03/US02/T01 (negotiation-outcome) — parent story
/// us-02-outcome-capture AC-1 ("<c>POST /api/negotiations/outcomes</c> records original/target/
/// final/saving/discount/duration/levers") end to end against a real, migrated Postgres+RLS
/// database (ADR-009's own "no in-memory provider" posture), plus AC-3's "versioned" half
/// (Appendix C rule 5 — never destructively overwritten) and "audit-tracked" half (Appendix C rule
/// 9). Mirrors <c>Raffa.Quotes.Tests.QuoteUploadServiceTests</c>'s own shape/scaffolding.
///
/// Runs all assertions through a dedicated, deliberately unprivileged Postgres role
/// (<see cref="AppRoleName"/>: `NOSUPERUSER NOBYPASSRLS`, not the table owner) — the Testcontainers
/// bootstrap role is always a superuser, and superusers unconditionally bypass row security, so
/// asserting tenant-scoped behavior over that connection would pass vacuously.
/// </summary>
public sealed class NegotiationOutcomeServiceTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_negotiation_outcome_app";
    private const string AppRolePassword = "raffa_negotiation_outcome_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<QuotesDbContext>();
        QuotesDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());

        await using (var adminDb = new QuotesDbContext(adminOptions.Options))
        {
            await adminDb.Database.MigrateAsync();

            await adminDb.Database.ExecuteSqlRawAsync(
                $"""
                CREATE ROLE {AppRoleName} LOGIN PASSWORD '{AppRolePassword}' NOSUPERUSER NOBYPASSRLS;
                GRANT USAGE ON SCHEMA public TO {AppRoleName};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRoleName};
                """);
        }

        _appConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = AppRoleName,
            Password = AppRolePassword,
        }.ConnectionString;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private QuotesDbContext CreateAppContext(ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<QuotesDbContext>();
        QuotesDbContextOptions.Configure(optionsBuilder, _appConnectionString, tenantContext);
        return new QuotesDbContext(optionsBuilder.Options);
    }

    private static NegotiationOutcomeCaptureRequest ValidRequest(
        Guid quoteId,
        decimal originalQuoteTotal = 520_000m,
        decimal? targetPrice = 420_000m,
        decimal finalPrice = 435_000m,
        int negotiationDurationDays = 24,
        IReadOnlyList<string>? leversUsed = null,
        Guid? savingsOpportunityId = null) =>
        new(
            quoteId,
            originalQuoteTotal,
            targetPrice,
            finalPrice,
            negotiationDurationDays,
            leversUsed ?? ["Term", "QuarterEnd"],
            savingsOpportunityId);

    private async Task<EntityId> SeedQuoteAsync(TenantId tenantId)
    {
        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(tenantId);
        await using var db = CreateAppContext(tenantContext);

        var quoteId = EntityId.New();
        db.Quotes.Add(new Quote
        {
            Id = quoteId,
            TenantId = tenantId,
            FileName = "quote.pdf",
            MimeType = "application/pdf",
            StoragePath = $"{tenantId.Value:D}/quote.pdf",
            Checksum = "deadbeef",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return quoteId;
    }

    [Fact]
    public async Task CaptureAsync_persists_the_outcome_computes_saving_and_discount_and_writes_an_audit_entry()
    {
        var tenantId = TenantId.New();
        var quoteId = await SeedQuoteAsync(tenantId);
        var now = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
        var auditWriter = new RecordingAuditWriter();
        var tenantContext = new TenantContext();

        await using var db = CreateAppContext(tenantContext);
        var service = new NegotiationOutcomeService(db, tenantContext, new FixedClock(now), auditWriter);

        // Reproduces spec §12.2's own worked example (Original 520k / Target 420k / Final 435k /
        // Saving 85k / Discount ~16.3% / Duration 24 days / Levers "36-month commitment; quarter-end
        // timing" -> Term + QuarterEnd).
        var result = await service.CaptureAsync(tenantId, ValidRequest(quoteId.Value));

        Assert.True(result.IsSuccess);
        var outcome = result.Value;
        Assert.Equal(quoteId, outcome.QuoteId);
        Assert.Equal(520_000m, outcome.OriginalQuoteTotal);
        Assert.Equal(420_000m, outcome.TargetPrice);
        Assert.Equal(435_000m, outcome.FinalPrice);
        Assert.Equal(85_000m, outcome.RealizedSaving);
        Assert.Equal(24, outcome.NegotiationDurationDays);
        Assert.Equal([NegotiationLeverType.Term, NegotiationLeverType.QuarterEnd], outcome.LeversUsed);
        Assert.Equal(now, outcome.CapturedAt);
        // Task E05/F03/US02/T02 (outcome-propagation): honestly null when the caller supplies no
        // savingsOpportunityId — not every negotiated outcome traces back to a pre-tracked
        // opportunity.
        Assert.Null(outcome.SavingsOpportunityId);

        var auditEntry = Assert.Single(auditWriter.Written);
        Assert.Equal(tenantId, auditEntry.TenantId);
        Assert.Equal("negotiation_outcome.captured", auditEntry.Action);
        Assert.Equal("negotiation_outcome", auditEntry.ResourceType);
        Assert.Equal(outcome.Id.Value.ToString(), auditEntry.ResourceId);
        Assert.Equal(now, auditEntry.Timestamp);

        using (tenantContext.BeginScope(tenantId))
        {
            await using var readDb = CreateAppContext(tenantContext);
            var persisted = await readDb.NegotiationOutcomes.SingleAsync(o => o.Id == outcome.Id);
            Assert.Equal(tenantId, persisted.TenantId);
            Assert.Equal(quoteId, persisted.QuoteId);
            Assert.Equal(85_000m, persisted.RealizedSaving);
            Assert.Equal([NegotiationLeverType.Term, NegotiationLeverType.QuarterEnd], persisted.LeversUsed);
        }
    }

    [Fact]
    public async Task CaptureAsync_allows_a_second_capture_for_the_same_quote_without_overwriting_the_first()
    {
        // AC-3 "versioned" (Appendix C rule 5 — never destructively overwrite): a renegotiation or a
        // correction to an earlier capture is a new row, never an update to the first one.
        var tenantId = TenantId.New();
        var quoteId = await SeedQuoteAsync(tenantId);
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new NegotiationOutcomeService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var first = await service.CaptureAsync(tenantId, ValidRequest(quoteId.Value, finalPrice: 435_000m));
        var second = await service.CaptureAsync(tenantId, ValidRequest(quoteId.Value, finalPrice: 400_000m));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value.Id, second.Value.Id);

        using (tenantContext.BeginScope(tenantId))
        {
            await using var readDb = CreateAppContext(tenantContext);
            var rows = await readDb.NegotiationOutcomes.Where(o => o.QuoteId == quoteId).ToListAsync();
            Assert.Equal(2, rows.Count);
            // The first capture is still reachable, untouched, exactly as originally recorded.
            Assert.Contains(rows, o => o.Id == first.Value.Id && o.FinalPrice == 435_000m);
            Assert.Contains(rows, o => o.Id == second.Value.Id && o.FinalPrice == 400_000m);
        }
    }

    [Fact]
    public async Task CaptureAsync_fails_when_the_quote_does_not_exist_for_this_tenant()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var auditWriter = new RecordingAuditWriter();
        var service = new NegotiationOutcomeService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), auditWriter);

        var result = await service.CaptureAsync(tenantId, ValidRequest(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal(NegotiationOutcomeService.QuoteNotFoundError, result.Error);
        Assert.Empty(auditWriter.Written);
    }

    [Fact]
    public async Task CaptureAsync_fails_when_the_quote_belongs_to_a_different_tenant()
    {
        var ownerTenant = TenantId.New();
        var otherTenant = TenantId.New();
        var quoteId = await SeedQuoteAsync(ownerTenant);

        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new NegotiationOutcomeService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var result = await service.CaptureAsync(otherTenant, ValidRequest(quoteId.Value));

        Assert.True(result.IsFailure);
        Assert.Equal(NegotiationOutcomeService.QuoteNotFoundError, result.Error);
    }

    [Fact]
    public async Task CaptureAsync_rejects_a_non_positive_original_quote_total()
    {
        var tenantId = TenantId.New();
        var quoteId = await SeedQuoteAsync(tenantId);
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var auditWriter = new RecordingAuditWriter();
        var service = new NegotiationOutcomeService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), auditWriter);

        var result = await service.CaptureAsync(tenantId, ValidRequest(quoteId.Value, originalQuoteTotal: 0m));

        Assert.True(result.IsFailure);
        Assert.Equal(NegotiationOutcomeService.OriginalQuoteTotalMustBePositiveError, result.Error);
        Assert.Empty(auditWriter.Written);
    }

    [Fact]
    public async Task CaptureAsync_rejects_a_negative_target_price()
    {
        var tenantId = TenantId.New();
        var quoteId = await SeedQuoteAsync(tenantId);
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new NegotiationOutcomeService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var result = await service.CaptureAsync(tenantId, ValidRequest(quoteId.Value, targetPrice: -1m));

        Assert.True(result.IsFailure);
        Assert.Equal(NegotiationOutcomeService.TargetPriceMustBeNonNegativeError, result.Error);
    }

    [Fact]
    public async Task CaptureAsync_accepts_a_null_target_price_when_no_target_was_ever_set()
    {
        // LineNegotiationStrategy.OpeningTarget is nullable for the identical reason (insufficient
        // benchmark data) — outcome capture must not be blocked on a target this module honestly
        // never had (Appendix C rule 9 "from day one").
        var tenantId = TenantId.New();
        var quoteId = await SeedQuoteAsync(tenantId);
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new NegotiationOutcomeService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var result = await service.CaptureAsync(tenantId, ValidRequest(quoteId.Value, targetPrice: null));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.TargetPrice);
    }

    [Fact]
    public async Task CaptureAsync_rejects_a_non_positive_final_price()
    {
        var tenantId = TenantId.New();
        var quoteId = await SeedQuoteAsync(tenantId);
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new NegotiationOutcomeService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var result = await service.CaptureAsync(tenantId, ValidRequest(quoteId.Value, finalPrice: 0m));

        Assert.True(result.IsFailure);
        Assert.Equal(NegotiationOutcomeService.FinalPriceMustBePositiveError, result.Error);
    }

    [Fact]
    public async Task CaptureAsync_rejects_a_negative_negotiation_duration()
    {
        var tenantId = TenantId.New();
        var quoteId = await SeedQuoteAsync(tenantId);
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new NegotiationOutcomeService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var result = await service.CaptureAsync(
            tenantId, ValidRequest(quoteId.Value, negotiationDurationDays: -1));

        Assert.True(result.IsFailure);
        Assert.Equal(NegotiationOutcomeService.NegotiationDurationDaysMustBeNonNegativeError, result.Error);
    }

    [Fact]
    public async Task CaptureAsync_rejects_an_empty_levers_used_list()
    {
        var tenantId = TenantId.New();
        var quoteId = await SeedQuoteAsync(tenantId);
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new NegotiationOutcomeService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var result = await service.CaptureAsync(
            tenantId, ValidRequest(quoteId.Value, leversUsed: Array.Empty<string>()));

        Assert.True(result.IsFailure);
        Assert.Equal(NegotiationOutcomeService.LeversUsedRequiredError, result.Error);
    }

    [Fact]
    public async Task CaptureAsync_rejects_a_levers_used_entry_outside_the_seven_canonical_levers()
    {
        var tenantId = TenantId.New();
        var quoteId = await SeedQuoteAsync(tenantId);
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new NegotiationOutcomeService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var result = await service.CaptureAsync(
            tenantId, ValidRequest(quoteId.Value, leversUsed: ["NotARealLever"]));

        Assert.True(result.IsFailure);
        Assert.Equal(NegotiationOutcomeService.LeversUsedInvalidError, result.Error);
    }

    [Fact]
    public async Task CaptureAsync_parses_lever_names_case_insensitively()
    {
        var tenantId = TenantId.New();
        var quoteId = await SeedQuoteAsync(tenantId);
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new NegotiationOutcomeService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var result = await service.CaptureAsync(tenantId, ValidRequest(quoteId.Value, leversUsed: ["volume"]));

        Assert.True(result.IsSuccess);
        Assert.Equal([NegotiationLeverType.Volume], result.Value.LeversUsed);
    }

    // Task E05/F03/US02/T02 (outcome-propagation): CaptureAsync persists whatever
    // savingsOpportunityId the caller supplies, but never validates it against Raffa.Savings —
    // this module cannot see that module at all (ADR-002). The actual cross-module propagation
    // (and 404 when the id is unknown) is Raffa.Api.NegotiationOutcomePropagationService's own
    // job, proved end to end by Raffa.IntegrationTests
    // .NegotiationOutcomePropagationEndToEndTests against the real, composed host.
    [Fact]
    public async Task CaptureAsync_persists_the_supplied_savings_opportunity_id_unvalidated()
    {
        var tenantId = TenantId.New();
        var quoteId = await SeedQuoteAsync(tenantId);
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new NegotiationOutcomeService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        // An id that names no real SavingsOpportunity anywhere — CaptureAsync still succeeds
        // (Raffa.Quotes structurally cannot check Raffa.Savings), recording the caller's intent
        // for Raffa.Api's own orchestrator to resolve afterward.
        var savingsOpportunityId = Guid.NewGuid();

        var result = await service.CaptureAsync(
            tenantId, ValidRequest(quoteId.Value, savingsOpportunityId: savingsOpportunityId));

        Assert.True(result.IsSuccess);
        Assert.Equal(new EntityId(savingsOpportunityId), result.Value.SavingsOpportunityId);

        using (tenantContext.BeginScope(tenantId))
        {
            await using var readDb = CreateAppContext(tenantContext);
            var persisted = await readDb.NegotiationOutcomes.SingleAsync(o => o.Id == result.Value.Id);
            Assert.Equal(new EntityId(savingsOpportunityId), persisted.SavingsOpportunityId);
        }
    }

    [Fact]
    public async Task CaptureAsync_leaves_savings_opportunity_id_null_when_the_caller_supplies_none()
    {
        var tenantId = TenantId.New();
        var quoteId = await SeedQuoteAsync(tenantId);
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new NegotiationOutcomeService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var result = await service.CaptureAsync(tenantId, ValidRequest(quoteId.Value, savingsOpportunityId: null));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.SavingsOpportunityId);
    }
}
