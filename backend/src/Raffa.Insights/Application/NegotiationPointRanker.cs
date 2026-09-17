using System.Globalization;
using Raffa.Benchmark.Contracts;
using Raffa.Insights.Contracts;
using Raffa.SharedKernel;

namespace Raffa.Insights.Application;

/// <summary>
/// The six canonical negotiation-point categories task E31/F02/US01/T01 (point-ranker; NW-96;
/// ADR-024 w19 cl. 23) fixes, in the council-decided priority order (parent story
/// us-01-point-ranker AC-2): above-band price, uncapped/high liability, auto-renew with short
/// notice, SLA/credits, term/volume, payment terms. <see cref="NegotiationPointRanker.Rank"/> never
/// reorders these — declaration order below <b>is</b> the rank order, the same "enum order is spec
/// order" convention <c>Raffa.Insights.Contracts.ContractNegotiationLeverType</c> already
/// establishes for the (deliberately different, ungrounded-by-design) seven-lever playbook.
/// </summary>
public enum NegotiationPointTopic
{
    AboveBandPrice,
    UncappedOrHighLiability,
    AutoRenewShortNotice,
    SlaCredits,
    TermVolume,
    PaymentTerms,
}

/// <summary>
/// <see cref="NegotiationPointTopic"/>'s two host-facing projections — the stable
/// <c>point_key</c>/human-label pair <c>Raffa.Renewals.Domain.RenewalNegotiationTodo</c>'s own doc
/// comment names ("one of the six canonical categories NW-96 orders"). Centralized here, not
/// hand-formatted at each call site, so the host mapping in <c>Raffa.Api.AskCopilotService</c> and
/// this module's own tests always agree on the same two strings per topic.
/// </summary>
public static class NegotiationPointTopicExtensions
{
    /// <summary>The stable, kebab-case identity <c>RenewalNegotiationTodo.PointKey</c> persists —
    /// never renamed once shipped (the idempotent upsert keys on it).</summary>
    public static string ToPointKey(this NegotiationPointTopic topic) => topic switch
    {
        NegotiationPointTopic.AboveBandPrice => "above-band-price",
        NegotiationPointTopic.UncappedOrHighLiability => "uncapped-or-high-liability",
        NegotiationPointTopic.AutoRenewShortNotice => "auto-renew-short-notice",
        NegotiationPointTopic.SlaCredits => "sla-credits",
        NegotiationPointTopic.TermVolume => "term-volume",
        NegotiationPointTopic.PaymentTerms => "payment-terms",
        _ => throw new ArgumentOutOfRangeException(nameof(topic), topic, "Unknown NegotiationPointTopic."),
    };

    /// <summary>The human-readable label <c>RenewalNegotiationTodo.Topic</c> persists — set once at
    /// creation, never rewritten by a repeat upsert (that entity's own doc comment).</summary>
    public static string ToDisplayLabel(this NegotiationPointTopic topic) => topic switch
    {
        NegotiationPointTopic.AboveBandPrice => "Above-band unit price",
        NegotiationPointTopic.UncappedOrHighLiability => "Uncapped or high liability",
        NegotiationPointTopic.AutoRenewShortNotice => "Auto-renews with short notice",
        NegotiationPointTopic.SlaCredits => "Missing or weak SLA / credits",
        NegotiationPointTopic.TermVolume => "Term / volume commitment",
        NegotiationPointTopic.PaymentTerms => "Payment terms",
        _ => throw new ArgumentOutOfRangeException(nameof(topic), topic, "Unknown NegotiationPointTopic."),
    };
}

/// <summary>
/// How compelling <see cref="NegotiationPointRanker"/>'s own grounding evidence is for one point —
/// informational only. Never persisted: <c>Raffa.Renewals.Application.RenewalNegotiationTodoPoint</c>'s
/// own doc comment records that "strength is not part of this module's persisted field set and is
/// dropped by whichever composition maps the ranker's output" onto it — this lets the chat top-3
/// narration say which point is strongest without the model inventing a judgement the calculators
/// never made.
/// </summary>
public enum NegotiationPointStrength
{
    Moderate,
    Strong,
}

