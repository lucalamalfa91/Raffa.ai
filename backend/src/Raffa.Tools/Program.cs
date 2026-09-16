// Raffa.Tools — operator console (task E20/F02/US02/T01, w17, NW-73).
// Third composition root: no table, no endpoint, no business rule; referenced by nothing.
// Compose the modules' own ServiceCollectionExtensions; never hand-build services.
//
// Credential rules (ADR-009 w17 clause 1 / task §9):
//   - Postgres:       ConnectionStrings__DocumentsContracts + ConnectionStrings__Audit from
//                     the app's own 'postgres-connection' Key Vault secret (never superuser,
//                     never BYPASSRLS). The workflow reads it via az keyvault secret show and
//                     passes it as environment variables.
//   - Blob storage:   ConnectionStrings__Storage from the app's own 'storage-connection' secret.
//   - Service Bus:    DefaultAzureCredential (resolved after az login OIDC in the workflow);
//                     AZURE_CLIENT_ID is NOT set on the runner — it selects the user-assigned
//                     managed identity, absent from the GitHub Actions host (task §9).
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Raffa.Audit.Infrastructure;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Messaging;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Raffa.Storage;
using Raffa.Tools;

// ── Argument parsing ────────────────────────────────────────────────────────
// Required: --tenant-id <guid>
// Required: --requested-by <string>   (goes to AuditEntry.Detail, never to Actor)
// Optional: --run-id <string>         (defaults to a fresh Guid)
string? tenantIdStr = null;
string? requestedBy = null;
string? runId = null;

for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--tenant-id")      tenantIdStr  = args[i + 1];
    if (args[i] == "--requested-by")   requestedBy  = args[i + 1];
    if (args[i] == "--run-id")         runId        = args[i + 1];
}

if (string.IsNullOrWhiteSpace(tenantIdStr))
{
    Console.Error.WriteLine("Usage: Raffa.Tools --tenant-id <guid> --requested-by <actor> [--run-id <id>]");
    Console.Error.WriteLine("       --tenant-id   is required (the workspace GUID).");
    return 2;
}

if (!Guid.TryParse(tenantIdStr, out var tenantGuid))
{
    Console.Error.WriteLine($"--tenant-id '{tenantIdStr}' is not a valid GUID.");
    return 2;
}

if (string.IsNullOrWhiteSpace(requestedBy))
{
    Console.Error.WriteLine("--requested-by is required (goes into the run-scoped audit row Detail, never into Actor).");
    return 2;
}

var tenantId = new TenantId(tenantGuid);
runId ??= Guid.NewGuid().ToString();

// ── Configuration ───────────────────────────────────────────────────────────
var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();

var documentsCs = configuration.GetConnectionString("DocumentsContracts")
    ?? throw new InvalidOperationException(
        "Missing required configuration ConnectionStrings__DocumentsContracts. " +
        "Set it from the app's own 'postgres-connection' Key Vault secret (ADR-009 w17 clause 1).");

var auditCs = configuration.GetConnectionString("Audit")
    ?? throw new InvalidOperationException(
        "Missing required configuration ConnectionStrings__Audit. " +
        "Set it from the app's own 'postgres-connection' Key Vault secret.");

var storageCs = configuration.GetConnectionString("Storage")
    ?? throw new InvalidOperationException(
        "Missing required configuration ConnectionStrings__Storage. " +
        "Set it from the app's own 'storage-connection' Key Vault secret.");

// ── DI composition ─────────────────────────────────────────────────────────
// Compose through the modules' own extension methods — never hand-build (task §2).
// AddDocumentsContractsModule wires DocumentsContractsDbContext with the three-argument
// DocumentsContractsDbContextOptions.Configure (ITenantContext supplied, interceptor wired —
// so every connection carries app.tenant_id from ITenantContext.Current). The two-argument
// form omits the interceptor; with it absent the RLS policy returns zero rows silently
// (BulkReprocessTenantBindingTests proves both paths).
var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(configuration);
services.AddDocumentsContractsModule(documentsCs);
services.AddAuditModule(auditCs);
services.AddAzureBlobDocumentStorage(storageCs);
services.AddExtractionQueuePublisher(configuration);
services.AddScoped<BulkReprocessRunner>();

await using var serviceProvider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateOnBuild = false });

// ── Scoped execution ────────────────────────────────────────────────────────
// DbContext (and all Application services) is Scoped — run the whole operation in one scope.
// BeginScope sets ITenantContext.Current before any DB call so TenantRlsConnectionInterceptor
// can emit SET app.tenant_id on every Postgres connection (ADR-009 w17 clause 1 §3).
await using var scope = serviceProvider.CreateAsyncScope();
var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
using var tenantScope = tenantContext.BeginScope(tenantId);

var runner = scope.ServiceProvider.GetRequiredService<BulkReprocessRunner>();
return await runner.RunAsync(tenantId, requestedBy, runId);
