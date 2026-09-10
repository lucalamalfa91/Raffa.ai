namespace Raffa.Quotes.Application.Strategy;

/// <summary>
/// One recommended negotiation lever plus its rationale (task E05/F03/US01/T01,
/// negotiation-strategy; parent story us-01-negotiation-strategy AC-1's own "levers with
/// rationale"). <see cref="NegotiationStrategyCalculator"/> always returns exactly the seven
/// <see cref="NegotiationLeverType"/> values, in the fixed order spec §12.1's own example lists
/// them — never a variable-length subset — so a caller always sees the full canonical playbook,
/// with <see cref="Rationale"/> distinguishing a lever this quote's own data actually grounds from
/// one that is still a generic, always-available negotiation tactic (see that type's own doc
/// comment for why omitting an ungrounded lever, rather than saying so honestly, would be a worse
/// default).
///
/// <para>
/// <b>Structured evidence (task E05/F03/US01/T02, strategy-evidence)</b>: parent story AC-2
/// ("Rationale cites explicit evidence per lever", Appendix C rule 2 "never show a consequential...
/// fact without source evidence and confidence metadata") is satisfied by <see cref="Evidence"/> —
/// a Quotes-local record, not <c>Raffa.AiGateway.Contracts.AiCitation</c>/<c>AiEvidenceSnippet</c>
/// (those are document-citation-shaped for RAG answers over unstructured text, and this module's
/// own allowed-reference set — <c>Raffa.ArchitectureTests.DependencyDirectionTests</c>' exactly
/// <c>[SharedKernel, Benchmark]</c> for <c>Raffa.Quotes</c> — cannot reference
/// <c>Raffa.AiGateway</c> anyway; see <see cref="NegotiationStrategyCalculator"/>'s own doc
/// comment). <see cref="Evidence"/> turns the concrete quote/line fact <see cref="Rationale"/>
/// already names inline (e.g. the actual quantity, term or bundle count) into a structured,
/// queryable pointer — it never invents a fact <see cref="Rationale"/> does not already cite, and
/// is empty under the identical "no source field exists yet" condition that already makes
/// <see cref="Rationale"/> read as a generic, always-available tactic rather than a
/// this-quote-specific one.
/// </para>
/// </summary>
/// <param name="LeverType">Which of the seven canonical levers this is.</param>
/// <param name="Rationale">Human-readable, deterministic explanation of why this lever applies —
/// see <see cref="NegotiationStrategyCalculator"/>'s own doc comment for the deterministic-vs-LLM
/// split (AC-3; Appendix C rule 6) this text currently sits on.</param>
/// <param name="Evidence">Structured citations backing <see cref="Rationale"/> (task
/// E05/F03/US01/T02, strategy-evidence; AC-2) — see <see cref="NegotiationLeverEvidence"/>'s own
/// doc comment. Empty exactly when <see cref="Rationale"/> itself is the generic, ungrounded
/// variant (no source field exists for this quote/line yet) — never fabricated to fill the
/// list (Appendix C rule 10).</param>
public sealed record NegotiationLever(
    NegotiationLeverType LeverType,
    string Rationale,
    IReadOnlyList<NegotiationLeverEvidence> Evidence);