/// <summary>
/// One ranked, grounded negotiation point (task E31/F02/US01/T01's own field list, verbatim:
/// topic/rank/current/target/whyItMatters/citationKeys/strength; parent story us-01-point-ranker
/// AC-1). Never constructed for an ungrounded topic — see <see cref="NegotiationPointRanker"/>'s own
/// doc comment.
/// </summary>
/// <param name="Topic">Which of the six canonical categories.</param>
/// <param name="Rank">1-based priority among this contract's <em>grounded</em> points only —
/// <see cref="NegotiationPointTopic"/>'s own declaration order with every ungrounded topic skipped,
/// never a gap (AC-2).</param>
/// <param name="Current">What the contract has today for this point, in plain language grounded in
/// <see cref="CitationKeys"/> — never a filler sentence ("no data captured yet").</param>
/// <param name="Target">What Procurement should ask for.</param>
/// <param name="WhyItMatters">Why the target is worth pursuing / achievable.</param>
/// <param name="CitationKeys"><see cref="InsightsCitationKeys"/>' <c>fact:</c>/<c>calc:</c> shapes
/// pointing at the stored fact(s)/clause(s)/benchmark band this point is grounded in — never empty
/// (a point with no citation is, by definition, not grounded).</param>
/// <param name="Strength">See <see cref="NegotiationPointStrength"/>.</param>
public sealed record NegotiationPoint(
    NegotiationPointTopic Topic,
    int Rank,
    string Current,
    string Target,
    string WhyItMatters,
    IReadOnlyList<string> CitationKeys,
    NegotiationPointStrength Strength);

/// <summary>
/// One extracted clause, snapshotted for <see cref="NegotiationPointRanker"/> — <c>Raffa.Insights</c>
/// cannot reference <c>Raffa.Documents.Contracts.Application.Contract360Clause</c> (allow-list
/// <c>[SharedKernel, Benchmark]</c>, ADR-002), so the host (<c>Raffa.Api.AskCopilotService</c>) maps
/// the handful of fields grounding actually needs — the same "own small shape, host maps into it"
/// pattern <see cref="ContractCriticalityInputs"/> already establishes for risk severity.
/// <see cref="ClauseType"/> is open-vocabulary, extraction-sourced free text (e.g. "limitation of
/// liability", "SLA") — grounding matches on it by keyword, honestly: a clause the extraction
/// pipeline never labelled with a matching keyword is not evidence of anything, and is never
/// silently treated as "confirmed absent" either (Appendix C rule 10).
/// </summary>
public sealed record NegotiationClauseSnapshot(string ClauseType, string RawText);

/// <summary>
/// One extracted, assessed risk, snapshotted for <see cref="NegotiationPointRanker"/> — same
/// reasoning as <see cref="NegotiationClauseSnapshot"/>. <see cref="Severity"/> reuses
/// <see cref="CriticalityRiskSeverity"/> rather than a fourth near-identical severity enum in this
/// module (<c>Raffa.Api.InsightsEndpointExtensions.ToCriticalityRiskSeverity</c> already maps
/// Documents/Contracts' own <c>RiskSeverity</c> onto it for the identical reason).
/// </summary>
public sealed record NegotiationRiskSnapshot(string RiskType, string Description, CriticalityRiskSeverity Severity);

