using Raffa.Quotes.Application.Strategy;
using Raffa.SharedKernel;

namespace Raffa.Quotes.Domain;

/// <summary>
/// The final negotiated outcome for one <see cref="Quote"/> (task E05/F03/US02/T01,
/// negotiation-outcome; parent story us-02-outcome-capture AC-1 "<c>POST
/// /api/negotiations/outcomes</c> records original/target/final/saving/discount/duration/levers";
/// product spec §6 data model "NegotiationOutcome | original quote, target, final price, savings,
/// discount, duration, levers used"; spec §12.2 "Negotiation outcome capture"; module-map.md
/// "Quotes | Quote, QuoteLine, Assessment, NegotiationOutcome | /api/quotes,
/// /api/negotiations/outcomes"). Captures what actually happened at the end of a negotiation as
/// permissioned proprietary learning data feeding spec §12.3's "data flywheel" (Appendix C rule 9
/// "Capture negotiation outcomes ... from day one").
///
/// <para>
/// <b>Append-only, never a "correction" of a previous capture</b> (parent story AC-3 "Outcome is
/// versioned + audit-tracked", App C #5/#9): unlike <c>Raffa.Documents.Contracts.Domain
/// .ContractVersion</c> (which snapshots a single, still-mutable <c>Contract</c> row across
/// successive corrections), there is no <c>PATCH</c>/<c>PUT</c> for this entity anywhere in spec
/// Appendix A — only <c>POST</c>. <see cref="Application.Outcome.NegotiationOutcomeService
/// .CaptureAsync"/> is the only writer and only ever <c>Add</c>s a new row; nothing in this module
/// ever updates or deletes one. "Versioned" is satisfied the same way
/// <c>Raffa.Savings.Domain.RealizedSavings</c>'s own doc comment already establishes for the
/// identical App C #5/#9 pairing on a sibling "capture a final, consequential figure" entity: a
/// second outcome recorded for the same <see cref="QuoteId"/> (a renegotiation, or a correction to
/// an earlier capture) is simply another row, ordered by <see cref="CapturedAt"/> — the original
/// capture stays reachable rather than being destructively overwritten (Appendix C rule 5).
/// </para>
///
/// <para>
/// Every numeric field below is caller-supplied (<see cref="Application.Outcome
/// .NegotiationOutcomeCaptureRequest"/>) except <see cref="RealizedSaving"/>/
/// <see cref="DiscountPercent"/>, which <see cref="Application.Outcome.NegotiationOutcomeCalculator"/>
/// derives deterministically from <see cref="OriginalQuoteTotal"/>/<see cref="FinalPrice"/> — the
/// same "never let the model (or the caller) guess an arithmetic fact code can compute exactly"
/// posture <c>NegotiationStrategyCalculator</c>/<c>TargetSavingCalculator</c> already establish for
/// this module's other money math (AC-3 of the sibling us-01 story; Appendix C rule 6). No
/// aggregation from this quote's own <see cref="QuoteLine"/> rows is attempted here — like
/// <see cref="Quote.Supplier"/>/<see cref="Quote.Currency"/>/<see cref="Quote.Geography"/>/
/// <see cref="Quote.PurchaseDate"/>, an explicit, caller-supplied fact beats an inferred one when
/// this module has no deterministic way to roll up a whole quote's total yet (see <see cref="Quote"/>'s
/// own doc comment for the identical reasoning).
/// </para>
/// </summary>
public sealed class NegotiationOutcome : TenantScopedEntity
{
    /// <summary>The quote this outcome concludes. Same-module reference, kept as a plain indexed id
    /// column rather than a physical FK — mirrors <c>Raffa.Savings.Domain.RealizedSavings
    /// .SavingsOpportunityId</c>'s own doc comment ("this module's own 'no cross-aggregate FK'
    /// convention rather than introducing the one exception"). Existence for this tenant is
    /// validated by <see cref="Application.Outcome.NegotiationOutcomeService.CaptureAsync"/> before
    /// this row is ever created (the same tenant-scoped lookup <c>MarketAssessmentService
    /// .AssessAsync</c> already performs for the identical id).</summary>
    public required EntityId QuoteId { get; set; }

