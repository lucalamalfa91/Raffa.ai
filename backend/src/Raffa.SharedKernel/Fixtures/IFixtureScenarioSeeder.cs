namespace Raffa.SharedKernel.Fixtures;

/// <summary>
/// AskRaffa 4.0 fixture mode (IMPLEMENTATION-PLAN.md §2, test/demo only, never Production): one
/// seeder per module that owns fixture data for the fixture tenant ("Helvetia Foods AG").
/// <c>POST /api/fixtures/reset</c> calls every registered seeder's <see cref="WipeAsync"/> in
/// descending <see cref="Order"/>, sweeps every remaining tenant row, then calls
/// <see cref="SeedAsync"/> in ascending <see cref="Order"/>. Register an implementation with
/// <c>services.AddScoped&lt;IFixtureScenarioSeeder, MySeeder&gt;()</c> from the module's own
/// <c>AddXxxFixtureSeeder()</c> extension, which the API host calls only when
/// <c>Fixtures:Enabled</c> is true.
/// </summary>
public interface IFixtureScenarioSeeder
{
    /// <summary>Seed order; see <see cref="FixtureSeedOrder"/> for the slots already taken.</summary>
    int Order { get; }

    /// <summary>Seeds the module's rows for <paramref name="scenario"/> (<see cref="FixtureScenarios"/>).</summary>
    Task SeedAsync(FixtureSeedContext context, string scenario, CancellationToken cancellationToken);

    /// <summary>
    /// Removes what the module stored for the fixture tenant outside a <c>tenant_id</c> table
    /// (blobs, caches...). Rows in tables with a <c>tenant_id</c> column are swept by the host after
    /// every seeder's wipe, so most modules have nothing to do here.
    /// </summary>
    Task WipeAsync(FixtureSeedContext context, CancellationToken cancellationToken);
}

/// <summary>The seed slots. Identity first (the workspace and its members), then contracts, then
/// the modules that read contracts.</summary>
public static class FixtureSeedOrder
{
    public const int IdentityWorkspace = 0;
    public const int DocumentsContracts = 100;
    public const int Renewals = 200;
    public const int Savings = 300;
    public const int Quotes = 400;
    public const int Chat = 500;
}

/// <summary>The two scenarios of the prototype's demo bar: "Da zero" and "Con dati".</summary>
public static class FixtureScenarios
{
    public const string Zero = "zero";
    public const string Full = "full";

    public static bool IsKnown(string? scenario) => scenario is Zero or Full;
}
