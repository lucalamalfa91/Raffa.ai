using Raffa.AiFlows.Shared.Audit;
using System.Globalization;
using Raffa.AiFlows.Ask.Answering;
using Raffa.AiFlows.Ask.Interview;
using Raffa.AiFlows.Ask.Planning;
using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.Negotiation.Verdict;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Planning;
using Raffa.AiFlows.Shared.Reply;
using Raffa.AiFlows.Shared.Routing;
using Raffa.AiFlows.WebResearch.Orchestration;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Domain;
using Raffa.Insights.Savings;
using Raffa.Market.Contracts;
using Raffa.Documents.Contracts.Application;
using Raffa.Insights.Application;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiFlows.Ask.Reply;

/// <summary>
/// The next steps every abstain path of an Ask turn attaches (recovery actions, suggested
/// follow-ups, the proposal area). Moved verbatim from <see cref="AskCopilotService"/> (F1 Ask
/// split, PR-1).
/// </summary>
public sealed class AbstainRecovery(CapabilityRouting capabilityRouting)
{
    /// <summary>
    /// The recovery actions every abstain path this method's caller can reach attaches
    /// (E25/F05/US01/T01, story us-01-abstain-recovery-backend AC-1/AC-3; ADR-024 "every abstain
    /// has a clickable next step"). Never derived from <c>AiAnswerResult.ActionKeys</c> — an
    /// abstaining model has nothing grounded to suggest, and AC-2 requires a real catalog href
    /// regardless of what it returned. Zero validated contracts is the one failure Ask can actually
    /// unblock (upload something), so that case gets the Documents upload action; otherwise the
    /// recovery is where the answer to <paramref name="intent"/>'s kind of question lives: Savings +
    /// Renewals for a savings or priority question, Renewals + Portfolio for a date or renewal
    /// question, Quote check (+ Contract 360 when a contract is in scope) for a market question,
    /// the contract or Portfolio + Documents for a clause, Documents review for a document-status
    /// question, and Portfolio for everything else (including a turn with no intent, the unseen-scope
    /// refusal, whose own copy already says "Open Portfolio"). It used to be the Ask capability
    /// itself (<c>/ask</c>) — the screen the user is already on, so the button led nowhere. Every
    /// target is <see cref="CapabilityRoleGate.Any"/>, so this never role-gates away to an empty list.
    /// </summary>
    internal IReadOnlyList<CopilotAction> ResolveAbstainRecoveryActions(
        PortfolioPage portfolio, RoutingContext routingContext, AskIntent? intent = null)
    {
        if (portfolio.Items.Count == 0)
        {
            // Documents is Always-available, so HowTo(DocumentsKey) would Navigate to /documents.
            // UnknownSupplier is the catalog path that emits CopilotActionKind.Upload at /documents.
            return capabilityRouting.ResolveActions([CapabilityIntent.UnknownSupplier], routingContext);
        }

        CapabilityIntent[] intents = intent switch
        {
            AskIntent.Savings or AskIntent.PortfolioSavingsTarget or AskIntent.PortfolioStrategy =>
                [CapabilityIntent.Savings, CapabilityIntent.Deadline],
            AskIntent.RenewalStrategy or AskIntent.StructuredFact =>
                [CapabilityIntent.Deadline, CapabilityIntent.HowTo(CapabilityCatalog.PortfolioKey)],
            AskIntent.MarketCompare or AskIntent.PortfolioMarketPosition =>
                [CapabilityIntent.Benchmark],
            AskIntent.Clause when routingContext.ContractId is not null =>
                [CapabilityIntent.HowTo(CapabilityCatalog.ContractDetailKey), CapabilityIntent.HowTo(CapabilityCatalog.DocumentsKey)],
            AskIntent.Clause =>
                [CapabilityIntent.HowTo(CapabilityCatalog.PortfolioKey), CapabilityIntent.HowTo(CapabilityCatalog.DocumentsKey)],
            AskIntent.DocumentStatus =>
                [CapabilityIntent.HowTo(CapabilityCatalog.DocumentsAttentionKey)],
            _ => [CapabilityIntent.HowTo(CapabilityCatalog.PortfolioKey)],
        };

        return capabilityRouting.ResolveActions(intents, routingContext);
    }

    /// <summary>The area an abstain's suggested follow-ups come from: Documents while nothing is
    /// uploaded (the one thing that unblocks Ask), else the same grouping
    /// <see cref="ResolveAbstainRecoveryActions"/> routes to — savings, renewals, market position,
    /// documents — and the general pair for a clause or anything else.</summary>
    internal static IReadOnlyList<string> AbstainFollowUps(string question, PortfolioPage portfolio, AskIntent intent)
    {
        var capabilityKey = portfolio.Items.Count == 0
            ? CapabilityCatalog.DocumentsKey
            : intent switch
            {
                AskIntent.Savings or AskIntent.PortfolioSavingsTarget or AskIntent.PortfolioStrategy => CapabilityCatalog.SavingsKey,
                AskIntent.RenewalStrategy or AskIntent.StructuredFact => CapabilityCatalog.RenewalsKey,
                AskIntent.MarketCompare or AskIntent.PortfolioMarketPosition => CapabilityCatalog.QuoteCheckKey,
                AskIntent.DocumentStatus => CapabilityCatalog.DocumentsKey,
                _ => CapabilityCatalog.AskKey,
            };

        return GroundedFallbackAnswer.SuggestedQuestions(question, capabilityKey);
    }

    /// <summary>The kind of question <see cref="HelpfulFallbackAnswer.Proposal"/> writes its way
    /// forward for: the area the question's own words point at ("quando scade…" is a renewal
    /// question even when the planner files it as a clause lookup), else the intent's own area.</summary>
    internal static string? ProposalArea(string question, AskIntent intent) =>
        HelpfulFallbackAnswer.AreaFor(question) ?? intent switch
        {
            AskIntent.Savings or AskIntent.PortfolioSavingsTarget => CapabilityCatalog.SavingsKey,
            AskIntent.RenewalStrategy => CapabilityCatalog.RenewalsKey,
            AskIntent.MarketCompare or AskIntent.PortfolioMarketPosition => CapabilityCatalog.QuoteCheckKey,
            AskIntent.Clause => CapabilityCatalog.ContractDetailKey,
            AskIntent.DocumentStatus => CapabilityCatalog.DocumentsKey,
            _ => null,
        };

    /// <summary>The two-argument form the web flows receive as a delegate.</summary>
    internal AbstainRecoveryResolver Resolver => ResolveAbstainRecovery;

    /// <summary>The two-argument form of <see cref="ResolveAbstainRecoveryActions"/> the web flows
    /// receive as a delegate (<see cref="AbstainRecoveryResolver"/>): they never reference this class.</summary>
    private IReadOnlyList<CopilotAction> ResolveAbstainRecovery(PortfolioPage portfolio, RoutingContext routingContext) =>
        ResolveAbstainRecoveryActions(portfolio, routingContext);
}
