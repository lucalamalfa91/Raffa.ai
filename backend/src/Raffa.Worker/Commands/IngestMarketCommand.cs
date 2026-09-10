using Raffa.Market.Ingestion;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Worker.Commands;

/// <summary>
/// The Worker's operator ingestion entry point (task E13/F02/US01/T02, task objective, verbatim):
/// <c>dotnet run --project backend/src/Raffa.Worker -- ingest-market --feed
/// backend/fixtures/market-intelligence.mock.json</c>. Host wiring only — the actual ingestion
/// logic lives in <see cref="MarketIngestionService"/> (ADR-002 "host is a thin composition
/// root"), the same split every other endpoint/handler in this codebase already keeps. Internal:
/// this is host wiring, not a public API surface — enforced by
/// <c>Raffa.ArchitectureTests.DependencyDirectionTests.Host_must_not_contain_domain_types</c>,
/// mirroring <c>Raffa.Worker.Scheduling.RenewalThresholdSchedulerHostedService</c>'s own
/// internal visibility.
///
/// A one-shot CLI command, not a hosted-service loop: <c>Program.cs</c> checks for this command
/// before calling <c>host.Run()</c> (which would otherwise block forever running this host's own
/// hosted services) and exits immediately after <see cref="RunAsync"/> completes.
///
/// <b>Why <c>--feed &lt;path&gt;</c> is accepted but not forwarded as a feed version:</b>
/// <see cref="MarketIngestionService.IngestAsync"/>'s own <c>feedVersion</c> parameter is a
/// version *tag* the provider already knows about (<see cref="Raffa.Market.IMarketIntelligenceProvider.GetDealsAsync"/>'s
/// own doc comment); the checked-in
/// <see cref="Raffa.Market.Mock.MockMarketIntelligenceProvider"/> (task E13/F02/US01/T01) reads
/// its fixture from an <b>embedded resource</b>, never a runtime file path, and only ever serves
/// its one checked-in version — passing this command's own <c>--feed</c> *path* straight through
/// as <c>feedVersion</c> would fail every real invocation of the task's own example command line
/// with "Mock market feed has only version '...'". <c>--feed</c> is therefore accepted purely as
/// an operator-facing, informational label (echoed to the console below) — forward-compatible
/// shape for a later, real R-MKT-05 provider that might select a feed by version/URL — while this
/// task always ingests "whatever the provider's current feed is" (<c>feedVersion: null</c>), the
/// only mode the mock provider actually supports.
/// </summary>
internal static class IngestMarketCommand
{
    /// <summary>The leading CLI argument <c>Program.cs</c> checks for.</summary>
    public const string Name = "ingest-market";

    private const string FeedArgumentName = "--feed";

    /// <summary>
    /// Runs one ingestion pass and prints its <see cref="IngestionSummary"/> to the console.
    /// Returns a process exit code (0 success, 1 failure) — never throws for an expected failure
    /// (a missing connection string, a provider/embed error), matching this codebase's
    /// <c>Result&lt;T&gt;</c>-not-exceptions convention for the AI Gateway/provider calls
    /// <see cref="MarketIngestionService"/> itself makes.
    /// </summary>
    public static async Task<int> RunAsync(
        IServiceProvider hostServices, string[] args, CancellationToken cancellationToken = default)
    {
        var feedArgument = ParseFeedArgument(args);

        // Fresh scope: MarketIngestionService is registered Scoped (it shares its own
        // MarketDbContext instance, also Scoped) -- same IServiceScopeFactory-per-invocation shape
        // Raffa.Worker.Scheduling.RenewalThresholdSchedulerHostedService already uses for its own
        // per-tick Scoped dependencies.
        using var scope = hostServices.CreateScope();
        var ingestionService = scope.ServiceProvider.GetService<MarketIngestionService>();

        if (ingestionService is null)
        {
            // MarketIngestionService is only registered when ServiceCollectionExtensions
            // .AddMarketModule receives a non-null connection string (see that method's own doc
            // comment) -- a clear, named failure here instead of an opaque DI resolution
            // exception the moment host.Services would otherwise be asked to construct it.
            await Console.Error.WriteLineAsync(
                "ingest-market requires ConnectionStrings:Market to be configured (set env var " +
                "ConnectionStrings__Market) -- there is nothing to ingest into otherwise.");
            return 1;
        }

        Console.WriteLine(feedArgument is null
            ? "Ingesting market intelligence feed (provider's current feed)..."
            : $"Ingesting market intelligence feed (source: {feedArgument})...");

        var result = await ingestionService.IngestAsync(feedVersion: null, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            await Console.Error.WriteLineAsync($"ingest-market failed: {result.Error}").ConfigureAwait(false);
            return 1;
        }

        var summary = result.Value;
        Console.WriteLine(
            $"Ingested feed '{summary.FeedVersion}': {summary.Inserted} inserted, " +
            $"{summary.Updated} updated, {summary.Unchanged} unchanged.");

        return 0;
    }

    /// <summary>Returns the value following a <c>--feed</c> argument, or <see langword="null"/>
    /// when absent — see the type doc comment for why this value is informational only.</summary>
    private static string? ParseFeedArgument(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], FeedArgumentName, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
