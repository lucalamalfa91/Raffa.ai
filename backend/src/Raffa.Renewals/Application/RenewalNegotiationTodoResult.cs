using Raffa.Renewals.Domain;
using Raffa.SharedKernel;

namespace Raffa.Renewals.Application;

/// <summary>Outcome of a successful <see cref="RenewalNegotiationTodoService.UpsertAsync"/>,
/// <see cref="RenewalNegotiationTodoService.GetAsync"/> or <see cref="RenewalNegotiationTodoService.SetDoneAsync"/>
/// call — the current, persisted state of one negotiation TODO row (task E29/F01/US01/T01,
/// todo-entity-api). 1:1 with <see cref="RenewalNegotiationTodo"/>'s own fields minus
/// <see cref="TenantScopedEntity.TenantId"/>/<see cref="TenantScopedEntity.Id"/> — same "wire-facing
/// projection, not the entity itself" shape <see cref="RenewalActionResult"/> already
/// establishes.</summary>
public sealed record RenewalNegotiationTodoResult(
    EntityId ContractId,
    string PointKey,
    string Topic,
    int Rank,
    string Current,
    string Target,
    string Rationale,
    IReadOnlyList<string> CitationKeys,
    string Source,
    RenewalNegotiationTodoStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
