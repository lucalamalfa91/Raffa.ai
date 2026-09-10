using Raffa.SharedKernel;

namespace Raffa.AiGateway.Tests.TestSupport;

/// <summary>
/// Fake <see cref="IAuditWriter"/> that records every entry written. Same shape and name as
/// <c>Raffa.Documents.Contracts.Tests.DocumentUploadServiceTests.RecordingAuditWriter</c> and
/// <c>Raffa.IntegrationTests.RecordingDocumentStorage</c> — a lightweight in-memory spy is enough
/// to prove <see cref="Raffa.AiGateway.Logging.LoggingAiGateway"/> writes the right entries
/// without standing up a real Postgres-backed <c>Raffa.Audit.Infrastructure.AuditWriter</c>.
/// </summary>
public sealed class RecordingAuditWriter : IAuditWriter
{
    public List<AuditEntry> Written { get; } = [];

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        Written.Add(entry);
        return Task.CompletedTask;
    }
}
