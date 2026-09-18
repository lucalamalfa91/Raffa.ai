namespace Raffa.Renewals.Application;

/// <summary>
/// One ranked negotiation point, as the host (<c>Raffa.Api.AskCopilotService</c>, epic-29/feature-02)
/// hands it to <see cref="RenewalNegotiationTodoService.UpsertAsync"/> after
/// <c>Raffa.Insights.Application.NegotiationPointRanker</c> (epic-31) has produced it — task
/// E29/F01/US01/T01's own field list (topic/rank/current/target/rationale/citation_keys), matching
/// NW-96's point shape one field short (the ranker's own <c>whyItMatters</c> becomes
/// <see cref="Rationale"/> here; <c>strength</c> is not part of this module's persisted field set
/// and is dropped by whichever composition maps the ranker's output onto this record). Deliberately
/// living here rather than in <c>Raffa.Api</c> or <c>Raffa.Insights</c> — the same
/// <c>Raffa.ArchitectureTests.DependencyDirectionTests.Host_must_not_contain_domain_types</c> reason
/// <c>RenewalActionRequest</c>'s own doc comment gives for its sibling request shape, and this
/// module's own <c>[SharedKernel, Benchmark]</c> allow-list (ADR-002) means it cannot reference
/// <c>Raffa.Insights</c>'s own point type either — so the host maps one onto the other, and this is
/// the shape it maps onto.
/// </summary>
/// <param name="PointKey">Stable identity for this point — see
/// <see cref="Raffa.Renewals.Domain.RenewalNegotiationTodo.PointKey"/>'s own doc comment. Never an
/// array index or an ordinal ("point_key stable", the parent story's own council decision).</param>
/// <param name="Topic">See <see cref="Raffa.Renewals.Domain.RenewalNegotiationTodo.Topic"/>.</param>
/// <param name="Rank">See <see cref="Raffa.Renewals.Domain.RenewalNegotiationTodo.Rank"/>.</param>
/// <param name="Current">See <see cref="Raffa.Renewals.Domain.RenewalNegotiationTodo.Current"/>.</param>
/// <param name="Target">See <see cref="Raffa.Renewals.Domain.RenewalNegotiationTodo.Target"/>.</param>
/// <param name="Rationale">See <see cref="Raffa.Renewals.Domain.RenewalNegotiationTodo.Rationale"/>.</param>
/// <param name="CitationKeys">See <see cref="Raffa.Renewals.Domain.RenewalNegotiationTodo.CitationKeys"/>.</param>
public sealed record RenewalNegotiationTodoPoint(
    string PointKey,
    string Topic,
    int Rank,
    string Current,
    string Target,
    string Rationale,
    IReadOnlyList<string> CitationKeys);
