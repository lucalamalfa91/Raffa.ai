using Raffa.Chat.Application.Ask4;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;

namespace Raffa.Chat.Infrastructure.Fixtures;

/// <summary>
/// Fixture mode only (registered when <c>Fixtures:Enabled</c>): the Chiedi answers are the
/// prototype's deterministic <c>answer()</c> over the tenant's validated <see cref="ContractFacts"/>
/// (<see cref="ContractFactsAnswerer"/>), so the UI shows exactly the prototype's texts.
/// </summary>
public sealed class FixtureAskAnswerService(IContractFactsReader facts, IClock clock) : IAskAnswerService
{
    public async Task<AskAnswer> AnswerAsync(
        TenantId tenantId, string userId, string question, EntityId? contractId, string lang,
        IReadOnlyList<(string Role, string Markdown)>? recentTurns = null, CancellationToken cancellationToken = default)
    {
        var validated = await facts.ListValidatedAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var contract = contractId is { } id
            ? validated.FirstOrDefault(c => c.ContractId == id.Value)
              ?? await facts.GetAsync(tenantId, id, cancellationToken).ConfigureAwait(false)
            : AskRules.NamedContract(question, OrderForNaming(validated));
        if (contract is null)
        {
            return AskAnswer.Need;
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        return ContractFactsAnswerer.Answer(question, contract, today, lang);
    }

    /// <summary>The prototype scans its contracts in id order (Salesforce, Microsoft, AWS, DocuSign).</summary>
    private static List<ContractFacts> OrderForNaming(IReadOnlyList<ContractFacts> validated) =>
        validated.OrderBy(c => c.FixtureId ?? int.MaxValue).ToList();
}
