using Raffa.SharedKernel;

namespace Raffa.Renewals.Tests.TestSupport;

/// <summary>
/// Fake <see cref="IAuditWriter"/> that records every entry written. Same shape and name as
/// <c>Raffa.Chat.Tests.TestSupport.RecordingAuditWriter</c> /
/// <c>Raffa.AiGateway.Tests.TestSupport.RecordingAuditWriter</c> — a lightweight in-memory spy is
/// enough to prove <see cref="Raffa.Renewals.Application.RenewalThresholdScheduler"/> writes the
/// right <c>renewal.approaching</c> entry without standing up a real Postgres-backed
/// <c>Raffa.Audit.Infrastructure.AuditWriter</c>.
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
