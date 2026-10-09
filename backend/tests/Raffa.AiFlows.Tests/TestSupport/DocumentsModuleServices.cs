using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Storage;
using Raffa.SharedKernel.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Raffa.AiFlows.Tests.TestSupport;

/// <summary>
/// A service collection holding the Documents/Contracts module plus the few registrations a host
/// supplies for it (configuration, audit writer, blob storage), so a test can compose the AI flows
/// layer on top of the real module and build the provider. Nothing opens a connection: the
/// connection string only has to parse.
/// </summary>
public static class DocumentsModuleServices
{
    public const string ConnectionString =
        "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true";

    public static ServiceCollection Create()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection().Build());
        services.AddSingleton<IAuditWriter, RecordingAuditWriter>();
        services.AddSingleton<IDocumentStorage, UnusedDocumentStorage>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<InMemoryExtractionQueue>();
        services.AddSingleton<IExtractionQueuePublisher>(sp => sp.GetRequiredService<InMemoryExtractionQueue>());
        services.AddSingleton<IExtractionDeadLetterResubmitter>(sp => sp.GetRequiredService<InMemoryExtractionQueue>());
        services.AddDocumentsContractsModule(ConnectionString);
        return services;
    }

    private sealed class UnusedDocumentStorage : IDocumentStorage
    {
        public Task<string> SaveAsync(
            TenantId tenantId, EntityId documentId, int versionNumber, string fileName, Stream content,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<byte[]?> LoadAsync(
            TenantId tenantId, string storagePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            TenantId tenantId, string storagePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> SavePreviewAsync(
            TenantId tenantId, EntityId documentId, Stream content, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> SavePreviewPageAsync(
            TenantId tenantId, EntityId documentId, int page, Stream content,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
