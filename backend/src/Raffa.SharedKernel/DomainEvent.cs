namespace Raffa.SharedKernel;

/// <summary>
/// Base class for domain events. Events are dispatched in-process via the mediator
/// (no durable outbox at R0). Each event carries the tenant context and a timestamp.
/// </summary>
public abstract record DomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public required TenantId TenantId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
}
