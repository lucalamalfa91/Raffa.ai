using Raffa.SharedKernel;
using Raffa.SharedKernel.Fixtures;

namespace Raffa.Api.Fixtures;

/// <summary>
/// <c>Fixtures:*</c> — AskRaffa 4.0 fixture mode (IMPLEMENTATION-PLAN.md §2), for the parity suite
/// and demos only. Off by default; the host refuses to start with it on in <c>Production</c>.
/// </summary>
internal sealed class FixtureOptions
{
    public const string SectionName = "Fixtures";

    public static readonly DateOnly DefaultNow = new(2026, 9, 8);

    public bool Enabled { get; set; }

    /// <summary>The scenario clock date when a request carries no <c>X-Raffa-Now</c> header.</summary>
    public DateOnly Now { get; set; } = DefaultNow;

    /// <summary>Folder holding the fixture JSON (<c>backend/fixtures/askraffa4</c>); found by walking
    /// up from the content root when unset.</summary>
    public string? DataPath { get; set; }

    /// <summary>Web origins allowed to call the API cross-origin in fixture mode (the parity web
    /// server, e.g. <c>http://localhost:5173</c>).</summary>
    public string[] CorsOrigins { get; set; } = [];

    public void ValidateOrThrow(string environmentName)
    {
        if (Enabled && string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Fixtures:Enabled is true in the Production environment. Fixture mode (seeded demo tenant, " +
                "scenario clock, fixture users) exists for the parity suite and demos only and must never run in Production.");
        }
    }

    public string ResolveDataPath(string contentRoot)
    {
        if (!string.IsNullOrWhiteSpace(DataPath))
        {
            return Path.GetFullPath(DataPath, contentRoot);
        }

        foreach (var start in new[] { contentRoot, AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                foreach (var candidate in new[] { Path.Combine(dir.FullName, "backend", "fixtures", "askraffa4"), Path.Combine(dir.FullName, "fixtures", "askraffa4") })
                {
                    if (File.Exists(Path.Combine(candidate, "contracts.json")))
                    {
                        return candidate;
                    }
                }
            }
        }

        throw new InvalidOperationException(
            "Fixture data folder not found (looked for backend/fixtures/askraffa4 above the content root). Set Fixtures:DataPath.");
    }
}

/// <summary>The fixture tenant and its two members (prototype: Luca = CPO, Marta = Buyer).</summary>
internal static class FixtureTenant
{
    public static readonly TenantId TenantId = new(Guid.Parse("4a5ec0de-a4a4-4f00-8000-000000000001"));

    public const string Name = "Helvetia Foods AG";

    public const string DefaultUserKey = "luca";

    public static readonly IReadOnlyList<FixtureUser> Users =
    [
        new("luca", "Luca Lamalfa", "Head of Procurement", "luca.lamalfa@helvetia-foods.example",
            FixtureIds.Deterministic(TenantId.Value, "subject:luca").ToString(), "admin", IsApprover: true),
        new("marta", "Marta Keller", "Buyer", "marta.keller@helvetia-foods.example",
            FixtureIds.Deterministic(TenantId.Value, "subject:marta").ToString(), "procurement", IsApprover: false),
    ];

    public static FixtureUser? Find(string? key) =>
        Users.FirstOrDefault(u => string.Equals(u.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase));
}
