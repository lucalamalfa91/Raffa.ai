using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.Shared.Pack;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Domain;

namespace Raffa.AiFlows.Ask.Handlers;

/// <summary>
/// One handler per <see cref="AskIntent"/> (F1-T08): the former <c>plan.Intent switch</c> of the
/// in-domain pipeline plus the three intent-keyed short-circuits (web research, navigate/quote
/// route, notice question). The rules that follow the pack (data-check scope, research, council,
/// recovery, follow-ups) stay pure functions of the intent in <see cref="IntentPolicy"/>.
/// </summary>
public interface IIntentHandler
{
    /// <summary>The intents this handler owns (one handler per intent; the registry refuses overlaps).</summary>
    IReadOnlyList<AskIntent> Intents { get; }

    /// <summary>Either a reply that ends the turn before the agents run, or the intent's own pack.</summary>
    Task<IntentOutcome> HandleAsync(InDomainTurn turn, CancellationToken cancellationToken);
}

/// <summary>What a handler hands back to the in-domain pipeline.</summary>
public abstract record IntentOutcome
{
    private protected IntentOutcome()
    {
    }

    /// <summary>A reply that ends the turn: no pack, no agents, no composer.</summary>
    public sealed record Reply(CopilotReply Value, bool GuardIntervened = false, bool FallbackUsed = false) : IntentOutcome;

    /// <summary>The intent's pack, in the order the former switch produced it.</summary>
    public sealed record Pack(IReadOnlyList<PackItem> Items) : IntentOutcome;
}
