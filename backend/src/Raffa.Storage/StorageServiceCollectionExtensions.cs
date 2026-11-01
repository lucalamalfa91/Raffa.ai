using Azure.Storage.Blobs;
using Raffa.SharedKernel.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Storage;

/// <summary>
/// Composition-root wiring for the Azure Blob Storage document store (ADR-005, ADR-011), shared by
/// both hosts. Mirrors the module registration shape domain modules use (<c>AddXxx(IServiceCollection)</c>)
/// even though the concrete adapter lives in its own adapter project, not a domain module (ADR-002:
/// no provider SDK in domain code).
///
/// Task E16/F02/US02/T01 (durable-queue-transport): moved out of
/// <c>Raffa.Api.Infrastructure.DocumentStorageServiceCollectionExtensions</c>, which now delegates
/// here, so <c>Raffa.Worker/WorkerServiceCollectionExtensions.AddWorkerHost</c> can call the exact
/// same method the API host does — one registration shape, not two independently-maintained copies
/// of "how to construct <see cref="AzureBlobDocumentStorage"/>".
/// </summary>
public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Container name fixed by Terraform (<c>infra/modules/storage/main.tf</c>,
    /// <c>azurerm_storage_container.documents</c>) — the two must always agree.
    /// </summary>
    public const string DocumentsContainerName = "documents";

    public static IServiceCollection AddAzureBlobDocumentStorage(
        this IServiceCollection services, string connectionString)
    {
        // Constructing BlobContainerClient makes no network call (same laziness as AddDbContext
        // elsewhere in this solution's composition roots) — safe to register as a plain Singleton.
        services.AddSingleton(_ => new BlobContainerClient(connectionString, DocumentsContainerName));
        services.AddSingleton<IDocumentStorage, AzureBlobDocumentStorage>();

        return services;
    }

    /// <summary>The value of <c>ConnectionStrings:Storage</c> that selects <see cref="InMemoryDocumentStorage"/>.</summary>
    public const string InMemoryConnectionString = "InMemory";

    /// <summary>Blob storage, or the in-memory store when the connection string is <c>InMemory</c>
    /// (the Postgres-only <c>Parity</c> environment).</summary>
    public static IServiceCollection AddDocumentStorage(this IServiceCollection services, string connectionString)
    {
        if (string.Equals(connectionString, InMemoryConnectionString, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<InMemoryDocumentStorage>();
            services.AddSingleton<IDocumentStorage>(sp => sp.GetRequiredService<InMemoryDocumentStorage>());
            return services;
        }

        return services.AddAzureBlobDocumentStorage(connectionString);
    }
}
