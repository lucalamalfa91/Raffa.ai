using Raffa.SharedKernel;

namespace Raffa.Chat.Application.Ask4;

/// <summary>
/// AskRaffa 4.0 Chiedi (README §7): one question, one answer that cites its source or says it
/// cannot. The production implementation (<c>Raffa.AiFlows</c>) runs the existing sourced-answer
/// engine; fixture mode registers a deterministic one over <see cref="Raffa.SharedKernel.Contracts.ContractFacts"/>
/// that equals the prototype's <c>answer()</c>.
/// </summary>
public interface IAskAnswerService
{
    /// <param name="contractId">The contract the user picked; null = resolve it from the text
    /// (a validated contract's supplier named in the question).</param>
    /// <param name="recentTurns">The conversation so far (role <c>you</c>/<c>raffa</c>, oldest first), so a follow-up
    /// is read in context and the chat does not start over at every message.</param>
    /// <param name="lang"><c>it</c> | <c>en</c>.</param>
    Task<AskAnswer> AnswerAsync(
        TenantId tenantId,
        string userId,
        string question,
        EntityId? contractId,
        string lang,
        IReadOnlyList<(string Role, string Markdown)>? recentTurns = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The answer. <see cref="NeedsContract"/>: the question names no contract — the UI shows the
/// parameter picker ("Per quale contratto?") and asks again with the chosen one.
/// </summary>
public sealed record AskAnswer(
    bool NeedsContract,
    string? Text,
    string? Source,
    AskAnswerCta? Cta,
    EntityId? ContractId,
    string? Supplier)
{
    public static AskAnswer Need { get; } = new(true, null, null, null, null, null);
}

/// <summary>A follow-up the answer offers: <c>compareQuote</c> (pick an offer) or <c>openContract</c>.</summary>
public sealed record AskAnswerCta(string Kind, EntityId? ContractId);

/// <summary>AskRaffa 4.0 switches read by the answer services (<c>/api/capabilities</c> flags).</summary>
public sealed class AskAnswerOptions
{
    /// <summary><c>askraffa4.noBenchmark</c>: market / benchmark questions get the declared-limit
    /// refusal and no market pack is ever consulted.</summary>
    public bool NoBenchmark { get; set; } = true;
}
