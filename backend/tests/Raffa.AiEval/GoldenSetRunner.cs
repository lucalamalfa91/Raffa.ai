using Raffa.AiEval.TenantFixtures;

namespace Raffa.AiEval;

/// <summary>
/// Runs the whole golden set exactly once per <c>dotnet test</c> invocation, then hands the scored
/// outcomes to every test case (task E13/F06/US01/T02, ask-golden-set).
///
/// <para>
/// One host per tenant fixture, three hosts total, each started once and reused for every question
/// against that workspace — starting a <c>WebApplicationFactory</c> per case would multiply an
/// already-slow host boot by 40+. Cases run strictly sequentially: the recording
/// <c>IAiGateway</c> / <c>IAuditWriter</c> doubles are per-host, and the golden set's whole point
/// is reproducibility, so overlapping turns on one host would make the "zero gateway calls" and
/// "one audit row" observations ambiguous.
/// </para>
///
/// <para>
/// The report is written at the end of <see cref="InitializeAsync"/>, before a single assertion
/// runs, so <c>reports/last-run.md</c> exists and is complete even when the suite goes red — which
/// is precisely when a reader needs it.
/// </para>
/// </summary>
public sealed class GoldenSetRunner : IAsyncLifetime
{
    private readonly Dictionary<string, GoldenCaseOutcome> _outcomes = new(StringComparer.Ordinal);

    /// <summary>Every case's scored outcome, keyed by <c>GoldenCase.Id</c>.</summary>
    internal IReadOnlyDictionary<string, GoldenCaseOutcome> Outcomes => _outcomes;

    internal IReadOnlyList<GoldenCase> Cases => GoldenSet.Cases;

    /// <summary>Absolute path of the markdown artefact this run wrote.</summary>
    public string ReportPath { get; private set; } = AiEvalOptions.ReportPath;

    public async Task InitializeAsync()
    {
        var byFixture = GoldenSet.Cases
            .GroupBy(c => c.TenantFixture, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var group in byFixture)
        {
            await using var host = await AskEvalHost.StartAsync(TenantFixtureCatalog.Get(group.Key))
                .ConfigureAwait(false);

            foreach (var goldenCase in group)
            {
                var turn = await host.AskAsync(goldenCase.Question).ConfigureAwait(false);
                _outcomes[goldenCase.Id] = GoldenCaseEvaluator.Evaluate(goldenCase, turn);
            }
        }

        ReportPath = GoldenSetReport.Write([.. GoldenSet.Cases.Select(c => _outcomes[c.Id])]);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Throws with the full failure list when <paramref name="caseId"/> was never run —
    /// a case id in a test's <c>MemberData</c> that the runner did not produce means the JSON and
    /// the run disagree, which is a harness bug, not a model failure.</summary>
    internal GoldenCaseOutcome Outcome(string caseId) =>
        _outcomes.TryGetValue(caseId, out var outcome)
            ? outcome
            : throw new InvalidOperationException(
                $"Golden case '{caseId}' produced no outcome — the runner did not execute it.");
}

/// <summary>
/// xunit collection binding the single <see cref="GoldenSetRunner"/> instance to every test class
/// in this project, so the 40+ HTTP turns happen once rather than once per class.
/// </summary>
[CollectionDefinition(Name)]
public sealed class GoldenSetCollection : ICollectionFixture<GoldenSetRunner>
{
    public const string Name = "ai-eval-golden-set";
}
