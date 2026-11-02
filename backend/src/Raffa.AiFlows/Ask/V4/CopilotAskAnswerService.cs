using Raffa.AiFlows.Ask.Orchestration;
using Raffa.Chat.Application.Ask4;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;

namespace Raffa.AiFlows.Ask.V4;

/// <summary>
/// AskRaffa 4.0 Chiedi answers in production: the existing sourced-answer engine
/// (<see cref="AskCopilotService"/>) scoped to one validated contract. The 4.0 rules come first:
/// no named contract → the picker; a market / benchmark question with <c>askraffa4.noBenchmark</c>
/// on → the declared-limit refusal, before the engine (so no market pack is ever built). An answer
/// without a citation is returned with "nessuna evidenza sopra soglia" as its source — it never
/// states a fact without saying what it rests on.
/// </summary>
public sealed class CopilotAskAnswerService(
    AskCopilotService copilot, IContractFactsReader facts, AskAnswerOptions options) : IAskAnswerService
{
    public async Task<AskAnswer> AnswerAsync(
        TenantId tenantId, string userId, string question, EntityId? contractId, string lang, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        // The interface language (IT/EN) decides the language of the model's answer and of every fallback text.
        using var responseLanguage = Raffa.SharedKernel.Language.ResponseLanguage.Use(lang);
        var validated = await facts.ListValidatedAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var contract = contractId is { } id
            ? validated.FirstOrDefault(c => c.ContractId == id.Value)
            : AskRules.NamedContract(question, validated);
        if (contract is null)
        {
            return AskAnswer.Need;
        }

        if (options.NoBenchmark && AskRules.IsMarketQuestion(question))
        {
            return AskRules.MarketRefusal(contract, lang);
        }

        var reply = await copilot
            .AskAsync(tenantId, question, [], userId, new EntityId(contract.ContractId), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var citation = reply.Citations.FirstOrDefault();
        if (citation is null || string.IsNullOrWhiteSpace(reply.AnswerMarkdown))
        {
            return string.IsNullOrWhiteSpace(reply.AnswerMarkdown)
                ? AskRules.NoEvidence(contract, lang)
                : new AskAnswer(false, reply.AnswerMarkdown, lang == "en" ? "no evidence above threshold" : "nessuna evidenza sopra soglia",
                    null, new EntityId(contract.ContractId), contract.Supplier);
        }

        var source = citation.Title
            + (citation.Page is { } page ? " p." + page : "")
            + (string.IsNullOrWhiteSpace(citation.Section) ? "" : " " + citation.Section);
        return new AskAnswer(false, reply.AnswerMarkdown, source, null, new EntityId(contract.ContractId), contract.Supplier);
    }
}