/// <summary>
/// One contract's raw facts for <see cref="NegotiationPointRanker.Rank"/> (task E31/F02/US01/T01;
/// parent story us-01-point-ranker AC-1/AC-2). Same "composition-root-only-can-build-this" shape as
/// <see cref="StrategyInputs"/>/<see cref="ContractCriticalityInputs"/> — only
/// <c>Raffa.Api.AskCopilotService</c> can map clause/risk/commercial snapshots from a
/// <c>Contract360Result</c> into this record (ADR-002; Insights stays fenced to
/// <c>[SharedKernel, Benchmark]</c>).
/// </summary>
/// <param name="ContractId">Which contract this ranks — also this ranker's own citation-key root.</param>
/// <param name="PricedLines">Every priced line on this contract, already benchmarked where a band
/// resolved — the same async <c>InsightsEndpointExtensions.ToPricedLines</c> overload
/// <c>AskCopilotService.BuildRenewalStrategyPackAsync</c>/<c>BuildMarketComparePackAsync</c> use
/// (ADR-024 w17 clause 7, "one resolution per screen") — grounds
/// <see cref="NegotiationPointTopic.AboveBandPrice"/> and <see cref="NegotiationPointTopic.TermVolume"/>.</param>
/// <param name="AutoRenewal">Grounds <see cref="NegotiationPointTopic.AutoRenewShortNotice"/> together
/// with <paramref name="RenewalDate"/>/<paramref name="CancellationDeadline"/>.</param>
/// <param name="RenewalDate">Echoes <c>RenewalCalculationResult.RenewalDate</c>.</param>
/// <param name="CancellationDeadline">Echoes <c>RenewalCalculationResult.CancellationDeadline</c>
/// (falling back to the extracted contract fact when the engine has none — the same two-source order
/// <c>InsightsEndpointExtensions.ToStrategyInputs</c> already uses).</param>
/// <param name="Clauses">This contract's extracted clauses — grounds
/// <see cref="NegotiationPointTopic.UncappedOrHighLiability"/>/<see cref="NegotiationPointTopic.SlaCredits"/>
/// when no matching <paramref name="Risks"/> entry exists.</param>
/// <param name="Risks">This contract's extracted, assessed risks — the <em>preferred</em> grounding
/// source for <see cref="NegotiationPointTopic.UncappedOrHighLiability"/>/
/// <see cref="NegotiationPointTopic.SlaCredits"/> (a risk is already a curated, negotiation-relevant
/// finding; a clause is raw material that may or may not be one).</param>
/// <param name="PaymentTerms">The extracted payment-terms free text (e.g. "Net 30"), from
/// <c>Contract360Overview.PaymentTerms</c> — grounds <see cref="NegotiationPointTopic.PaymentTerms"/>
/// only when non-blank; <see langword="null"/>/blank is a data gap, never narrated as a fact
/// (Appendix C rule 10).</param>
public sealed record NegotiationPointInputs(
    EntityId ContractId,
    IReadOnlyList<PricedLine> PricedLines,
    bool AutoRenewal,
    DateOnly? RenewalDate,
    DateOnly? CancellationDeadline,
    IReadOnlyList<NegotiationClauseSnapshot> Clauses,
    IReadOnlyList<NegotiationRiskSnapshot> Risks,
    string? PaymentTerms);

/// <summary>
/// Ranks one contract's negotiation points (task E31/F02/US01/T01, point-ranker; NW-96; ADR-024 w19
/// cl. 23; parent story us-01-point-ranker AC-1/AC-2). Pure and synchronous: no database/HTTP/LLM
/// call anywhere in <see cref="Rank"/> (Appendix C rule 6) — the same <see cref="NegotiationPointInputs"/>
/// always produces the same ranked list.
///
/// <para>
/// <b>Grounded-only, by construction</b> (AC-1; epic-31's own "Out of scope: no ungrounded '7 lever'
/// dump"): each of the six <see cref="NegotiationPointTopic"/> categories has its own
/// <c>TryGround*</c> method below that returns <see langword="null"/> the moment its one required
/// piece of evidence (a stored fact, a clause, a risk, or a benchmark band — never a peer chunk: this
/// is a pure calculator with no retrieval access, unlike <c>Raffa.Api.AskCopilotService</c>'s own RAG
/// packs) is absent. <see cref="Rank"/> then keeps only the non-null results, in the fixed
/// <see cref="NegotiationPointTopic"/> declaration order, and numbers them 1..N with no gaps (AC-2) —
/// a contract that grounds nothing returns an empty list, never a fallback generic dump. That is what
/// makes "no utilization data yet"-style filler impossible to emit here: unlike
/// <c>Raffa.Insights.Negotiation.PricedLineNegotiationCalculator</c>'s own seven-lever playbook
/// (which always returns all seven, generic phrasing included, by design for that older, different
/// contract), this ranker has no lever-shaped fallback path to fall into at all.
/// </para>
///
/// <para>
/// <b>Absence of evidence is never evidence of absence</b> (Appendix C rule 10, applied per topic):
/// an empty <see cref="NegotiationPointInputs.Risks"/>/<see cref="NegotiationPointInputs.Clauses"/>
/// list never grounds a "no SLA was found" point — that would assert a fact the extraction pipeline
/// never actually determined. Only an <em>affirmative</em> match (a risk or clause the pipeline did
/// extract, naming the topic) grounds a point; a topic with zero matching evidence is skipped exactly
/// like a topic this input shape has no source for at all.
/// </para>
/// </summary>
public static class NegotiationPointRanker
{
    /// <summary>"Above-band" V1 planning threshold: more than this percentage above the matched P75
    /// counts as <see cref="NegotiationPointStrength.Strong"/> evidence, not an ADR/spec pin — same
    /// status as <c>Raffa.Insights.Negotiation.PricedLineNegotiationCalculator.QuarterEndProximityDays</c>.</summary>
    private const decimal AboveBandStrongMarginPercent = 10m;

