namespace Raffa.Renewals.Application;

/// <summary>
/// Request body for `PUT /api/renewals/{id}/negotiation-todos` (task E29/F01/US01/T01,
/// todo-entity-api; parent story us-01-todo-entity-api AC-2, the tick). Deliberately living here
/// rather than in `Raffa.Api` — same
/// `Raffa.ArchitectureTests.DependencyDirectionTests.Host_must_not_contain_domain_types` reason
/// <see cref="RenewalActionRequest"/>'s own doc comment gives for its sibling request shape.
///
/// The raw, unvalidated wire value — <see cref="RenewalNegotiationTodoService.SetDoneAsync"/> is
/// the one place that validates it is non-blank and names an existing row, so a blank/unknown
/// value fails with the same <see cref="Raffa.SharedKernel.Result{T}"/> shape as any other
/// service-level validation, not a framework-level 400 with a less specific message.
/// </summary>
public sealed record RenewalNegotiationTodoTickRequest(string? PointKey);
