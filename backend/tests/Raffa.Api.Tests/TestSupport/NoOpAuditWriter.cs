using Raffa.SharedKernel;

namespace Raffa.Api.Tests.TestSupport;

/// <summary>
/// Discards every audit entry. The success-path tests in <c>ChatEndpointTests</c>/
/// <c>ConversationsEndpointTests</c> that run the full Ask engine (<c>Raffa.Api.AskCopilotService</c>)
/// need *some* <see cref="IAuditWriter"/> that does not require a live Postgres-backed
/// <c>Raffa.Audit</c> store (the one <c>AddAuditModule</c> registers) — this project asserts on
/// the HTTP reply, not on the audit trail;
/// <c>Raffa.IntegrationTests.AskRaffaRagCrossTenantIsolationTests</c> already proves the audit
/// entry's own shape, with its own recording (not no-op) fake.
/// </summary>
internal sealed class NoOpAuditWriter : IAuditWriter
{
    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