    /// <summary>A notice window at or under this many days is grounded as "short" — V1 planning
    /// constant: no <c>cancellationNoticeDays</c> column exists yet to read a contractual figure from
    /// (ADR-024 w19 NW-92, "no cancellationNoticeDays column this wave"), so this ranker derives the
    /// window from <see cref="NegotiationPointInputs.RenewalDate"/> minus
    /// <see cref="NegotiationPointInputs.CancellationDeadline"/> instead.</summary>
    private const int ShortNoticeThresholdDays = 60;

    /// <summary>At or under this many days, a short notice window is
    /// <see cref="NegotiationPointStrength.Strong"/> rather than <see cref="NegotiationPointStrength.Moderate"/>.</summary>
    private const int ShortNoticeStrongThresholdDays = 30;

    /// <summary>A recorded term at or above this many months is
    /// <see cref="NegotiationPointStrength.Strong"/> term/volume evidence.</summary>
    private const int LongTermStrongThresholdMonths = 24;

    /// <summary>Excerpt length for a quoted clause's <see cref="NegotiationClauseSnapshot.RawText"/>
    /// in a point's <see cref="NegotiationPoint.Current"/> sentence — long enough to be meaningful,
    /// short enough that a whole extracted clause never floods the chat top-3.</summary>
    private const int ClauseExcerptMaxLength = 160;

    private static readonly string[] LiabilityKeywords = ["liability", "liable", "indemnif"];
    private static readonly string[] SlaCreditsKeywords = ["sla", "service level", "credit"];

    /// <summary>
    /// Ranks <paramref name="inputs"/>'s grounded negotiation points — see this type's own doc
    /// comment. Every branch is covered by <c>Raffa.Insights.Tests.NegotiationPointRankerTests</c>.
    /// </summary>
    public static IReadOnlyList<NegotiationPoint> Rank(NegotiationPointInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        NegotiationPoint?[] candidates =
        [
            TryGroundAboveBandPrice(inputs),
            TryGroundLiability(inputs),
            TryGroundAutoRenewShortNotice(inputs),
            TryGroundSlaCredits(inputs),
            TryGroundTermVolume(inputs),
            TryGroundPaymentTerms(inputs),
        ];

        var ranked = new List<NegotiationPoint>(candidates.Length);
        foreach (var candidate in candidates)
        {
            if (candidate is { } point)
            {
                ranked.Add(point with { Rank = ranked.Count + 1 });
            }
        }

        return ranked;
    }

    // ----- Topic 1: above-band price -----

