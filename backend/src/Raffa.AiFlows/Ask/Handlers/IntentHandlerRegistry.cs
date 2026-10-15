using Raffa.AiFlows.Ask.Orchestration;
using Raffa.Chat.Domain;

namespace Raffa.AiFlows.Ask.Handlers;

/// <summary>Resolves the one <see cref="IIntentHandler"/> of an intent (F1-T08).</summary>
public sealed class IntentHandlerRegistry
{
    private readonly Dictionary<AskIntent, IIntentHandler> _byIntent = [];
    private readonly IIntentHandler _fallback;

    /// <exception cref="InvalidOperationException">Two handlers claim the same intent.</exception>
    public IntentHandlerRegistry(IEnumerable<IIntentHandler> handlers, DefaultIntentHandler fallback)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(fallback);
        _fallback = fallback;

        foreach (var handler in handlers)
        {
            foreach (var intent in handler.Intents)
            {
                if (!_byIntent.TryAdd(intent, handler))
                {
                    throw new InvalidOperationException(
                        $"Intent {intent} is claimed by both {_byIntent[intent].GetType().Name} and {handler.GetType().Name}.");
                }
            }
        }
    }

    /// <summary>The handler of <paramref name="intent"/>; the empty-pack fallback for an intent with
    /// none (<see cref="AskIntent.PortfolioMarketPosition"/> and any value added later), which is
    /// exactly what the former switch's <c>_ =&gt; []</c> arm did.</summary>
    public IIntentHandler For(AskIntent intent) => _byIntent.TryGetValue(intent, out var handler) ? handler : _fallback;
}

/// <summary>The handler of every intent with no pack of its own: an empty pack (the former <c>_ =&gt; []</c> arm).</summary>
public sealed class DefaultIntentHandler : IIntentHandler
{
    public IReadOnlyList<AskIntent> Intents { get; } = [];

    public Task<IntentOutcome> HandleAsync(InDomainTurn turn, CancellationToken cancellationToken) =>
        Task.FromResult<IntentOutcome>(new IntentOutcome.Pack([]));
}
