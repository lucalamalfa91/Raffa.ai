using Azure.Storage.Blobs;
using Raffa.SharedKernel.Storage;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// Composition-root wiring for the Azure Blob Storage document store (ADR-005, ADR-011). Mirrors
/// the module registration shape domain modules use (<c>AddXxx(IServiceCollection)</c>) even
/// though the concrete adapter lives in the host, not a domain module (ADR-002: no provider SDK
/// in domain code).
/// </summary>
internal static class DocumentStorageServiceCollectionExtensions
{
    /// <summary>
    /// Container name fixed by Terraform (<c>infra/modules/storage/main.tf</c>,
    /// <c>azurerm_storage_container.documents</c>) — the two must always agree.
    /// </summary>
    internal const string DocumentsContainerName = "documents";

    public static IServiceCollection AddAzureBlobDocumentStorage(
        this IServiceCollection services, string connectionString)
    {
        // Constructing BlobContainerClient makes no network call (same laziness as AddDbContext
        // below it in Program.cs) — safe to register as a plain Singleton.
        services.AddSingleton(_ => new BlobContainerClient(connectionString, DocumentsContainerName));
        services.AddSingleton<IDocumentStorage, AzureBlobDocumentStorage>();

        return services;
    }
}
