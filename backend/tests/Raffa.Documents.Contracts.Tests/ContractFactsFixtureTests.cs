using Raffa.Documents.Contracts.Infrastructure.Fixtures;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Fixtures;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>AskRaffa 4.0: the fixture contracts (backend/fixtures/askraffa4/contracts.json, converted
/// from the prototype's raffa-data.js) become <c>ContractFacts</c> with real ids, ISO dates, their
/// evidence and — for the sample document — the two fields to verify.</summary>
public sealed class ContractFactsFixtureTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("4a5ec0de-a4a4-4f00-8000-000000000001"));

    private static FixtureSeedContext Context() =>
        new(Tenant, "Helvetia Foods AG", [], new DateTimeOffset(2026, 9, 8, 9, 0, 0, TimeSpan.Zero), DataDirectory());

    private static string DataDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "backend", "fixtures", "askraffa4");
            if (File.Exists(Path.Combine(candidate, "contracts.json")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("backend/fixtures/askraffa4 not found");
    }

    [Fact]
    public void DocuSign_facts_match_the_prototype()
    {
        var facts = DocumentsContractsFixtureSeeder.BuildFacts(Context(), 4);

        Assert.Equal("DocuSign", facts.Supplier);
        Assert.Equal(FixtureIds.Contract(Tenant, 4).Value, facts.ContractId);
        Assert.Equal(new DateOnly(2026, 10, 2), facts.NoticeDeadline);
        Assert.Equal(new DateOnly(2026, 10, 31), facts.EndDate);
        Assert.Equal(58000m, facts.Spend);
        Assert.Equal("Productivity", facts.Category);
        Assert.Equal(3, facts.Deep.Products.Count);
        Assert.Equal(2, facts.Deep.Push.Count);
        Assert.Contains("sf_renew", facts.Evidence.Keys);
        Assert.Empty(facts.ReviewFields);
        Assert.Null(facts.ValidatedAt);
    }

    [Fact]
    public void Salesforce_carries_the_two_fields_to_verify_and_its_quotes()
    {
        var facts = DocumentsContractsFixtureSeeder.BuildFacts(Context(), 1);

        Assert.Equal(["Renewal term", "Price uplift at renewal"], facts.ReviewFields.Select(f => f.Label));
        Assert.Equal(new DateOnly(2026, 10, 18), facts.NoticeDeadline);
        Assert.All(new[] { "sf_term", "sf_renew", "sf_liab", "sf_uplift", "sf_renewterm" }, k => Assert.Contains(k, facts.Evidence.Keys));
        Assert.Equal("automatically renew for successive twelve (12) month periods", facts.Evidence["sf_renew"].Quote);
    }

    [Fact]
    public void Contract_ids_are_deterministic_per_tenant()
    {
        Assert.Equal(FixtureIds.Contract(Tenant, 2), FixtureIds.Contract(Tenant, 2));
        Assert.NotEqual(FixtureIds.Contract(Tenant, 2), FixtureIds.Contract(Tenant, 3));
        Assert.NotEqual(FixtureIds.Contract(Tenant, 2), FixtureIds.Contract(TenantId.New(), 2));
        // RFC 4122 version 5 / variant bits.
        var text = FixtureIds.Contract(Tenant, 1).Value.ToString();
        Assert.Equal('5', text[14]);
    }

    [Theory]
    [InlineData("18 Oct 2026", 2026, 10, 18)]
    [InlineData("01 Mar 2024", 2024, 3, 1)]
    public void Parses_prototype_dates(string text, int y, int m, int d) =>
        Assert.Equal(new DateOnly(y, m, d), DocumentsContractsFixtureSeeder.ParseDate(text));
}