    /// <summary>The quoted total before negotiation (spec §12.2 example: "Original Quote: CHF
    /// 520k") — explicit caller input (see this type's own doc comment for why), never summed from
    /// this quote's own <see cref="QuoteLine.ExtendedPrice"/> rows (no task has built that
    /// quote-level rollup yet). Always <c>&gt; 0</c> (<see cref="Application.Outcome
    /// .NegotiationOutcomeService.OriginalQuoteTotalMustBePositiveError"/>).</summary>
    public required decimal OriginalQuoteTotal { get; set; }

    /// <summary>The opening/recommended target price this negotiation aimed for (spec §12.2 example:
    /// "Target: CHF 420k") — echoes what <c>NegotiationStrategyService</c> would have recommended
    /// for this quote, but not read back from it directly: that service computes a strategy
    /// per-<see cref="QuoteLine"/>, never rolled up to one quote-level figure (see
    /// <c>QuoteNegotiationStrategy</c>'s own doc comment), so a single quote-level target here is
    /// necessarily a separate, caller-supplied fact. <see langword="null"/> when no target was set
    /// ahead of this negotiation (for example, the market assessment itself had insufficient
    /// benchmark data to anchor one — <c>LineNegotiationStrategy.OpeningTarget</c> is nullable for
    /// the identical reason) — outcome capture is never blocked on a target that was honestly never
    /// available (Appendix C rule 9's "from day one" would be defeated by requiring a fact this
    /// module cannot always supply).</summary>
    public decimal? TargetPrice { get; set; }

    /// <summary>The actual negotiated price (spec §12.2 example: "Final Price: CHF 435k"). Always
    /// <c>&gt; 0</c> (<see cref="Application.Outcome.NegotiationOutcomeService
    /// .FinalPriceMustBePositiveError"/>) — never clamped to or validated against
    /// <see cref="OriginalQuoteTotal"/>/<see cref="TargetPrice"/>; if the final price legitimately
    /// ends up above the original quote, <see cref="RealizedSaving"/> is honestly negative rather
    /// than silently clamped (Appendix C rule 10: no fabricated precision, not even an optimistic
    /// one).</summary>
    public required decimal FinalPrice { get; set; }

    /// <summary>Deterministic <see cref="OriginalQuoteTotal"/> minus <see cref="FinalPrice"/> (spec
    /// §12.2 example: "Realized Saving: CHF 85k") — computed by
    /// <see cref="Application.Outcome.NegotiationOutcomeCalculator.Compute"/>, never asked of a
    /// caller or a model (Appendix C rule 6).</summary>
    public required decimal RealizedSaving { get; set; }

    /// <summary>Deterministic <see cref="RealizedSaving"/> as a percentage of
    /// <see cref="OriginalQuoteTotal"/> (spec §12.2 example: "Discount: 16.3%") — same computed-not-
    /// supplied posture as <see cref="RealizedSaving"/>.</summary>
    public required decimal DiscountPercent { get; set; }

    /// <summary>How many days the negotiation took, start to close (spec §12.2 example: "Duration:
    /// 24 days") — caller-supplied; always <c>&gt;= 0</c>
    /// (<see cref="Application.Outcome.NegotiationOutcomeService
    /// .NegotiationDurationDaysMustBeNonNegativeError"/>).</summary>
    public required int NegotiationDurationDays { get; set; }

