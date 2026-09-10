using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Chat.Domain;
using Raffa.SharedKernel;

namespace Raffa.Chat.Application;

/// <summary>
/// Implements task E02/F04/US02/T01 (us-02-rag-citations, AC-1/AC-2/AC-3): turns a
/// <see cref="QueryRouteDecision"/> already routed to <see cref="QueryIntent.Semantic"/> (task
/// E02/F04/US01/T01, <see cref="AskRaffaQueryRouter"/>) plus caller-supplied, already-retrieved,
/// already-authorized evidence into a grounded answer with citations, or an explicit
/// "cannot determine" — spec §8.3/§8.4, ADR-004's `answer` role.
///
/// <para>
/// <b>AC-1 (auth-before-retrieval) / AC-3 (unauthorized documents never enter the LLM
/// context)</b>: this service never retrieves anything itself — it has no database dependency at
/// all. <paramref name="evidence"/> (see <see cref="AnswerAsync"/>) must already be the result of
/// an authorized, tenant-scoped retrieval (ADR-011: "the retrieval pipeline cannot run before a
/// tenant+role+object authorization check"). <c>Raffa.Chat</c> cannot reference
/// <c>Raffa.Documents.Contracts</c> at all (<c>Raffa.ArchitectureTests
/// .DependencyDirectionTests</c>'s allow-list for this module is exactly
/// <c>[SharedKernel, AiGateway]</c> — see <c>backend/README.md</c>'s "Dependency direction"
/// table), so the tenant-scoped pgvector search
/// (<c>Raffa.Documents.Contracts.Application.EmbeddingRetrievalService.SearchAsync</c>, task
/// E02/F02/US02/T02) and the mapping of its hits into <see cref="AiEvidenceSnippet"/> both happen
/// in the composition root (<c>Raffa.Api.ChatEndpointExtensions</c>, the one project allowed to
/// reference every module) *before* this method is ever called — the same "caller already fetched,
/// already tenant-scoped data" shape <see cref="DeterministicQueryHandler"/> already uses for
/// <see cref="ContractFact"/>.
/// </para>
///
/// <para>
/// <b>AC-2 (citations or cannot-determine)</b>: delegates the initial grounding/abstention decision
/// to <see cref="IAiGateway.AnswerAsync"/> (ADR-004 `answer` role), which already returns
/// <see cref="AiAnswerResult.CanDetermine"/> = <see langword="false"/> for empty evidence rather
/// than fabricating (spec §8.4 "no evidence, no claim"; Appendix C rule 10) — see
/// <c>Raffa.AiGateway.Fixtures.FixtureAiGateway.AnswerAsync</c>. Task E02/F04/US02/T02
/// (abstain-guard) then runs <see cref="AbstainGuard.Enforce"/> on that result before it is audited
/// or returned: a real model's <see langword="true"/> verdict is only trusted when every citation it
/// produced is actually grounded in <paramref name="evidence"/> — an ungrounded/hallucinated
/// citation, a zero-citation "determined" claim, or an empty answer text is downgraded to an honest
/// "cannot determine" rather than passed through (see <see cref="AbstainGuard"/>'s own doc comment).
/// </para>
///
/// <para>
/// Also writes one append-only <see cref="AuditEntry"/> per successful call (ADR-011: "Audit
/// records access and corrections... an audit domain logs who/when/what changed... and access
/// events, keyed by tenant" — an Ask Raffa semantic query is exactly an access event over
/// contract content). Same gateway-abstraction shape <c>Raffa.Documents.Contracts.Application
/// .DocumentUploadService</c>/<c>ContractCorrectionService</c> already use — <see cref="IAuditWriter"/>
/// lives in <c>Raffa.SharedKernel</c>, not <c>Raffa.Audit</c>, so this does not cross the
/// ADR-002 module boundary. Mirrors <c>Raffa.AiGateway.Logging.LoggingAiGateway</c>'s own
/// "never write raw prompt or retrieved contract text to logs" rule (ADR-011): the audit
/// <see cref="AuditEntry.Detail"/> carries only counts and the answer/gateway's own reproducibility
/// pointer, never the question text, the evidence text, or the answer text itself.
/// </para>
/// </summary>
public sealed class RagAnswerService(
    IAiGateway aiGateway, IAuditWriter auditWriter, IClock clock, AbstainGuard abstainGuard)
{
    /// <summary>Same interim-actor placeholder as <c>Raffa.Documents.Contracts.Application
    /// .DocumentUploadService.UnattributedActor</c>: ADR-010 (Entra ID/OIDC) is not in this task's
    /// "Architecture decisions in force" list, so there is no validated caller identity to record
    /// yet, and a client-supplied actor would be an unverified, spoofable identity — worse than an
    /// explicit, honest placeholder.</summary>
    private const string UnattributedActor = "unattributed";

    /// <summary><see cref="AuditEntry.Action"/> for every answered Ask Raffa semantic query.
    /// Past-tense, matching this codebase's established convention (<c>DocumentUploadService</c>'s
    /// <c>"document.uploaded"</c>, <c>ContractCorrectionService</c>'s <c>"contract.corrected"</c>,
    /// <c>LoggingAiGateway</c>'s <c>"ai.{role}"</c>).</summary>
    private const string AuditAnsweredAction = "chat.answered";

    /// <summary><see cref="AuditEntry.ResourceType"/> for every Ask Raffa answer. There is no
    /// single domain entity id to point at (an answer may cite zero, one, or many documents) —
    /// same rationale as <c>LoggingAiGateway.ResourceType</c> ("ai_call"), generalized here to the
    /// chat/answer surface.</summary>
    private const string AuditResourceType = "ask_raffa";

    /// <summary>
    /// Answers <paramref name="decision"/>'s question against <paramref name="evidence"/> — the
    /// grounded-Q&amp;A half of RAG (retrieval itself already happened in the caller, see the type
    /// doc comment).
    /// </summary>
    /// <param name="tenantId">The caller's tenant — recorded on the audit entry only; never used
    /// to filter or fetch anything here (there is nothing left to filter — see the type doc
    /// comment on why retrieval already happened upstream).</param>
    /// <param name="decision">Must be routed <see cref="QueryIntent.Semantic"/> — a
    /// <see cref="QueryIntent.Structured"/> decision has no RAG evidence to answer from and is a
    /// caller bug, not a legitimate input (same defensive shape as
    /// <see cref="DeterministicQueryPlanner.Plan"/>'s identical guard on the opposite branch).</param>
    /// <param name="evidence">Already-retrieved, already-authorized evidence (ADR-011). An empty
    /// list is valid input, not an error — see <see cref="AiAnswerRequest.Evidence"/>'s own doc
    /// comment.</param>
    /// <exception cref="ArgumentNullException"><paramref name="decision"/> or
    /// <paramref name="evidence"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="decision"/>'s
    /// <see cref="QueryRouteDecision.Intent"/> is not <see cref="QueryIntent.Semantic"/>.</exception>
    public async Task<Result<AiAnswerResult>> AnswerAsync(
        TenantId tenantId,
        QueryRouteDecision decision,
        IReadOnlyList<AiEvidenceSnippet> evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(evidence);

        if (decision.Intent != QueryIntent.Semantic)
        {
            throw new ArgumentException(
                $"Only a '{QueryIntent.Semantic}' decision has RAG evidence to answer from; " +
                $"'{decision.Question}' was routed '{decision.Intent}'.",
                nameof(decision));
        }

        var result = await aiGateway
            .AnswerAsync(new AiAnswerRequest(decision.Question, evidence), cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return result;
        }

        // Task E02/F04/US02/T02 (abstain-guard): the gateway's own "true" verdict is not trusted
        // blindly — every citation must actually be grounded in the evidence handed to it, or this
        // downgrades to an honest "cannot determine" (see AbstainGuard's own doc comment). Already
        // -honest abstentions (result.Value.CanDetermine == false) pass through unchanged.
        var guarded = abstainGuard.Enforce(result.Value, evidence);

        // Recorded only for a successful gateway call — mirrors LoggingAiGateway.LogAsync's
        // identical "only successful calls are logged" choice: a failed call (for example the
        // gateway itself rejecting an empty question) never produced an answer to account for.
        // Every count below reflects the guarded result, not the gateway's raw claim, so the audit
        // trail always matches what the caller actually received.
        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                UnattributedActor,
                AuditAnsweredAction,
                AuditResourceType,
                guarded.Result.Metadata.InputHash,
                clock.UtcNow,
                $"canDetermine={guarded.Result.CanDetermine} " +
                $"citationCount={guarded.Result.Citations.Count} " +
                $"evidenceCount={evidence.Count} " +
                $"abstainGuardIntervened={guarded.Intervened}"),
            cancellationToken).ConfigureAwait(false);

        return Result<AiAnswerResult>.Success(guarded.Result);
    }
}
