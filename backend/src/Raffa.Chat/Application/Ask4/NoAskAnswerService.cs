using Raffa.SharedKernel;

namespace Raffa.Chat.Application.Ask4;

/// <summary>
/// The Chat module's own default <see cref="IAskAnswerService"/>: a host without the AI flows layer
/// answers honestly that it cannot, with no invented content. <c>AddAiFlows</c> replaces it with the
/// sourced-answer engine; fixture mode with the deterministic prototype answers.
/// </summary>
public sealed class NoAskAnswerService : IAskAnswerService
{
    public Task<AskAnswer> AnswerAsync(
        TenantId tenantId,
        string userId,
        string question,
        EntityId? contractId,
        string lang,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new AskAnswer(
            false,
            lang == "en"
                ? "I cannot answer here: the answer service is not configured."
                : "Non posso rispondere qui: il servizio di risposta non è configurato.",
            lang == "en" ? "no source available" : "nessuna fonte disponibile",
            null,
            contractId,
            null));
}
