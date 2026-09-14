using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// Composition-root wiring for the Azure Blob Storage document store (ADR-005, ADR-011). Task
/// E16/F02/US02/T01 (durable-queue-transport, ADR-027 §D11, ADR-002 w15 footer clause 2): the
/// concrete adapter and its DI registration moved out of this host into a dedicated
/// <c>Raffa.Storage</c> adapter project — the <c>Raffa.AiGateway</c> shape — because
/// <c>Raffa.Worker</c> needs to resolve <c>IDocumentStorage</c> too and the adapter was previously
/// <c>internal sealed</c> to this host alone. This extension method is kept, unchanged in name and
/// signature, purely so <c>Raffa.Api/Program.cs</c>'s own call site
/// (<c>builder.Services.AddAzureBlobDocumentStorage(storageConnectionString)</c>) needs no edit —
/// it now only delegates to <see cref="Raffa.Storage.StorageServiceCollectionExtensions"/>.
///
/// Called by explicit static invocation, never by <c>services.AddAzureBlobDocumentStorage(...)</c>
/// dot-syntax: this class declares an extension method of the identical name and signature, so the
/// dot-syntax call from inside this same class would resolve back to itself (infinite recursion)
/// rather than to <c>Raffa.Storage</c>'s method.
/// </summary>
internal static class DocumentStorageServiceCollectionExtensions
{
    public static IServiceCollection AddAzureBlobDocumentStorage(
        this IServiceCollection services, string connectionString) =>
        Raffa.Storage.StorageServiceCollectionExtensions.AddAzureBlobDocumentStorage(services, connectionString);
}
