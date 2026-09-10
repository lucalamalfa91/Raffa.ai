using System.Net;
using System.Text.Json;

namespace Raffa.IntegrationTests;

/// <summary>
/// Proves the Definition of done for task E10/F01/US01/T01 (seed-demo-fixture) and its parent
/// story us-01-seed-demo-db, verbatim: "After e09, running the seed against `raffa_demo` makes
/// `GET /api/savings` (with the demo tenant header) return at least one fixture-backed
/// opportunity." Driven against the real, composed <c>Raffa.Api</c> host and a real, migrated
/// Postgres+RLS database (see <see cref="DemoFixtureSeedIntegrationFixture"/>) — the checked-in
/// <c>backend/scripts/demo-fixture-seed.sql</c> is read from disk and applied exactly as
/// <c>.github/workflows/seed-demo-fixture.yml</c> applies it in `dev`/`demo`, never re-typed here.
/// Reuses <see cref="R1EndToEndTests"/>'s <c>GetAsync</c>/<c>ParseAsync</c> helpers rather than
/// duplicating them — the same cross-class reuse <see cref="R3EndToEndTests"/>/
/// <see cref="R4EndToEndTests"/> already established (they are generic HTTP plumbing, not
/// R1-specific).
/// </summary>
public sealed class DemoFixtureSeedEndToEndTests : IClassFixture<DemoFixtureSeedIntegrationFixture>
{
    private readonly DemoFixtureSeedIntegrationFixture _fixture;

    public DemoFixtureSeedEndToEndTests(DemoFixtureSeedIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Seed_makes_GET_api_savings_return_fixture_backed_opportunities_for_the_demo_tenant()
    {
        var client = _fixture.CreateClient();
        var demoTenantId = Guid.Parse(DemoFixtureSeedIntegrationFixture.DemoTenantId);

        var response = await R1EndToEndTests.GetAsync(client, "/api/savings", demoTenantId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await R1EndToEndTests.ParseAsync(response);
        var items = body.GetProperty("items").EnumerateArray().ToList();

        // The task's own Definition of done, verbatim: "at least one fixture-backed opportunity".
        Assert.True(items.Count >= 1, "expected at least one seeded savings opportunity for the demo tenant");
        Assert.Equal(3, body.GetProperty("totalCount").GetInt32());

        // "ADR-001 fixture benchmark rows" made concrete: every seeded row is traceable to a real
        // Raffa.Benchmark.Fixtures.FixtureBenchmarkAdapter.Catalog entry (AWS EC2, Zoom,
        // Snowflake — see demo-fixture-seed.sql's own header comment), spanning all three
        // SavingsProvenanceClassifier confidence tiers rather than one flat, arbitrary number.
        var confidenceLevels = items.Select(item => item.GetProperty("confidenceLevel").GetString()).ToList();
        Assert.Contains("High", confidenceLevels);
        Assert.Contains("Medium", confidenceLevels);
        Assert.Contains("Low", confidenceLevels);
        Assert.Contains(items, item => (item.GetProperty("type").GetString() ?? "").Contains("AWS", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(items, item => item.GetProperty("status").GetString() == "InProgress");
        Assert.Contains(items, item => item.GetProperty("owner").GetString() == "Alex Procurement");
    }

    [Fact]
    public async Task Seed_script_is_idempotent_a_second_apply_does_not_duplicate_rows()
    {
        // The fixture's own InitializeAsync already applied the script once; story
        // us-01-seed-demo-db AC-1's "repeatable" means a second apply is safe too.
        await _fixture.ReapplySeedAsync();

        var client = _fixture.CreateClient();
        var demoTenantId = Guid.Parse(DemoFixtureSeedIntegrationFixture.DemoTenantId);

        var response = await R1EndToEndTests.GetAsync(client, "/api/savings", demoTenantId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await R1EndToEndTests.ParseAsync(response);

        // Every INSERT in demo-fixture-seed.sql is ON CONFLICT (id) DO NOTHING against a fixed
        // id, so a second apply inserts nothing new.
        Assert.Equal(3, body.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Seed_data_is_not_visible_to_a_different_tenant()
    {
        var client = _fixture.CreateClient();
        var otherTenantId = Guid.NewGuid();

        var response = await R1EndToEndTests.GetAsync(client, "/api/savings", otherTenantId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await R1EndToEndTests.ParseAsync(response);

        // AC-4 ("seed does not disable RLS for the API identity") made observable: a tenant this
        // script never wrote under sees none of the seeded rows — application-scoped
        // (SavingsOpportunityService.ListAsync's own WHERE tenant_id) and RLS-backstopped
        // (ADR-009), the same boundary every other tenant in this codebase gets.
        Assert.Equal(0, body.GetProperty("totalCount").GetInt32());
    }
}
