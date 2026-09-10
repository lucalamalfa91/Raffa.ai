namespace Raffa.SharedKernel;

/// <summary>
/// Audit abstraction. Every module writes audit events through this interface.
/// The Audit module provides the implementation; domain modules never depend on it directly.
/// Append-only by contract (Appendix C rule 9).
/// </summary>
public interface IAuditWriter
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a single audit event: who did what, when, in which tenant context.
/// </summary>
public sealed record AuditEntry(
    TenantId TenantId,
    string Actor,
    string Action,
    string ResourceType,
    string ResourceId,
    DateTimeOffset Timestamp,
    string? Detail = null);
