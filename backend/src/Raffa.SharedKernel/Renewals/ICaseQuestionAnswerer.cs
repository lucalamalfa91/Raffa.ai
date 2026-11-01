namespace Raffa.SharedKernel.Renewals;

/// <summary>
/// AskRaffa 4.0 (README §3 "Composer", BACKEND-SPEC §2.8): a free question asked inside a renewal
/// case, answered about that case's contract. Implemented by <c>Raffa.Chat</c> over the Chiedi
/// answer service (sourced answer, or an honest "I can't"); the Renewals case composer calls it and
/// then appends its own "back to the pending step" line.
/// </summary>
public interface ICaseQuestionAnswerer
{
    /// <param name="lang"><c>it</c> | <c>en</c>; null = Italian.</param>
    Task<CaseQuestionAnswer> AnswerAsync(
        TenantId tenantId,
        EntityId contractId,
        string text,
        string? lang = null,
        CancellationToken cancellationToken = default);
}

/// <summary>The answer and what it rests on (<c>Fonte: …</c>); a refusal names its limit as the
/// source, so <see cref="Source"/> is null only when there is nothing to cite at all.</summary>
public sealed record CaseQuestionAnswer(string Text, string? Source);
