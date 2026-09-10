using Raffa.Benchmark.Fixtures;
using Raffa.Quotes.Application.Assessment;
using Raffa.Quotes.Application.Normalization;
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
/// Proves the Definition of Done for task E05/F01/US02/T02 (sku-recalculate; parent story
/// us-02-sku-normalization AC-2's "...and allow manual product mapping" half, AC-3 "Re-run
/// assessment after mapping correction"). Two layers, mirroring
/// <c>SkuNormalizationServiceTests</c>'s own "pure core, DB-aware wrapper, real-Postgres proof"
/// split:
///
/// <list type="bullet">
/// <item><see cref="SkuMappingService.ApplyCorrection"/> — the pure per-correction upsert rule
/// against a hand-built existing-mapping-or-null input, no database.</item>
/// <item><see cref="SkuMappingService.RecalculateAsync"/> — persistence, composed
/// normalize/re-assess, and audit, against a real Postgres+RLS database (ADR-009's own "no
/// in-memory provider" posture every other module persistence test in this solution already
/// takes) and the real <see cref="FixtureBenchmarkAdapter"/>, never a stub/mock of the Benchmark
/// Service (mirrors <c>MarketAssessmentServiceTests</c>'s own posture).</item>
/// </list>
/// </summary>
public sealed class SkuMappingServiceTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_sku_recalculate_app";
    private const string AppRolePassword = "raffa_sku_recalculate_app_test_password";

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

    private SkuMappingService CreateService(
        QuotesDbContext db, ITenantContext tenantContext, IAuditWriter auditWriter, IClock? clock = null) =>
        new(
            db,
            tenantContext,
            clock ?? new FixedClock(DateTimeOffset.UtcNow),
            new SkuNormalizationService(db),
            new MarketAssessmentService(db, new FixtureBenchmarkAdapter(), tenantContext),
            auditWriter);

    private async Task<EntityId> SeedQuoteAsync(
        TenantId tenantId,
        string? supplier = null,
        string? currency = null,
        string? geography = null,
        DateOnly? purchaseDate = null)
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
            Supplier = supplier,
            Currency = currency,
            Geography = geography,
            PurchaseDate = purchaseDate,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return quoteId;
    }

    private async Task<EntityId> SeedLineAsync(
        TenantId tenantId, EntityId quoteId, string sku, string description = "Test line",
        decimal? quantity = null, string? term = null, decimal? unitPrice = null)
    {
        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(tenantId);
        await using var db = CreateAppContext(tenantContext);

        var line = new QuoteLine
        {
            TenantId = tenantId,
            QuoteId = quoteId,
            Sku = sku,
            Description = description,
            Quantity = quantity,
            Term = term,
            UnitPrice = unitPrice,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.QuoteLines.Add(line);
        await db.SaveChangesAsync();

        return line.Id;
    }

    // ----- Pure core: SkuMappingService.ApplyCorrection -----

    [Fact]
    public void ApplyCorrection_builds_a_new_mapping_when_none_exists()
    {
        var tenantId = TenantId.New();
        var now = DateTimeOffset.UtcNow;
        var correction = new SkuMappingCorrection("sku-100", "  enterprise  ", " SKU-100 ", "Enterprise", "Suite");

        var mapping = SkuMappingService.ApplyCorrection(null, "SKU-100", correction, tenantId, now);

        Assert.Equal(tenantId, mapping.TenantId);
        Assert.Equal("SKU-100", mapping.NormalizedSku);
        Assert.Equal("ENTERPRISE", mapping.NormalizedEdition);
        Assert.Equal("SKU-100", mapping.CanonicalSku);
        Assert.Equal("Enterprise", mapping.CanonicalEdition);
        Assert.Equal("Suite", mapping.CanonicalProductName);
        Assert.Equal(now, mapping.CreatedAt);
    }

    [Fact]
    public void ApplyCorrection_treats_blank_optional_fields_as_null_rather_than_empty_strings()
    {
        var correction = new SkuMappingCorrection("sku-100", null, "SKU-100", "   ", "");

        var mapping = SkuMappingService.ApplyCorrection(
            null, "SKU-100", correction, TenantId.New(), DateTimeOffset.UtcNow);

        Assert.Null(mapping.NormalizedEdition);
        Assert.Null(mapping.CanonicalEdition);
        Assert.Null(mapping.CanonicalProductName);
    }

    [Fact]
    public void ApplyCorrection_updates_an_existing_mapping_in_place_rather_than_replacing_it()
    {
        var tenantId = TenantId.New();
        var originalCreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var existing = new SkuProductMapping
        {
            TenantId = tenantId,
            NormalizedSku = "SKU-100",
            CanonicalSku = "OLD-SKU",
            CanonicalProductName = "Old Name",
            CreatedAt = originalCreatedAt,
        };
        var correction = new SkuMappingCorrection("sku-100", "Enterprise", "NEW-SKU", null, "New Name");

        var updated = SkuMappingService.ApplyCorrection(
            existing, "SKU-100", correction, tenantId, DateTimeOffset.UtcNow);

        // Same instance, not a replacement -- the caller's dictionary/change-tracker entry for
        // `existing` stays valid.
        Assert.Same(existing, updated);
        Assert.Equal("NEW-SKU", updated.CanonicalSku);
        Assert.Equal("New Name", updated.CanonicalProductName);
        Assert.Equal("ENTERPRISE", updated.NormalizedEdition);
        // CreatedAt is never touched by an update -- this is a correction, not a re-creation.
        Assert.Equal(originalCreatedAt, updated.CreatedAt);
    }

    // ----- Persistence: SkuMappingService.RecalculateAsync -----

    [Fact]
    public async Task RecalculateAsync_fails_when_the_quote_does_not_exist_for_this_tenant()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        await using var db = CreateAppContext(tenantContext);
        var service = CreateService(db, tenantContext, auditWriter);

        var result = await service.RecalculateAsync(tenantId, EntityId.New(), corrections: null);

        Assert.True(result.IsFailure);
        Assert.Equal(SkuMappingService.QuoteNotFoundError, result.Error);
        Assert.Empty(auditWriter.Written);
    }

    [Fact]
    public async Task RecalculateAsync_fails_when_the_quote_belongs_to_a_different_tenant()
    {
        var ownerTenant = TenantId.New();
        var otherTenant = TenantId.New();
        var quoteId = await SeedQuoteAsync(ownerTenant);

        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = CreateService(db, tenantContext, new RecordingAuditWriter());

        var result = await service.RecalculateAsync(otherTenant, quoteId, corrections: null);

        Assert.True(result.IsFailure);
        Assert.Equal(SkuMappingService.QuoteNotFoundError, result.Error);
    }

    [Fact]
    public async Task RecalculateAsync_rejects_a_blank_sku_before_writing_anything()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        await using var db = CreateAppContext(tenantContext);
        var service = CreateService(db, tenantContext, auditWriter);

        // No quote seeded at all -- validation must fail before any existence check/query.
        var result = await service.RecalculateAsync(
            tenantId, EntityId.New(), [new SkuMappingCorrection("  ", null, "SKU-100", null, null)]);

        Assert.True(result.IsFailure);
        Assert.Equal(SkuMappingService.SkuRequiredError, result.Error);
        Assert.Empty(auditWriter.Written);
    }

    [Fact]
    public async Task RecalculateAsync_rejects_a_blank_canonical_sku_before_writing_anything()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        await using var db = CreateAppContext(tenantContext);
        var service = CreateService(db, tenantContext, auditWriter);

        var result = await service.RecalculateAsync(
            tenantId, EntityId.New(), [new SkuMappingCorrection("SKU-100", null, "   ", null, null)]);

        Assert.True(result.IsFailure);
        Assert.Equal(SkuMappingService.CanonicalSkuRequiredError, result.Error);
        Assert.Empty(auditWriter.Written);
    }

    [Fact]
    public async Task RecalculateAsync_creates_a_mapping_upgrades_the_line_and_writes_an_audit_entry()
    {
        var tenantId = TenantId.New();
        var quoteId = await SeedQuoteAsync(tenantId);
        var lineId = await SeedLineAsync(tenantId, quoteId, sku: "sku-200", description: "Not yet mapped");

        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        var now = new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);
        await using var db = CreateAppContext(tenantContext);
        var service = CreateService(db, tenantContext, auditWriter, new FixedClock(now));

        var result = await service.RecalculateAsync(
            tenantId, quoteId,
            [new SkuMappingCorrection("SKU-200", "Enterprise", "SKU-200", "Enterprise", "Widget Pro")]);

        Assert.True(result.IsSuccess);
        var recalculation = result.Value;
        Assert.Equal(quoteId, recalculation.QuoteId);
        Assert.Equal(1, recalculation.MappingsAppliedCount);
        Assert.Equal(1, recalculation.NormalizationOutcome.MatchedCount);
        Assert.Equal(0, recalculation.NormalizationOutcome.UnmatchedCount);
        Assert.Empty(recalculation.UnmatchedLines);

        var auditEntry = Assert.Single(auditWriter.Written);
        Assert.Equal(tenantId, auditEntry.TenantId);
        Assert.Equal("quote.sku_mapping_recalculated", auditEntry.Action);
        Assert.Equal("quote", auditEntry.ResourceType);
        Assert.Equal(quoteId.Value.ToString(), auditEntry.ResourceId);
        Assert.Equal(now, auditEntry.Timestamp);
        Assert.Contains("mappingsApplied=1", auditEntry.Detail);
        Assert.Contains("matched=1", auditEntry.Detail);

        using (tenantContext.BeginScope(tenantId))
        {
            await using var readDb = CreateAppContext(tenantContext);

            var mapping = await readDb.SkuProductMappings.SingleAsync(m => m.NormalizedSku == "SKU-200");
            Assert.Equal("SKU-200", mapping.CanonicalSku);
            Assert.Equal("Widget Pro", mapping.CanonicalProductName);

            var line = await readDb.QuoteLines.SingleAsync(l => l.Id == lineId);
            Assert.Equal(SkuMatchStatus.Matched, line.MatchStatus);
            Assert.Equal("SKU-200", line.NormalizedSku);
        }
    }

    [Fact]
    public async Task RecalculateAsync_updates_an_existing_mapping_instead_of_creating_a_duplicate()
    {
        var tenantId = TenantId.New();
        var quoteId = await SeedQuoteAsync(tenantId);
        await SeedLineAsync(tenantId, quoteId, sku: "SKU-300");

        var tenantContext = new TenantContext();

        await using (var firstDb = CreateAppContext(tenantContext))
        {
            var firstService = CreateService(firstDb, tenantContext, new RecordingAuditWriter());
            var firstResult = await firstService.RecalculateAsync(
                tenantId, quoteId, [new SkuMappingCorrection("SKU-300", null, "SKU-300", null, "First Name")]);
            Assert.True(firstResult.IsSuccess);
        }

        await using (var secondDb = CreateAppContext(tenantContext))
        {
            var secondService = CreateService(secondDb, tenantContext, new RecordingAuditWriter());
            // Re-submitting a correction for the same (tenant, normalized SKU) must update the row
            // already on file, never attempt a second insert -- the unique (tenant_id,
            // normalized_sku) index (SkuProductMappingConfiguration) would otherwise reject it.
            var secondResult = await secondService.RecalculateAsync(
                tenantId, quoteId, [new SkuMappingCorrection("sku-300", null, "SKU-300", null, "Corrected Name")]);
            Assert.True(secondResult.IsSuccess);
        }

        using (tenantContext.BeginScope(tenantId))
        {
            await using var readDb = CreateAppContext(tenantContext);
            var mappings = await readDb.SkuProductMappings.Where(m => m.NormalizedSku == "SKU-300").ToListAsync();
            var mapping = Assert.Single(mappings);
            Assert.Equal("Corrected Name", mapping.CanonicalProductName);
        }
    }

    [Fact]
    public async Task RecalculateAsync_with_no_corrections_is_a_pure_refresh_that_writes_no_mapping()
    {
        var tenantId = TenantId.New();
        var quoteId = await SeedQuoteAsync(tenantId);
        await SeedLineAsync(tenantId, quoteId, sku: "SKU-400");

        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        await using var db = CreateAppContext(tenantContext);
        var service = CreateService(db, tenantContext, auditWriter);

        var result = await service.RecalculateAsync(tenantId, quoteId, corrections: null);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.MappingsAppliedCount);
        Assert.Equal(1, result.Value.NormalizationOutcome.UnmatchedCount);
        var unmatched = Assert.Single(result.Value.UnmatchedLines);
        Assert.Equal("SKU-400", unmatched.Sku);
        Assert.Equal("SKU-400", unmatched.NormalizedSku);

        // A pure refresh still writes an audit entry (it is a real, if uneventful, recalculate
        // call) but never a SkuProductMapping row.
        Assert.Single(auditWriter.Written);

        using (tenantContext.BeginScope(tenantId))
        {
            await using var readDb = CreateAppContext(tenantContext);
            Assert.Empty(await readDb.SkuProductMappings.ToListAsync());
        }
    }

    [Fact]
    public async Task RecalculateAsync_only_reports_the_lines_still_unmatched_after_a_partial_correction()
    {
        var tenantId = TenantId.New();
        var quoteId = await SeedQuoteAsync(tenantId);
        await SeedLineAsync(tenantId, quoteId, sku: "SKU-500", description: "Gets corrected");
        var stillUnmatchedLineId = await SeedLineAsync(tenantId, quoteId, sku: "SKU-501", description: "Stays unmatched");

        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = CreateService(db, tenantContext, new RecordingAuditWriter());

        var result = await service.RecalculateAsync(
            tenantId, quoteId, [new SkuMappingCorrection("SKU-500", null, "SKU-500", null, null)]);

        Assert.True(result.IsSuccess);
        var stillUnmatched = Assert.Single(result.Value.UnmatchedLines);
        Assert.Equal(stillUnmatchedLineId, stillUnmatched.QuoteLineId);
        Assert.Equal("SKU-501", stillUnmatched.Sku);
    }

    [Fact]
    public async Task RecalculateAsync_a_mapping_learned_on_one_quote_resolves_a_different_quote_on_its_own_next_refresh()
    {
        // SkuProductMapping is tenant-scoped, not quote-scoped (see that type's own doc comment) --
        // a correction made while looking at quote A must resolve quote B's own matching line too,
        // the next time quote B is (re)normalized, with no correction ever submitted "for quote B".
        var tenantId = TenantId.New();
        var quoteA = await SeedQuoteAsync(tenantId);
        var quoteB = await SeedQuoteAsync(tenantId);
        await SeedLineAsync(tenantId, quoteA, sku: "SKU-600");
        var lineOnQuoteB = await SeedLineAsync(tenantId, quoteB, sku: "sku-600");

        var tenantContext = new TenantContext();

        await using (var dbForA = CreateAppContext(tenantContext))
        {
            var serviceForA = CreateService(dbForA, tenantContext, new RecordingAuditWriter());
            var resultForA = await serviceForA.RecalculateAsync(
                tenantId, quoteA, [new SkuMappingCorrection("SKU-600", null, "SKU-600", null, null)]);
            Assert.True(resultForA.IsSuccess);
        }

        await using (var dbForB = CreateAppContext(tenantContext))
        {
            var serviceForB = CreateService(dbForB, tenantContext, new RecordingAuditWriter());
            // Pure refresh for quote B -- no corrections of its own.
            var resultForB = await serviceForB.RecalculateAsync(tenantId, quoteB, corrections: null);

            Assert.True(resultForB.IsSuccess);
            Assert.Equal(0, resultForB.Value.MappingsAppliedCount);
            Assert.Empty(resultForB.Value.UnmatchedLines);
        }

        using (tenantContext.BeginScope(tenantId))
        {
            await using var readDb = CreateAppContext(tenantContext);
            var line = await readDb.QuoteLines.SingleAsync(l => l.Id == lineOnQuoteB);
            Assert.Equal(SkuMatchStatus.Matched, line.MatchStatus);
        }
    }

    [Fact]
    public async Task RecalculateAsync_returns_an_assessed_line_once_the_correction_resolves_its_sku()
    {
        // Ties AC-2 and AC-3 together end to end: the same catalog row
        // MarketAssessmentServiceTests/R4EndToEndTests already exercise (Salesforce, 12-month,
        // P25/P50/P75 = 1500/1800/2100, sample size 512) -- the correction does not change the
        // benchmark match itself (Sku is optional in BenchmarkQuery, see
        // MarketAssessmentQueryBuilder's own doc comment), but proves the full
        // correct-then-recalculate-then-assess chain returns a real, non-QuoteDataUnresolved
        // assessment in one call.
        var tenantId = TenantId.New();
        var quoteId = await SeedQuoteAsync(
            tenantId, supplier: "Salesforce", currency: "USD", geography: "US",
            purchaseDate: new DateOnly(2026, 7, 1));
        await SeedLineAsync(
            tenantId, quoteId, sku: "SKU-700", description: "Sales Cloud Enterprise",
            quantity: 100m, term: "12 months", unitPrice: 2300m);

        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = CreateService(db, tenantContext, new RecordingAuditWriter());

        var result = await service.RecalculateAsync(
            tenantId, quoteId, [new SkuMappingCorrection("SKU-700", null, "SKU-700", null, "Sales Cloud Enterprise")]);

        Assert.True(result.IsSuccess);
        var assessedLine = Assert.Single(result.Value.Assessment.Lines);
        Assert.Equal(MarketAssessmentStatus.Assessed, assessedLine.Status);
        Assert.Equal(MarketPosition.AboveMarket, assessedLine.Position);
        Assert.NotNull(assessedLine.TargetSaving);
    }
}
