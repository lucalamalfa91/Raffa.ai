using Raffa.SharedKernel;

namespace Raffa.Savings.Tests.TestSupport;

/// <summary>
/// Fake <see cref="IAuditWriter"/> that records every entry written. A lightweight in-memory spy is
/// enough to prove <see cref="Raffa.Savings.Application.SavingsOpportunityService"/> writes the
/// right audit entries without standing up a real Postgres-backed
/// <c>Raffa.Audit.Infrastructure.AuditWriter</c> — mirrors
/// <c>Raffa.Renewals.Tests.TestSupport.RecordingAuditWriter</c> exactly.
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