    /// <summary>Grounded iff at least one priced line carries both a unit price and a benchmark band
    /// whose current price sits strictly above the matched P75 — "above-band" (top quartile) is this
    /// ranker's own definition, distinct from <c>PricedLineNegotiationCalculator</c>'s P25/P50
    /// opening-target arithmetic (a different, older calculator answering a different question:
    /// "what should the next price be", not "is today's price already above the market").</summary>
    private static NegotiationPoint? TryGroundAboveBandPrice(NegotiationPointInputs inputs)
    {
        var worstIndex = -1;
        var worstOveragePercent = 0m;
        var aboveBandCount = 0;

        for (var i = 0; i < inputs.PricedLines.Count; i++)
        {
            var line = inputs.PricedLines[i];

            if (line.UnitPrice is not { } unitPrice)
            {
                continue;
            }

            if (line.Benchmark is not { } band)
            {
                continue;
            }

            if (band.P75 <= 0m || unitPrice <= band.P75)
            {
                continue;
            }

            aboveBandCount++;
            var overagePercent = (unitPrice - band.P75) / band.P75 * 100m;
            if (worstIndex == -1 || overagePercent > worstOveragePercent)
            {
                worstIndex = i;
                worstOveragePercent = overagePercent;
            }
        }

        if (worstIndex == -1)
        {
            return null;
        }

        var worstLine = inputs.PricedLines[worstIndex];
        var matchedBand = worstLine.Benchmark!;
        var unitPriceValue = worstLine.UnitPrice!.Value;
        var currency = worstLine.Currency ?? "n/a";

        var suffix = aboveBandCount > 1
            ? $" — the worst of {aboveBandCount.ToString(CultureInfo.InvariantCulture)} priced lines above band on this contract."
            : ".";

        var current = $"{worstLine.Description} is priced at {Fmt(unitPriceValue)} {currency}/unit, " +
            $"{Fmt(worstOveragePercent)}% above the market P75 ({Fmt(matchedBand.P75)}){suffix}";

        var target = $"Bring the unit price down to at least the P50 benchmark " +
            $"({Fmt(matchedBand.P50)} {currency}) — the matched market midpoint.";

        const string whyItMatters =
            "Paying above the top quartile of comparable deals is the single most defensible opening " +
            "ask in a renewal: it cites the market directly, not an internal target.";

        var strength = worstOveragePercent >= AboveBandStrongMarginPercent
            ? NegotiationPointStrength.Strong
            : NegotiationPointStrength.Moderate;

        return new NegotiationPoint(
            NegotiationPointTopic.AboveBandPrice,
            0,
            current,
            target,
            whyItMatters,
            [
                InsightsCitationKeys.Fact(inputs.ContractId, $"priced-line[{worstIndex}].unitPrice"),
                InsightsCitationKeys.Calc($"priced-line[{worstIndex}].above-band"),
            ],
            strength);
    }

    // ----- Topic 2: uncapped / high liability -----

    private static NegotiationPoint? TryGroundLiability(NegotiationPointInputs inputs)
    {
        const string target =
            "Cap total liability at a fixed multiple of annual fees (e.g. 12 months' charges), " +
            "carving out only gross negligence, wilful misconduct, IP infringement and confidentiality breach.";

        var risk = FindBestRisk(inputs.Risks, LiabilityKeywords);
        if (risk is { } r)
        {
            var strong = r.Entry.Severity is CriticalityRiskSeverity.High or CriticalityRiskSeverity.Critical
                || ContainsAny(r.Entry.RiskType, "uncapped", "unlimited")
                || ContainsAny(r.Entry.Description, "uncapped", "unlimited");

            return new NegotiationPoint(
                NegotiationPointTopic.UncappedOrHighLiability,
                0,
                $"{r.Entry.RiskType}: {r.Entry.Description}",
                target,
                "Uncapped or high liability exposes the business to damages far beyond the value of " +
                "the contract — a market-standard cap is rarely refused outright.",
                [InsightsCitationKeys.Fact(inputs.ContractId, $"risk[{r.Index}]")],
                strong ? NegotiationPointStrength.Strong : NegotiationPointStrength.Moderate);
        }

        var clause = FindClause(inputs.Clauses, LiabilityKeywords);
        if (clause is { } c)
        {
            return new NegotiationPoint(
                NegotiationPointTopic.UncappedOrHighLiability,
                0,
                $"{c.Entry.ClauseType} clause on file: \"{Truncate(c.Entry.RawText)}\"",
                target,
                "A liability clause with no confirmed cap is real financial exposure — review and " +
                "negotiate a cap before renewal.",
                [InsightsCitationKeys.Fact(inputs.ContractId, $"clause[{c.Index}]")],
                NegotiationPointStrength.Moderate);
        }

        return null;
    }

    // ----- Topic 3: auto-renew + short notice -----

