using Contigo.SharedKernel;
using Contigo.SharedKernel.Storage;

namespace Contigo.Api.Tests.TestSupport;

/// <summary>
/// In-memory <see cref="IDocumentStorage"/> for the in-memory API host (task E13/F04/US01/T01):
/// records every save so a test can prove a rejected upload never reached storage, and an admitted
/// one did — the same shape <c>Contigo.IntegrationTests.RecordingDocumentStorage</c> gives the
/// Testcontainers hosts.
/// </summary>
internal sealed class RecordingDocumentStorage : IDocumentStorage
{
    public List<(string Path, byte[] Content)> Saved { get; } = [];

    public async Task<string> SaveAsync(
        TenantId tenantId,
        EntityId documentId,
        int versionNumber,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var path = DocumentStoragePath.Build(tenantId, documentId, versionNumber, fileName);
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        Saved.Add((path, buffer.ToArray()));
        return path;
    }
}
