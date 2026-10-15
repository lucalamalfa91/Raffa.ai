using Raffa.AiFlows.Shared.Audit;
using System.Globalization;
using Raffa.AiFlows.Ask.Answering;
using Raffa.AiFlows.Ask.Gate;
using Raffa.AiFlows.Ask.Interview;
using Raffa.AiFlows.Ask.Planning;
using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.Ask.Routing;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Reply;
using Raffa.AiFlows.Shared.Routing;
using Raffa.AiFlows.WebResearch.Orchestration;
using Raffa.Benchmark.Contracts;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Interview;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Domain;
using Raffa.Insights.Contracts;
using Raffa.Market.Contracts;
using Raffa.Documents.Contracts.Application;
using Raffa.Insights.Application;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Ask.Packs;

/// <summary>
/// Pure helpers that resolve which contract a turn is about and how it is named. Moved verbatim
/// from <see cref="AskCopilotService"/> (F1 Ask split, PR-1).
/// </summary>
internal static class ContractResolution
{
    /// <summary>
    /// Task E27/F05/US01/T01 (NW-80; parent story us-01-supplier-resolution AC-2): which single
    /// contract a named-supplier turn answers about, replacing a plain <c>FirstOrDefault</c> over
    /// <paramref name="portfolio"/> (whichever row the portfolio query happened to return first for
    /// that supplier — never a deliberate choice, and never told to the user) with two rules, tried
    /// in order:
    ///
    /// <list type="number">
    /// <item><b>Scoped id wins.</b> <paramref name="scopedContractItem"/> is <see cref="AskCopilotService.AskAsync"/>'s
    /// own already-resolved <c>Conversation.ScopeContractId</c> (Contract 360's "Ask about it") — it
    /// names one exact contract with no ambiguity at all, so it is returned unchanged and no
    /// disambiguation note is produced (the user already knows which contract they opened this chat
    /// from). <see cref="AskCopilotService.BuildInDomainReplyAsync"/> passes <see langword="null"/> here instead for
    /// <see cref="AskIntent.PortfolioMarketPosition"/> (lock 4; task E27/F02/US01/T01, NW-76), which
    /// is never narrowed to a scoped contract, so that intent always falls through to rule 2.</item>
    /// <item><b>Else, the soonest cancellation deadline / renewal wins</b> — the same ordering
    /// <c>Raffa.Renewals.Application.RenewalPipelineBuilder.Build</c> uses: cancellation deadline
    /// when known, else renewal date, unknown-urgency rows last. Sorting directly on
    /// <see cref="PortfolioListItem.CancellationDeadline"/>/<see cref="PortfolioListItem.RenewalDate"/>
    /// *is* that ordering, not an approximation of it —
    /// <c>Raffa.Renewals.Application.RenewalEngine.Calculate</c>'s own doc comment records that its
    /// <c>RenewalDate</c> "reproduces <see cref="PortfolioListItem.RenewalDate"/>'s own convention on
    /// purpose". When more than one contract matches, <see cref="BuildMultiContractDisambiguationItem"/>
    /// is what keeps the pick from being a silent merge.</item>
    /// </list>
    /// </summary>
    /// <returns>The contract this turn answers about (or <see langword="null"/> when no supplier was
    /// named, or a named supplier has no contract at all — <see cref="GateLabel.NeedsDocument"/>
    /// already handled the latter upstream so this is not expected in practice), and a
    /// <see cref="PackCorpus.Calc"/> pack item naming the choice, only when the choice was actually
    /// ambiguous.</returns>
    internal static (PortfolioListItem? Item, PackItem? DisambiguationItem, IReadOnlyList<PortfolioListItem> Matches) ResolveNamedContractItem(
        PortfolioListItem? scopedContractItem,
        string? namedSupplier,
        PortfolioPage portfolio,
        IReadOnlyDictionary<EntityId, string> supplierNames)
    {
        if (scopedContractItem is not null)
        {
            return (scopedContractItem, null, []);
        }

        if (namedSupplier is null)
        {
            return (null, null, []);
        }

        var matches = portfolio.Items
            .Where(item =>
                item.SupplierId is { } supplierId &&
                supplierNames.TryGetValue(new EntityId(supplierId), out var name) &&
                string.Equals(name, namedSupplier, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.CancellationDeadline ?? item.RenewalDate ?? DateOnly.MaxValue)
            .ThenBy(item => item.ContractId)
            .ToList();

        if (matches.Count == 0)
        {
            return (null, null, []);
        }

        var chosen = matches[0];

        if (matches.Count == 1)
        {
            return (chosen, null, matches);
        }

        return (chosen, BuildMultiContractDisambiguationItem(chosen, matches.Count, namedSupplier), matches);
    }

    /// <summary>
    /// NW-80's required disambiguation sentence: <c>"Using {Type} CT-01 (renews …). Ask if you meant
    /// another."</c> (task text, verbatim). A <see cref="PackCorpus.Calc"/> pack item, not free-form
    /// model prose, so the fact of which contract was picked is grounded evidence the `answer` role
    /// can cite rather than something it might choose to omit — both the fixture and the live gateway
    /// cite pack items, never invent independent sentences (<c>Raffa.AiGateway.Fixtures
    /// .FixtureAiGateway.AnswerFromPack</c>'s own "no chunk concatenation" convention), and
    /// <see cref="AskCopilotService.BuildInDomainReplyAsync"/> prepends this item so <see cref="PackBudget.Apply"/> and
    /// that fixture's first-N-items citation both always keep it.
    ///
    /// <para>
    /// "CT-01" is this one reply's own local label, not a persisted contract code (no such column
    /// exists on <c>Contract</c> yet) — it exists only so "ask if you meant another" has something
    /// short to refer back to. It is always "01" because <paramref name="chosen"/> is always the
    /// soonest match (position 1 of <see cref="ResolveNamedContractItem"/>'s own soonest-first sort)
    /// and this sentence only ever names the one contract actually chosen, never enumerates the
    /// others.
    /// </para>
    /// </summary>
    internal static PackItem BuildMultiContractDisambiguationItem(
        PortfolioListItem chosen, int matchCount, string supplierDisplayName)
    {
        const string ChosenContractLabel = "CT-01";

        // Never fabricated (Appendix C rule 10): renewal date first (what RenewalPipelineBuilder's
        // own sort actually ordered on when known), else the cancellation deadline, else the bare
        // end date, else an honest "unknown" rather than inventing one.
        var renewalText = chosen.RenewalDate is { } renewalDate
            ? $"renews {renewalDate:yyyy-MM-dd}"
            : chosen.CancellationDeadline is { } deadline
                ? $"cancellation deadline {deadline:yyyy-MM-dd}"
                : chosen.EndDate is { } endDate
                    ? $"ends {endDate:yyyy-MM-dd}"
                    : "renewal date unknown";

        var snippet =
            $"Using {chosen.Type} {ChosenContractLabel} ({renewalText}). {supplierDisplayName} has " +
            $"{matchCount} contracts on file — ask if you meant another.";

        return new PackItem(
            InsightsCitationKeys.Calc("multi-contract-resolution"),
            PackCorpus.Calc,
            $"{supplierDisplayName} — multiple contracts",
            null,
            null,
            null,
            snippet,
            null, // Href is always null for PackCorpus.Calc (PackItem.Href's own doc comment).
            null,
            null,
            "deterministic calculator",
            []);
    }

    /// <summary>
    /// The name a contract is shown under everywhere in a pack (R-SUP-04): the resolved supplier
    /// name from the per-turn map <see cref="AskCopilotService.AskAsync"/> builds, else the contract type -- never a
    /// guid. Synchronous twin of <see cref="ContractContextReader.ResolveDisplayNameAsync(PortfolioListItem, CancellationToken)"/>
    /// for the multi-contract paths that already hold the whole map.
    /// </summary>
    internal static string DisplayNameFor(PortfolioListItem item, IReadOnlyDictionary<EntityId, string> supplierNames) =>
        item.SupplierId is { } supplierId && supplierNames.TryGetValue(new EntityId(supplierId), out var name)
            ? name
            : item.Type.ToString();

    /// <summary>
    /// "Salesforce · MSA" -- the one title shape every contract-level pack item uses, so a reply
    /// can always say which supplier it means. When no supplier name resolved (the display name fell
    /// back to the type), the title says so instead of the misleading "MSA · MSA".
    /// </summary>
    internal static string ContractTitle(PortfolioListItem item, string displayName) =>
        string.Equals(displayName, item.Type.ToString(), StringComparison.Ordinal)
            ? $"Unnamed supplier · {item.Type}"
            : $"{displayName} · {item.Type}";

    internal static (string? Sentence, EntityId? ContractId) BuildPortfolioHook(
        PortfolioPage portfolio, IReadOnlyDictionary<EntityId, string> supplierNames)
    {
        if (portfolio.Items.Count == 0)
        {
            return (null, null);
        }

        var soonest = portfolio.Items
            .Where(item => item.CancellationDeadline is not null)
            .OrderBy(item => item.CancellationDeadline)
            .FirstOrDefault()
            ?? portfolio.Items.OrderBy(item => item.EndDate ?? DateOnly.MaxValue).First();

        var name = soonest.SupplierId is { } supplierId && supplierNames.TryGetValue(new EntityId(supplierId), out var resolved)
            ? resolved
            : soonest.Type.ToString();

        var sentence = soonest.EndDate is { } end
            ? $"your {name} contract runs until {end:yyyy-MM-dd}"
            : $"your {name} contract is on file and ready to ask about";

        return (sentence, new EntityId(soonest.ContractId));
    }

    internal static EntityId? FindContractIdBySupplier(
        PortfolioPage portfolio, IReadOnlyDictionary<EntityId, string> supplierNames, string namedSupplier)
    {
        var match = portfolio.Items.FirstOrDefault(item =>
            item.SupplierId is { } supplierId &&
            supplierNames.TryGetValue(new EntityId(supplierId), out var name) &&
            string.Equals(name, namedSupplier, StringComparison.OrdinalIgnoreCase));

        return match is null ? null : new EntityId(match.ContractId);
    }

    internal static ContractFact ToContractFact(PortfolioListItem item) =>
        new(
            new EntityId(item.ContractId),
            item.SupplierId is { } supplierId ? new EntityId(supplierId) : null,
            item.AnnualSpend,
            item.EndDate,
            item.AutoRenewal);
}