    /// <summary>Grounded iff the contract auto-renews and both dates are known, deriving the notice
    /// window as <see cref="NegotiationPointInputs.RenewalDate"/> minus
    /// <see cref="NegotiationPointInputs.CancellationDeadline"/> — see
    /// <see cref="ShortNoticeThresholdDays"/>'s own doc comment for why this is derived rather than
    /// read from a stored notice-days figure.</summary>
    private static NegotiationPoint? TryGroundAutoRenewShortNotice(NegotiationPointInputs inputs)
    {
        if (!inputs.AutoRenewal)
        {
            return null;
        }

        if (inputs.RenewalDate is not { } renewalDate)
        {
            return null;
        }

        if (inputs.CancellationDeadline is not { } cancellationDeadline)
        {
            return null;
        }

        var noticeDays = renewalDate.DayNumber - cancellationDeadline.DayNumber;
        if (noticeDays <= 0 || noticeDays > ShortNoticeThresholdDays)
        {
            return null;
        }

        var strength = noticeDays <= ShortNoticeStrongThresholdDays
            ? NegotiationPointStrength.Strong
            : NegotiationPointStrength.Moderate;

        return new NegotiationPoint(
            NegotiationPointTopic.AutoRenewShortNotice,
            0,
            $"Auto-renews on {renewalDate:yyyy-MM-dd} unless notice is given by " +
            $"{cancellationDeadline:yyyy-MM-dd} — a {noticeDays.ToString(CultureInfo.InvariantCulture)}-day notice window.",
            "Negotiate a longer notice window (90+ days) or convert to opt-in renewal so the contract " +
            "never locks in by default.",
            "A short notice window is easy to miss and re-commits the business to a full term " +
            "automatically — extending it costs the supplier nothing and removes a real risk.",
            [
                InsightsCitationKeys.Fact(inputs.ContractId, "renewalDate"),
                InsightsCitationKeys.Fact(inputs.ContractId, "cancellationDeadline"),
            ],
            strength);
    }

    // ----- Topic 4: SLA / credits -----

    private static NegotiationPoint? TryGroundSlaCredits(NegotiationPointInputs inputs)
    {
        const string target =
            "Add measurable SLA commitments with meaningful service credits (e.g. 5-10% of monthly " +
            "fees per breach tier, escalating with repeated misses).";

        var risk = FindBestRisk(inputs.Risks, SlaCreditsKeywords);
        if (risk is { } r)
        {
            var strong = r.Entry.Severity is CriticalityRiskSeverity.High or CriticalityRiskSeverity.Critical;

            return new NegotiationPoint(
                NegotiationPointTopic.SlaCredits,
                0,
                $"{r.Entry.RiskType}: {r.Entry.Description}",
                target,
                "Without an enforceable SLA and real credits, there is no financial consequence for " +
                "the supplier under-delivering.",
                [InsightsCitationKeys.Fact(inputs.ContractId, $"risk[{r.Index}]")],
                strong ? NegotiationPointStrength.Strong : NegotiationPointStrength.Moderate);
        }

        var clause = FindClause(inputs.Clauses, SlaCreditsKeywords);
        if (clause is { } c)
        {
            return new NegotiationPoint(
                NegotiationPointTopic.SlaCredits,
                0,
                $"{c.Entry.ClauseType} clause on file: \"{Truncate(c.Entry.RawText)}\"",
                "Tighten service levels and increase credit percentages, with escalating penalties " +
                "for repeated breaches.",
                "An existing SLA/credits clause is the easiest starting point to improve — the " +
                "principle is already conceded, only the numbers are open.",
                [InsightsCitationKeys.Fact(inputs.ContractId, $"clause[{c.Index}]")],
                NegotiationPointStrength.Moderate);
        }

        return null;
    }

    // ----- Topic 5: term / volume -----

