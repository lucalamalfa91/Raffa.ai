using Raffa.SharedKernel;

namespace Raffa.Renewals.Application;

/// <summary>
/// The minimal, validated contract terms <see cref="RenewalEngine"/> needs to compute a renewal
/// date and cancellation deadline (product spec §9.1 "Renewal generation"; parent story
/// us-01-deterministic-dates AC-1).
///
/// <para>
/// This intentionally does not reference <c>Raffa.Documents.Contracts.Domain.Contract</c> —
/// ADR-002's dependency-direction rule allows <c>Raffa.Renewals</c> to reference only
/// <c>Raffa.SharedKernel</c> and <c>Raffa.Benchmark</c>
/// (<c>Raffa.ArchitectureTests.DependencyDirectionTests</c>'s allow-list for this module; see
/// <c>backend/README.md</c>'s "Dependency direction" table). This is the same shape decision
/// <c>Raffa.Chat.Application.ContractFact</c> already made for the same reason — a composition
/// root (<c>Raffa.Api</c>, which may reference every module) maps a real <c>Contract</c> row
/// onto this DTO 1:1; no task in this wave wires that composition yet (this task's own wave-spec
/// entry depends on nothing but the empty <c>Raffa.Renewals</c> scaffold, not on a
/// Documents/Contracts capability). Until that wiring lands, a caller supplies these values
/// however it likes — a test's fixture data today, a real query result mapped 1:1 later.
/// </para>
/// </summary>
/// <param name="ContractId">The source <c>Contract.Id</c> — echoed back on
/// <see cref="RenewalCalculationResult"/> so a caller processing many contracts (spec §9.1's
/// "daily scheduler for each active contract") can correlate a result to its input without
/// zipping two lists by hand.</param>
/// <param name="EndDate">The source <c>Contract.EndDate</c>. The anchor every date below is
/// computed from; null when the contract's end date has not been extracted/validated yet, the
/// same "not enough data" gap <see cref="Raffa.Renewals.Domain.RenewalCalculationStatus.CannotDetermine"/>
/// exists for.</param>
/// <param name="AutoRenewal">The source <c>Contract.AutoRenewal</c>. A contract only has a
/// renewal date/cancellation deadline to compute when this is true — the same rule
/// <c>Raffa.Documents.Contracts.Application.PortfolioListItem.RenewalDate</c> and
/// <c>Raffa.Chat.Application.DeterministicQueryHandler</c>'s renewal-window filter already
/// apply; see <see cref="Raffa.Renewals.Domain.RenewalCalculationStatus.NoRenewal"/>.</param>
/// <param name="CancellationNoticeDays">
/// How many days before <see cref="EndDate"/> notice must be given to cancel out of
/// auto-renewal — the raw structured fact product spec §7.3's own extraction-evidence example
/// names (<c>"cancellation_notice_days": 90</c>), a day-count the engine subtracts from
/// <see cref="EndDate"/> (Appendix C rule 6: deterministic arithmetic, not an LLM-produced date).
/// Null when that notice period has not been extracted/validated yet.
///
/// <para>
/// Honest gap: <c>Raffa.Documents.Contracts.Domain.Contract</c> has no persisted column for
/// this today — its "dates" extraction stage
/// (<c>Raffa.Documents.Contracts.Application.Extraction.StagedExtractionService.ApplyDatesFact</c>)
/// still writes a raw <c>cancellationDeadline</c> date straight onto <c>Contract</c> instead of a
/// notice-day count, which is exactly the "date invented outside deterministic code" gap this
/// engine exists to close (parent story: "dates are never invented by the LLM"). Adding that
/// column/extraction field and mapping it onto this parameter is a follow-up composition task
/// (`Raffa.Documents.Contracts` is a different module and a different task's file scope — "do
/// not touch" per this task's own instructions), the same kind of gap
/// <c>Raffa.Chat.Application.ContractFact</c> already documents for its own callers.
/// </para>
/// </param>
public sealed record ContractRenewalTerms(
    EntityId ContractId,
    DateOnly? EndDate,
    bool AutoRenewal,
    int? CancellationNoticeDays);
