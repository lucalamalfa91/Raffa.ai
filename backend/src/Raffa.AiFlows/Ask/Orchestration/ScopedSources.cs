using Raffa.AiFlows.Shared.Pack;

namespace Raffa.AiFlows.Ask.Orchestration;

/// <summary>
/// The source rule of a chat bound to one contract (Contract 360 / Renewals "Ask Raffa",
/// <c>Conversation.ScopeContractId</c>): the answer stands on that contract's own sources. A tenant
/// source of another contract -- a named one, or a "similar types" peer -- may only sit beside the
/// contract's own evidence, never replace it. Pure, so the rule is testable without a model.
/// </summary>
internal static class ScopedSources
{
    /// <summary>True when <paramref name="item"/> is a tenant source (a contract fact, a clause, a
    /// calculation over one contract): the kind of source that can belong to a contract.</summary>
    public static bool IsContractSource(PackItem item) =>
        item.Corpus is PackCorpus.Tenant or PackCorpus.Calc;

    /// <summary>True when <paramref name="item"/> is a source of the scoped contract itself.</summary>
    public static bool BelongsTo(PackItem item, Guid scopeContractId) =>
        IsContractSource(item)
        && Guid.TryParse(item.ContractId, out var id)
        && id == scopeContractId;

    /// <summary>
    /// The pack can ground an answer about the scoped contract: either it carries one of that
    /// contract's own sources, or it carries no contract source at all (a how-to or a market
    /// question asked from the bound chat). A pack whose only contract sources belong to other
    /// contracts cannot.
    /// </summary>
    public static bool PackCanGround(IReadOnlyList<PackItem> pack, Guid scopeContractId) =>
        !pack.Any(IsContractSource) || pack.Any(item => BelongsTo(item, scopeContractId));

    /// <summary>
    /// The answer's citations respect the rule: when the pack holds the scoped contract's own
    /// sources and the answer cites evidence (a contract or a market source), at least one cited
    /// source is the contract's own. An answer built only on other suppliers' material -- a peer
    /// contract or a market note about another product -- does not answer about this contract.
    /// A how-to answer that cites only Raffa features passes. Unknown keys are left to the
    /// grounding guard.
    /// </summary>
    public static bool CitationsBelong(IReadOnlyList<string> citationKeys, IReadOnlyList<PackItem> pack, Guid scopeContractId)
    {
        if (!pack.Any(item => BelongsTo(item, scopeContractId)))
        {
            return true;
        }

        var evidence = pack
            .Where(item => citationKeys.Contains(item.CitationKey, StringComparer.Ordinal))
            .Where(item => IsContractSource(item) || item.Corpus == PackCorpus.Market)
            .ToList();
        return evidence.Count == 0 || evidence.Any(item => BelongsTo(item, scopeContractId));
    }

    /// <summary>The honest opening when the bound contract's own sources cannot answer: it names the
    /// contract and says the other material found is not used, instead of answering from it.</summary>
    public static string OtherSourcesLead(string? supplierName, bool italian)
    {
        var name = string.IsNullOrWhiteSpace(supplierName) ? null : supplierName.Trim();
        return italian
            ? (name is null
                ? "Le fonti validate di questo contratto non rispondono a questa domanda, e non uso documenti di altri fornitori al suo posto.\n\n"
                : $"Le fonti validate del contratto {name} non rispondono a questa domanda, e non uso documenti di altri fornitori al suo posto.\n\n")
            : (name is null
                ? "This contract's validated sources do not answer this question, and I do not use other suppliers' documents in their place.\n\n"
                : $"The validated sources of the {name} contract do not answer this question, and I do not use other suppliers' documents in their place.\n\n");
    }
}
