using Raffa.AiGateway.Telemetry;
using Raffa.AiFlows.Shared.Audit;
using Raffa.AiFlows.Ask.Answering;
using Raffa.AiFlows.Ask.Gate;
using Raffa.AiFlows.Ask.Interview;
using Raffa.AiFlows.Ask.Planning;
using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.Ask.Routing;
using Raffa.AiFlows.Negotiation.Verdict;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Planning;
using Raffa.AiFlows.Shared.Reply;
using Raffa.AiFlows.Shared.Routing;
using Raffa.AiFlows.WebResearch.Orchestration;
using Raffa.Benchmark.Contracts;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Interview;
using Raffa.Chat.Application.Reply;
using Raffa.Insights.Contracts;
using Raffa.Insights.Savings;
using Raffa.Market.Contracts;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.Insights.Application;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Ask.Orchestration;

/// <summary>
/// The one <c>chat.*</c> audit row an Ask turn writes (R-ASK-09). Moved verbatim from
/// <see cref="AskCopilotService"/> (F1 Ask split, PR-1): same actions, same detail string.
/// </summary>
public sealed class AskTurnAudit(IAuditWriter auditWriter, IClock clock)
{
    private const string AuditAnsweredAction = "chat.answered";
    private const string AuditRedirectedAction = "chat.redirected";
    private const string AuditRefusedAction = "chat.refused";
    private const string AuditAbstainedAction = "chat.abstained";
    private const string AuditInterviewedAction = "chat.interviewed";
    private const string AuditDraftedAction = "chat.drafted";
    private const string AuditResourceType = AskAuditResourceType.Value;

    internal async Task WriteAsync(
        TenantId tenantId,
        CopilotReply reply,
        bool guardIntervened,
        bool fallbackUsed,
        string gapInvestigation,
        bool webMode,
        string actor,
        CancellationToken cancellationToken)
    {
        var action = reply.Kind switch
        {
            ReplyKind.Answer => AuditAnsweredAction,
            ReplyKind.Abstain => AuditAbstainedAction,
            ReplyKind.Redirect => AuditRedirectedAction,
            ReplyKind.Refusal => AuditRefusedAction,
            ReplyKind.Draft => AuditDraftedAction,
            ReplyKind.Interview => AuditInterviewedAction,
            _ => AuditAnsweredAction,
        };

        var packHash = ComputeHash(string.Join('|', reply.Citations.Select(c => c.N + ":" + c.Corpus)));

        // AC-7 / R-ASK-09: "audit records abstainGuardIntervened=true" when Guards.RegenerateOnce's
        // retry-then-downgrade path fired — the field name every Ask audit entry carries,
        // so an operator/query filters on one consistent key.
        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                actor,
                action,
                AuditResourceType,
                packHash,
                clock.UtcNow,
                $"kind={reply.Kind} citationCount={reply.Citations.Count} actionCount={reply.Actions.Count} " +
                $"packHash={packHash} abstainGuardIntervened={guardIntervened} " +
                $"fallbackUsed={fallbackUsed} " +
                $"interviewQuestions={reply.Interview?.Questions.Count ?? 0} " +
                $"webConsent={reply.Interview?.Questions.Any(q => q.Presentation == InterviewPresentation.Consent) ?? false} " +
                $"unverified={reply.Provenance.Unverified} " +
                $"gapInvestigation={gapInvestigation} " +
                $"webMode={webMode} " +
                $"turnId={RunContext.Current?.TurnId ?? "none"} " +
                (RunContext.TurnDetail ?? "flow=none")),
            cancellationToken).ConfigureAwait(false);
    }

    private static string ComputeHash(string input) => QueryHasher.ComputeHash(input);
}
