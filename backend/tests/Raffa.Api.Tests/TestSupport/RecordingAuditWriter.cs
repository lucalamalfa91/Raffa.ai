using Raffa.SharedKernel;

namespace Raffa.Api.Tests.TestSupport;

/// <summary>
/// Records every <see cref="AuditEntry"/> the in-memory API host writes (task E13/F04/US01/T01):
/// the admission gate's one <c>document.rejected</c> row is asserted on exactly, not just "some
/// audit happened".
/// </summary>
internal sealed class RecordingAuditWriter : IAuditWriter
{
    private readonly List<AuditEntry> _entries = [];

    public IReadOnlyList<AuditEntry> Entries => _entries;

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        lock (_entries)
        {
            _entries.Add(entry);
        }

        return Task.CompletedTask;
    }
}
