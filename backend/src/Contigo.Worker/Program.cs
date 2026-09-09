// Contigo Worker Host — thin composition root (ADR-002).
// References the same domain/application libraries as the API host (parent story us-04 AC-2:
// "Worker host references the same application services"); hosts background processing
// (extraction, renewal recomputation, benchmark refresh, quote assessment) driven off the
// durable queue (AC-2: "... and consumes the queue").
using Contigo.Market;
using Contigo.Worker;
using Contigo.Worker.Commands;

var builder = Host.CreateApplicationBuilder(args);

// Same configuration key and fail-fast shape as Contigo.Api/Program.cs: both hosts read the
// Documents/Contracts connection string from ConnectionStrings:DocumentsContracts so `dev`/`demo`
// Container Apps share one config-naming convention across the API and worker Container Apps
// (ADR-002, ADR-005).
var documentsContractsConnectionString = builder.Configuration.GetConnectionString("DocumentsContracts")
    ?? throw new InvalidOperationException(
        "Missing required configuration 'ConnectionStrings:DocumentsContracts' " +
        "(set env var ConnectionStrings__DocumentsContracts in deployed environments).");

// Same configuration key Contigo.Api/Program.cs reads (task E01/F09/US01/T01, r0-integration):
// DocumentUploadService now requires IAuditWriter, which only AddAuditModule registers -- see
// WorkerServiceCollectionExtensions.AddWorkerHost's own doc comment.
var auditConnectionString = builder.Configuration.GetConnectionString("Audit")
    ?? throw new InvalidOperationException(
        "Missing required configuration 'ConnectionStrings:Audit' " +
        "(set env var ConnectionStrings__Audit in deployed environments).");

// Same configuration key Contigo.Api/Program.cs reads (task E03/F03/US01/T02, renewal-action):
// AddRenewalsModule now requires a connection string for its own first DbContext.
var renewalsConnectionString = builder.Configuration.GetConnectionString("Renewals")
    ?? throw new InvalidOperationException(
        "Missing required configuration 'ConnectionStrings:Renewals' " +
        "(set env var ConnectionStrings__Renewals in deployed environments).");

// Task E13/F02/US01/T02 (market-index): unlike the three connection strings above, this one is
// optional -- ServiceCollectionExtensions.AddMarketModule's own DI swap already treats a null
// connection string as "keep task E13/F02/US01/T01's in-memory/mock wiring, register nothing
// database-backed" (that method's own doc comment), so this host does not fail-fast when it is
// absent. Only the `ingest-market` command below actually requires it, and fails loudly, by name,
// when it is missing (IngestMarketCommand.RunAsync's own doc comment) -- there is no worker job
// yet (this task's own scope is the operator CLI entry point, not a scheduled recompute) that
// would otherwise silently no-op without ever telling an operator why.
var marketConnectionString = builder.Configuration.GetConnectionString("Market");

// WorkerServiceCollectionExtensions.AddWorkerHost is the single source of truth for this host's
// composition (module registration + queue consumer + hosted service) -- Contigo.Worker.Tests
// calls the same method to prove the wiring, not a hand-rolled copy of it.
builder.Services.AddWorkerHost(documentsContractsConnectionString, auditConnectionString, renewalsConnectionString);

// Task E13/F02/US01/T02 (market-index): Contigo.Market's own composition method, called directly
// here rather than folded into AddWorkerHost -- it is not "the same application services" AC-2
// documents that method for (Documents/Contracts + Audit + Renewals); this host's only Market
// consumer is the one-shot `ingest-market` command below, not a hosted service, so it does not
// belong inside AddWorkerHost's own "hosted service" wiring either.
builder.Services.AddMarketModule(marketConnectionString);

var host = builder.Build();

// Task E13/F02/US01/T02 (market-index): a one-shot operator command, not a hosted-service loop --
// checked before host.Run() (which blocks forever running this host's own hosted services) so
// this exits immediately once the ingestion pass completes, matching the task's own worked
// example: `dotnet run --project backend/src/Contigo.Worker -- ingest-market --feed
// backend/fixtures/market-intelligence.mock.json`.
if (args.Length > 0 && string.Equals(args[0], IngestMarketCommand.Name, StringComparison.Ordinal))
{
    return await IngestMarketCommand.RunAsync(host.Services, args);
}

host.Run();
return 0;
