using Raffa.Quotes.Application.Normalization;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Quotes.Tests;

/// <summary>
/// Proves the Definition of Done for task E05/F01/US02/T01 (sku-normalization; parent story
/// us-02-sku-normalization AC-1 "Normalize SKU/edition to the canonical product mapping", AC-2's
/// own "show unmatched SKUs" half). Three layers, mirroring
/// <c>QuoteLineExtractionServiceTests</c>' own "pure core, DB-aware wrapper, real-Postgres proof"
/// split:
///
/// <list type="bullet">
/// <item><see cref="SkuNormalizer"/> — pure text normalization, no database.</item>
/// <item><see cref="SkuNormalizationService.Apply"/> — the pure per-line matching rule against a
/// hand-built mapping dictionary, no database.</item>
/// <item><see cref="SkuNormalizationService.NormalizeAsync"/> — persistence, against a real
/// Postgres+RLS database (ADR-009's own "no in-memory provider" posture every other module
/// persistence test in this solution already takes).</item>
/// </list>
/// </summary>
public sealed class SkuNormalizationServiceTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("sku-ent-500", "SKU-ENT-500")]
    [InlineData("  SKU-ent-500  ", "SKU-ENT-500")]
    [InlineData("SKU   ENT     500", "SKU ENT 500")]
    [InlineData("sku\tent\n500", "SKU ENT 500")]
    public void Normalize_trims_collapses_whitespace_and_uppercases_without_touching_punctuation(
        string? raw, string? expected)
    {
        Assert.Equal(expected, SkuNormalizer.Normalize(raw));
    }

    [Fact]
    public void Normalize_does_not_treat_a_dash_as_whitespace_noise()
    {
        // AC-1's own "conservative normalization" rule (see SkuNormalizer's own doc comment): two
        // differently-punctuated codes must stay distinct, never silently folded together.
        Assert.NotEqual(SkuNormalizer.Normalize("SKU-ENT-500"), SkuNormalizer.Normalize("SKUENT500"));
    }

    [Fact]
    public void Apply_flags_a_missing_sku_as_not_applicable_but_still_normalizes_edition()
    {
        var line = NewLine(sku: null, edition: "Enterprise");
        var mappings = EmptyMappings();

        var status = SkuNormalizationService.Apply(line, mappings);

        Assert.Equal(SkuMatchStatus.NotApplicable, status);
        Assert.Equal(SkuMatchStatus.NotApplicable, line.MatchStatus);
        Assert.Null(line.NormalizedSku);
        Assert.Equal("ENTERPRISE", line.NormalizedEdition);
    }

    [Fact]
    public void Apply_flags_a_present_sku_as_unmatched_when_no_mapping_exists_for_this_tenant()
    {
        var line = NewLine(sku: "SKU-999", edition: null);
        var mappings = EmptyMappings();

        var status = SkuNormalizationService.Apply(line, mappings);

        Assert.Equal(SkuMatchStatus.Unmatched, status);
        Assert.Equal("SKU-999", line.NormalizedSku);
    }

    [Fact]
    public void Apply_matches_case_and_whitespace_insensitively_against_an_existing_mapping()
    {
        var line = NewLine(sku: "  sku-100  ", edition: "Enterprise");
        var mapping = new SkuProductMapping
        {
            TenantId = TenantId.New(),
            NormalizedSku = "SKU-100",
            CanonicalSku = "SKU-100",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var mappings = new Dictionary<string, SkuProductMapping>(StringComparer.Ordinal)
        {
            [mapping.NormalizedSku] = mapping,
        };

        var status = SkuNormalizationService.Apply(line, mappings);

        Assert.Equal(SkuMatchStatus.Matched, status);
        Assert.Equal("SKU-100", line.NormalizedSku);
    }

    [Fact]
    public async Task NormalizeAsync_persists_matched_unmatched_and_not_applicable_lines_for_one_quote()
    {
        await using var harness = await Harness.CreateAsync();

        var tenantId = TenantId.New();
        var quoteId = EntityId.New();
        var now = DateTimeOffset.UtcNow;

        await using (var db = harness.CreateDbContext())
        using (harness.TenantContext.BeginScope(tenantId))
        {
            db.SkuProductMappings.Add(new SkuProductMapping
            {
                TenantId = tenantId,
                NormalizedSku = "SKU-100",
                CanonicalSku = "SKU-100",
                CanonicalProductName = "Enterprise Suite",
                CreatedAt = now,
            });

            db.QuoteLines.Add(new QuoteLine
            {
                TenantId = tenantId,
                QuoteId = quoteId,
                Sku = "sku-100",
                Description = "Matches an existing mapping",
                CreatedAt = now,
            });
            db.QuoteLines.Add(new QuoteLine
            {
                TenantId = tenantId,
                QuoteId = quoteId,
                Sku = "SKU-UNKNOWN",
                Description = "No mapping exists for this SKU yet",
                CreatedAt = now,
            });
            db.QuoteLines.Add(new QuoteLine
            {
                TenantId = tenantId,
                QuoteId = quoteId,
                Sku = null,
                Description = "Professional services -- not SKU-level",
                CreatedAt = now,
            });

            await db.SaveChangesAsync();
        }

        SkuNormalizationOutcome outcome;
        await using (var db = harness.CreateDbContext())
        using (harness.TenantContext.BeginScope(tenantId))
        {
            var service = new SkuNormalizationService(db);
            outcome = await service.NormalizeAsync(tenantId, quoteId);
            await db.SaveChangesAsync();
        }

        Assert.Equal(3, outcome.LineCount);
        Assert.Equal(1, outcome.MatchedCount);
        Assert.Equal(1, outcome.UnmatchedCount);
        Assert.Equal(1, outcome.NotApplicableCount);

        await using var readDb = harness.CreateDbContext();
        using var readScope = harness.TenantContext.BeginScope(tenantId);

        var lines = await readDb.QuoteLines.Where(l => l.QuoteId == quoteId).ToListAsync();

        var matched = Assert.Single(lines, l => l.Sku == "sku-100");
        Assert.Equal(SkuMatchStatus.Matched, matched.MatchStatus);
        Assert.Equal("SKU-100", matched.NormalizedSku);

        var unmatched = Assert.Single(lines, l => l.Sku == "SKU-UNKNOWN");
        Assert.Equal(SkuMatchStatus.Unmatched, unmatched.MatchStatus);

        var notApplicable = Assert.Single(lines, l => l.Sku == null);
        Assert.Equal(SkuMatchStatus.NotApplicable, notApplicable.MatchStatus);
        Assert.Null(notApplicable.NormalizedSku);
    }

    [Fact]
    public async Task NormalizeAsync_is_re_runnable_and_upgrades_a_line_to_matched_once_a_mapping_is_added()
    {
        // Proves the "recalculate" shape task E05/F01/US02/T02 depends on: a mapping learned after
        // the fact resolves a line that was unmatched on the first pass, with no other input
        // changing.
        await using var harness = await Harness.CreateAsync();

        var tenantId = TenantId.New();
        var quoteId = EntityId.New();
        var now = DateTimeOffset.UtcNow;

        await using (var db = harness.CreateDbContext())
        using (harness.TenantContext.BeginScope(tenantId))
        {
            db.QuoteLines.Add(new QuoteLine
            {
                TenantId = tenantId,
                QuoteId = quoteId,
                Sku = "SKU-200",
                Description = "Not yet mapped",
                CreatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        await using (var firstPassDb = harness.CreateDbContext())
        using (harness.TenantContext.BeginScope(tenantId))
        {
            var outcome = await new SkuNormalizationService(firstPassDb).NormalizeAsync(tenantId, quoteId);
            await firstPassDb.SaveChangesAsync();
            Assert.Equal(1, outcome.UnmatchedCount);
        }

        await using (var mappingDb = harness.CreateDbContext())
        using (harness.TenantContext.BeginScope(tenantId))
        {
            mappingDb.SkuProductMappings.Add(new SkuProductMapping
            {
                TenantId = tenantId,
                NormalizedSku = "SKU-200",
                CanonicalSku = "SKU-200",
                CreatedAt = now,
            });
            await mappingDb.SaveChangesAsync();
        }

        await using (var secondPassDb = harness.CreateDbContext())
        using (harness.TenantContext.BeginScope(tenantId))
        {
            var outcome = await new SkuNormalizationService(secondPassDb).NormalizeAsync(tenantId, quoteId);
            await secondPassDb.SaveChangesAsync();
            Assert.Equal(1, outcome.MatchedCount);
            Assert.Equal(0, outcome.UnmatchedCount);
        }
    }

    [Fact]
    public async Task A_different_tenant_cannot_see_another_tenants_sku_product_mapping()
    {
        // ADR-009: same cross-tenant proof QuoteRlsCrossTenantIsolationTests already gives
        // quote/quote_extraction_job/quote_line, extended to this task's own new table. Runs
        // through a dedicated, deliberately unprivileged role -- the Testcontainers bootstrap role
        // is always a superuser and would bypass row security unconditionally.
        await using var harness = await Harness.CreateAsync();

        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var now = DateTimeOffset.UtcNow;

        await using (var db = harness.CreateDbContext())
        using (harness.TenantContext.BeginScope(tenantA))
        {
            db.SkuProductMappings.Add(new SkuProductMapping
            {
                TenantId = tenantA,
                NormalizedSku = "OWNED-BY-A",
                CanonicalSku = "OWNED-BY-A",
                CreatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = harness.CreateDbContext())
        using (harness.TenantContext.BeginScope(tenantB))
        {
            db.SkuProductMappings.Add(new SkuProductMapping
            {
                TenantId = tenantB,
                NormalizedSku = "OWNED-BY-B",
                CanonicalSku = "OWNED-BY-B",
                CreatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        await using var readAsTenantA = harness.CreateDbContext();
        using var scopeA = harness.TenantContext.BeginScope(tenantA);

        var visible = await readAsTenantA.SkuProductMappings.ToListAsync();

        Assert.Single(visible);
        Assert.Equal("OWNED-BY-A", Assert.Single(visible).NormalizedSku);
    }

    private static QuoteLine NewLine(string? sku, string? edition) => new()
    {
        TenantId = TenantId.New(),
        QuoteId = EntityId.New(),
        Sku = sku,
        Edition = edition,
        Description = "Test line",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Dictionary<string, SkuProductMapping> EmptyMappings() => new(StringComparer.Ordinal);

    /// <summary>Shared Testcontainers + unprivileged-role bootstrap for this file's persistence
    /// tests -- same shape as <c>QuoteUploadServiceTests</c>/<c>QuoteRlsCrossTenantIsolationTests</c>,
    /// factored into one disposable helper since every test method below needs it identically (a
    /// fresh container per test method, same as every other Testcontainers-backed test class in
    /// this project).</summary>
    private sealed class Harness : IAsyncDisposable
    {
        private const string AppRoleName = "raffa_sku_normalization_app";
        private const string AppRolePassword = "raffa_sku_normalization_app_test_password";

        private readonly PostgreSqlContainer _postgres;
        private readonly string _appConnectionString;

        public ITenantContext TenantContext { get; } = new TenantContext();

        private Harness(PostgreSqlContainer postgres, string appConnectionString)
        {
            _postgres = postgres;
            _appConnectionString = appConnectionString;
        }

        public static async Task<Harness> CreateAsync()
        {
            var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
            await postgres.StartAsync();

            var adminOptions = new DbContextOptionsBuilder<QuotesDbContext>();
            QuotesDbContextOptions.Configure(adminOptions, postgres.GetConnectionString());

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

            var appConnectionString = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
            {
                Username = AppRoleName,
                Password = AppRolePassword,
            }.ConnectionString;

            return new Harness(postgres, appConnectionString);
        }

        public QuotesDbContext CreateDbContext()
        {
            var optionsBuilder = new DbContextOptionsBuilder<QuotesDbContext>();
            QuotesDbContextOptions.Configure(optionsBuilder, _appConnectionString, TenantContext);
            return new QuotesDbContext(optionsBuilder.Options);
        }

        public ValueTask DisposeAsync() => _postgres.DisposeAsync();
    }
}