    private static NegotiationPoint? TryGroundTermVolume(NegotiationPointInputs inputs)
    {
        (int Index, PricedLine Line, int Months)? bestTerm = null;
        (int Index, PricedLine Line, decimal Quantity)? bestVolume = null;

        for (var i = 0; i < inputs.PricedLines.Count; i++)
        {
            var line = inputs.PricedLines[i];

            if (line.TermMonths is { } months && months > 0)
            {
                if (bestTerm is null || months > bestTerm.Value.Months)
                {
                    bestTerm = (i, line, months);
                }
            }

            if (line.Quantity is { } quantity && quantity > 0m)
            {
                if (bestVolume is null || quantity > bestVolume.Value.Quantity)
                {
                    bestVolume = (i, line, quantity);
                }
            }
        }

        if (bestTerm is null && bestVolume is null)
        {
            return null;
        }

        var parts = new List<string>();
        var citationKeys = new List<string>();

        if (bestTerm is { } term)
        {
            parts.Add($"{term.Line.Description} commits to a " +
                $"{term.Months.ToString(CultureInfo.InvariantCulture)}-month term");
            citationKeys.Add(InsightsCitationKeys.Fact(inputs.ContractId, $"priced-line[{term.Index}].termMonths"));
        }

        if (bestVolume is { } volume)
        {
            parts.Add($"{volume.Line.Description} orders {Fmt(volume.Quantity)} unit(s)");
            citationKeys.Add(InsightsCitationKeys.Fact(inputs.ContractId, $"priced-line[{volume.Index}].quantity"));
        }

        var strength = bestTerm is { } strongCheck && strongCheck.Months >= LongTermStrongThresholdMonths
            ? NegotiationPointStrength.Strong
            : NegotiationPointStrength.Moderate;

        return new NegotiationPoint(
            NegotiationPointTopic.TermVolume,
            0,
            string.Join("; ", parts) + ".",
            "Trade a renewed or longer term, or a larger consolidated volume commitment, for a lower " +
            "unit rate or added value (extra seats, a higher support tier).",
            "Term length and order volume are the supplier's own two biggest planning inputs — " +
            "committing to either is real, low-cost leverage for a price concession.",
            citationKeys,
            strength);
    }

    // ----- Topic 6: payment terms -----

    private static NegotiationPoint? TryGroundPaymentTerms(NegotiationPointInputs inputs)
    {
        if (string.IsNullOrWhiteSpace(inputs.PaymentTerms))
        {
            return null;
        }

        return new NegotiationPoint(
            NegotiationPointTopic.PaymentTerms,
            0,
            $"Current payment terms: {inputs.PaymentTerms}.",
            "Offer faster payment (e.g. move to Net 15) in exchange for a price concession, or extend " +
            "terms if cash-flow headroom matters more than a discount.",
            "Payment timing is a real lever even when the unit price itself will not move — suppliers " +
            "routinely trade a small discount for faster, more predictable cash flow.",
            [InsightsCitationKeys.Fact(inputs.ContractId, "paymentTerms")],
            // Always Moderate: payment terms is the lowest-priority canonical topic (NW-96's own
            // ordering) by the council's own ranking, never the strongest point on a contract.
            NegotiationPointStrength.Moderate);
    }

    // ----- Shared lookups -----

    private static (int Index, NegotiationRiskSnapshot Entry)? FindBestRisk(
        IReadOnlyList<NegotiationRiskSnapshot> risks, string[] keywords)
    {
        (int Index, NegotiationRiskSnapshot Entry)? best = null;

        for (var i = 0; i < risks.Count; i++)
        {
            var risk = risks[i];
            if (!ContainsAny(risk.RiskType, keywords) && !ContainsAny(risk.Description, keywords))
            {
                continue;
            }

            if (best is null || SeverityRank(risk.Severity) > SeverityRank(best.Value.Entry.Severity))
            {
                best = (i, risk);
            }
        }

        return best;
    }

    private static (int Index, NegotiationClauseSnapshot Entry)? FindClause(
        IReadOnlyList<NegotiationClauseSnapshot> clauses, string[] keywords)
    {
        for (var i = 0; i < clauses.Count; i++)
        {
            if (ContainsAny(clauses[i].ClauseType, keywords))
            {
                return (i, clauses[i]);
            }
        }

        return null;
    }

    private static int SeverityRank(CriticalityRiskSeverity severity) => severity switch
    {
        CriticalityRiskSeverity.None => 0,
        CriticalityRiskSeverity.Low => 1,
        CriticalityRiskSeverity.Medium => 2,
        CriticalityRiskSeverity.High => 3,
        CriticalityRiskSeverity.Critical => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unknown CriticalityRiskSeverity."),
    };

    private static bool ContainsAny(string? text, params string[] keywords)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var keyword in keywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string Truncate(string text) =>
        text.Length <= ClauseExcerptMaxLength ? text : text[..ClauseExcerptMaxLength] + "…";

    /// <summary>Culture-invariant, unpadded decimal formatting for explanation strings — same
    /// convention every other calculator in this codebase already establishes (see
    /// <c>Raffa.Insights.Negotiation.PricedLineNegotiationCalculator.Fmt</c>).</summary>
    private static string Fmt(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