    /// <summary>Which of the seven canonical <see cref="NegotiationLeverType"/> values were actually
    /// used to reach <see cref="FinalPrice"/> (spec §12.2 example: "Levers Used: 36-month commitment;
    /// quarter-end timing" — <see cref="NegotiationLeverType.Term"/>/<see cref="NegotiationLeverType
    /// .QuarterEnd"/> here). Reuses <c>NegotiationStrategyCalculator</c>'s own closed vocabulary
    /// (task E05/F03/US01/T01) rather than free text, so which levers actually work — the whole
    /// point of spec §12.3's "better recommendation" data-flywheel loop — is a queryable, aggregable
    /// dimension, not prose a later task would have to re-parse. Always at least one entry
    /// (<see cref="Application.Outcome.NegotiationOutcomeService.LeversUsedRequiredError"/>) —
    /// persisted as a comma-separated list of enum names (see
    /// <see cref="Infrastructure.Configurations.NegotiationOutcomeConfiguration"/>'s own value
    /// converter) rather than a native Postgres array: no other table in this module stores a
    /// collection column, and every other closed-vocabulary column here (<c>Quote.ProcessingStatus</c>,
    /// <c>QuoteLine.MatchStatus</c>) already uses the simpler "enum(-list) as string" convention.</summary>
    public required IReadOnlyList<NegotiationLeverType> LeversUsed { get; set; }

    /// <summary>When this outcome was recorded (caller-request-time, via <c>IClock</c> — not a
    /// database default, same "no hidden clock" convention <c>RealizedSavings.RealizedAt</c> already
    /// follows).</summary>
    public required DateTimeOffset CapturedAt { get; set; }

    /// <summary>Task E05/F03/US02/T02 (outcome-propagation; parent story us-02-outcome-capture AC-2
    /// "Realized savings surface on the savings dashboard (cross-wave)"). Which
    /// <c>Raffa.Savings.Domain.SavingsOpportunity</c> (if any) this outcome's own
    /// <see cref="RealizedSaving"/> was propagated onto — an explicit, caller-supplied fact, never
    /// inferred: <c>Raffa.Savings.Domain.SavingsOpportunity</c> carries no <c>QuoteId</c> of its
    /// own (that entity's own doc comment: a quote-originated opportunity is "R4, out of scope per
    /// epic-04's own 'Out of scope' list"), so there is no derivable link this module could compute
    /// on its own between a <see cref="Quote"/> and a tracked opportunity — the same "explicit
    /// caller-supplied fact beats an inferred one when this module has no deterministic way to roll
    /// up" reasoning this type's own doc comment already gives for <see cref="OriginalQuoteTotal"/>/
    /// <see cref="TargetPrice"/>.
    ///
    /// <para>
    /// Plain <see cref="EntityId"/>, not a foreign key: ADR-002 forbids <c>Raffa.Quotes</c> from
    /// referencing <c>Raffa.Savings</c> at all (<c>Raffa.ArchitectureTests
    /// .DependencyDirectionTests</c>'s allow-list for this module is exactly
    /// <c>[SharedKernel, Benchmark]</c>), so this module cannot validate the id names a real,
    /// tenant-owned opportunity — the same "cross-module reference by id only" treatment
    /// <c>Raffa.Savings.Domain.SavingsOpportunity.ContractId</c>/<c>SupplierId</c> already give
    /// their own cross-module references. <c>Raffa.Api.NegotiationOutcomePropagationService</c> —
    /// the composition root, the one place allowed to see both modules at once — is what actually
    /// resolves this id and reports whether propagation succeeded; a value here only records what the
    /// caller asked for, not whether it worked.
    /// </para>
    ///
    /// <para>
    /// <see langword="null"/> when the caller did not supply one — not every negotiated outcome
    /// traces back to a pre-tracked <c>SavingsOpportunity</c> (e.g. a new-purchase quote with no
    /// portfolio-contract-comparison opportunity ever identified for it), and outcome capture must
    /// never be blocked on a link this module honestly does not always have (Appendix C rule 9 "from
    /// day one", the same posture <see cref="TargetPrice"/>'s own doc comment already documents for
    /// an analogous gap).
    /// </para>
    /// </summary>
    public EntityId? SavingsOpportunityId { get; set; }
}
