using Raffa.SharedKernel;
using Raffa.SharedKernel.Renewals;

namespace Raffa.Chat.Application.Ask4;

/// <summary><see cref="ICaseQuestionAnswerer"/> over the Chiedi answer service, scoped to the case's contract.</summary>
public sealed class CaseQuestionAnswerer(IAskAnswerService answers) : ICaseQuestionAnswerer
{
    /// <summary>The identity the case composer's questions run as (the engine audits it).</summary>
    public const string Actor = "renewal-case";

    public async Task<CaseQuestionAnswer> AnswerAsync(
        TenantId tenantId, EntityId contractId, string text, string? lang = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var answer = await answers
            .AnswerAsync(tenantId, Actor, text, contractId, lang == "en" ? "en" : "it", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return answer.NeedsContract || answer.Text is null
            ? new CaseQuestionAnswer(
                lang == "en"
                    ? "I find no validated contract for this case, so I cannot answer with a source."
                    : "Non trovo un contratto validato per questa pratica, quindi non posso rispondere con una fonte.",
                lang == "en" ? "no evidence above threshold" : "nessuna evidenza sopra soglia")
            : new CaseQuestionAnswer(answer.Text, answer.Source);
    }
}
