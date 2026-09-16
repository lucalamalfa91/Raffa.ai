using Raffa.Audit.Infrastructure;
using Raffa.SharedKernel;

namespace Raffa.Api.Tests.TestSupport;

/// <summary>
/// Task E21/F01/US01/T01: in-memory <see cref="IAuditQueryService"/> double that lets endpoint
/// tests control which audit events the activity projection returns, without a real Audit Postgres
/// database. The real behaviour (contract scoping, allow-list, RLS, cross-tenant isolation) is
/// proven by <see cref="Raffa.Audit.Tests.ContractActivityProjectionTests"/>.
///
/// <para>
/// <see cref="GetContractEventsAsync"/> applies the allow-list and resource-id filter in memory,
/// so the endpoint's own "exclude non-listed actions" and "contract-scoped" properties are still
/// exercised by the endpoint test — only the Postgres/RLS layer is absent.
/// </para>
/// </summary>
internal sealed class StubAuditQueryService : IAuditQueryService
{
    private readonly List<AuditEventRecord> _events = [];

    /// <summary>Adds one event to the in-memory store — called by
    /// <see cref="InMemoryAskEngineFactory.SeedAuditEventAsync"/>.</summary>
    public void Seed(AuditEventRecord record) => _events.Add(record);

    public Task<IReadOnlyList<AuditEventRecord>> GetEventsAsync(
        TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AuditEventRecord>>([]);

    public Task<IReadOnlyList<AuditEventRecord>> GetContractEventsAsync(
        TenantId tenantId,
        string resourceId,
        IReadOnlyCollection<string> allowedActions,
        CancellationToken cancellationToken = default)
    {
        // Mirrors the real query: ResourceId match + action allow-list + newest-first.
        IReadOnlyList<AuditEventRecord> result = _events
            .Where(e => string.Equals(e.ResourceId, resourceId, StringComparison.Ordinal)
                && allowedActions.Contains(e.Action))
            .OrderByDescending(e => e.OccurredAt)
            .ToList();
        return Task.FromResult(result);
    }
}
