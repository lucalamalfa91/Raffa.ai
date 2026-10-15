using Raffa.AiFlows.Ask.Gate;
using static Raffa.AiFlows.Ask.Packs.ContractResolution;
using static Raffa.AiFlows.Ask.Packs.PackItemFactory;
using Raffa.AiFlows.Ask.Interview;
using Raffa.AiFlows.Ask.Planning;
using Raffa.AiFlows.CapabilityGaps.Catalog;
using Raffa.AiFlows.CapabilityGaps.Copy;
using Raffa.AiFlows.CapabilityGaps.Drafting;
using Raffa.AiFlows.CapabilityGaps.Investigation;
using Raffa.AiFlows.CapabilityGaps.Replies;
using Raffa.AiFlows.Negotiation.Orchestration;
using Raffa.AiFlows.Shared.Routing;
using Raffa.AiGateway.Telemetry;
using System.Globalization;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Interview;
using Raffa.Chat.Application.Language;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Domain;
using Raffa.Documents.Contracts.Application;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Ask.Orchestration;

/// <summary>
/// The <see cref="GateLabel.CapabilityGap"/> branch of the composition root (ADR-030 D1–D3): a
/// turn that asks Raffa to perform an operation it cannot do yet leaves with an honest preface in
/// the question's language, the nearest real alternative and an offer to report the gap — never a
/// retrieval-then-abstain, never the feature tour. For the email/letter gaps the alternative is a
/// drafted negotiation email written from the same Q3 pack a renewal-strategy turn gets (contract
/// facts, lever calculations, clause evidence, playbook, plus what Ask's agentic flow adds: the
/// market data check, the market researcher's notes and the council plays) by
/// <see cref="NegotiationDraftingWorkflow"/>; for the other gaps it is a deep link into the screen
/// that already holds the answer. (A gap the capability investigator finds, ADR-031, is never this
/// turn's reply: it follows the answer as a separate message — <see cref="CapabilityCheckRunner"/>.)
/// </summary>
public sealed partial class AskCopilotService
{
    /// <summary>
    /// INV-03: the turn's <c>ask.capability_trigger</c> audit row — the mode, T1/T2/T3, whether the
    /// check ran and why. Written for every fresh in-domain turn the check could look at (also when
    /// the kill switch is off, and in Always mode, where the flags say what Triggered would have
    /// done). Never the question, never any text; the matching <c>ask.capability_outcome</c> row
    /// carries the verdict once the check finishes (<see cref="CapabilityCheckRunner"/>). Telemetry
    /// never fails a turn.
    /// </summary>
    private async Task WriteTriggerAuditAsync(
        TenantId tenantId,
        string actor,
        string turnId,
        string mode,
        TriggerVerdict verdict,
        bool ran,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditWriter.WriteAsync(
                new AuditEntry(
                    tenantId,
                    actor,
                    CapabilityCheckRunner.TriggerAuditAction,
                    CapabilityCheckRunner.TriggerAuditResourceType,
                    turnId,
                    clock.UtcNow,
                    $"turnId={turnId} mode={mode} t1={verdict.T1} t2={verdict.T2} t3={verdict.T3} " +
                    $"ran={ran} reason={verdict.Reason} t3Language={verdict.T3Language ?? "none"} " +
                    $"lexicon={investigatorTrigger.LexiconVersion}"),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Fail-open: the answer is already built; a missing telemetry row must not lose it.
        }
    }

    /// <summary>A turn the capability investigator may look at (ADR-031): typed by the user, not
    /// resolved by key from an earlier turn (an interview option, a web consent or a decline),
    /// which continues a turn that was already investigated.</summary>
    private static bool IsFreshTurn(AskTurnHints hints) =>
        hints.ForcedIntent is null &&
        hints.ForcedContractId is null &&
        hints.ForcedSupplierName is null &&
        hints.AuthorizedWebResearch is null &&
        !hints.DeclinedWebResearch;
}
