using System.Globalization;
using Raffa.Insights.Contracts;
using Raffa.Insights.Criticality;
using Raffa.Insights.Negotiation;

namespace Raffa.Insights.Strategy;

/// <summary>
/// Builds the renewal-strategy pack for one contract (task E13/F07/US01/T01, insights-calculators;
/// parent story us-01-insights AC-4; R-STR-01). Pure and synchronous — no database/HTTP/LLM call
/// anywhere in <see cref="Build"/> (Appendix C rule 6): the same <see cref="StrategyInputs"/> always
/// produces the same <see cref="StrategyPack"/>. Composes <see cref="PricedLineNegotiationCalculator"/>
/// (per priced line) plus its own dates/next-steps/open-weak-facts logic — the model (a later Ask
/// task) only ever narrates this pack, it never computes a number itself (R-STR-01's own "The model
/// narrates; numbers are the calculators'", ADR-024).
/// </summary>
public static class StrategyPackBuilder
{
    /// <summary>The four tracker steps, verbatim from <c>raffa-v2/app.jsx</c>'s own
    /// <c>stepDefs</c> (mirroring the Contract 360 tracker). <c>Label</c> and <c>DueHint</c> are each
    /// formatted independently (one <see cref="string.Format(IFormatProvider, string, object)"/>
    /// call per field in <see cref="BuildNextSteps"/> below), so <c>{0}</c> in <c>Label</c> means
    /// <see cref="StrategyInputs.SupplierName"/> (or a generic fallback) while <c>{0}</c> in
    /// <c>DueHint</c> separately means the cancellation-deadline due hint — never a shared argument
    /// index across the two fields.</summary>
    private static readonly (string Label, string DueHint)[] NextStepTemplates =
    [
        ("Notify {0} of intent to renegotiate", "this week"),
        ("Request revised pricing and licence mix", "+10 days"),
        ("Counter with the market benchmark", "+20 days"),
        ("Sign, or send non-renewal notice", "{0}"),
    ];

    /// <summary>
    /// Builds <paramref name="inputs"/>'s full strategy pack — sections in the council-decided order
    /// (<see cref="StrategyPack"/>'s own doc comment). Every branch is covered by
    /// <c>Raffa.Insights.Tests.StrategyPackBuilderTests</c>.
    /// </summary>
    public static StrategyPack Build(StrategyInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var whenYouMustMove = BuildWhenYouMustMove(inputs);
        var (whereYouCanPush, targets) = BuildLeversAndTargets(inputs);
        var nextSteps = BuildNextSteps(inputs);
        var openWeakFacts = BuildOpenWeakFacts(inputs);

        return new StrategyPack(inputs.ContractId, whenYouMustMove, whereYouCanPush, targets, nextSteps, openWeakFacts);
    }

    /// <summary>
    /// "When you must move" (AC-4: "a passed deadline is stated as passed"). Prefers
    /// <see cref="StrategyInputs.DaysUntilCancellationDeadline"/> over
    /// <see cref="StrategyInputs.DaysUntilRenewal"/> — the date that actually forces a decision —
    /// the same preference <c>Raffa.Renewals.Application.RenewalPipelineBuilder
    /// .DetermineRecommendation</c> already establishes; falls back to the renewal date only when no
    /// cancellation deadline is known.
    /// </summary>
    private static WhenYouMustMove BuildWhenYouMustMove(StrategyInputs inputs)
    {
        var usingCancellationDeadline = inputs.DaysUntilCancellationDeadline is not null;
        var daysLeft = inputs.DaysUntilCancellationDeadline ?? inputs.DaysUntilRenewal;
        var label = usingCancellationDeadline ? "cancellation deadline" : "renewal date";

        string explanation;
        var passedDeadline = false;

        if (!inputs.AutoRenewal)
        {
            explanation = "This contract does not auto-renew: no renewal date or cancellation " +
                "deadline applies (a determined fact, not missing data — Appendix C rule 10).";
        }
        else if (daysLeft is not { } days)
        {
            explanation = "No renewal date or cancellation deadline could be determined for this " +
                "contract (Appendix C rule 10).";
        }
        else if (days < 0)
        {
            passedDeadline = true;
            explanation = $"The {label} passed {(-days).ToString(CultureInfo.InvariantCulture)} " +
                "day(s) ago — stated as passed, not hidden (AC-4).";
        }
        else
        {
            explanation = $"{days.ToString(CultureInfo.InvariantCulture)} day(s) until the {label}.";
        }

        return new WhenYouMustMove(
            inputs.RenewalDate, inputs.CancellationDeadline, daysLeft, passedDeadline, explanation);
    }

    /// <summary>
    /// "Where you can push" + "Targets" — one <see cref="PricedLineNegotiationCalculator.Compute"/>
    /// call per <see cref="StrategyInputs.PricedLines"/> entry, flattening every line's own levers
    /// into <see cref="StrategyPack.WhereYouCanPush"/> (prefixed by the line's own
    /// <c>Description</c> when the contract has more than one priced line, so a multi-line contract's
    /// levers stay attributable) and collecting each line's own numeric targets into
    /// <see cref="StrategyPack.Targets"/>.
    /// </summary>
    private static (IReadOnlyList<ContractNegotiationLever> Levers, IReadOnlyList<PricedLineTarget> Targets)
        BuildLeversAndTargets(StrategyInputs inputs)
    {
        var totalLines = inputs.PricedLines.Count;
        var levers = new List<ContractNegotiationLever>();
        var targets = new List<PricedLineTarget>();

        for (var i = 0; i < totalLines; i++)
        {
            var line = inputs.PricedLines[i];
            var result = PricedLineNegotiationCalculator.Compute(
                inputs.ContractId, line, i, totalLines, inputs.AsOfDate);

            var prefix = totalLines > 1 ? $"{line.Description}: " : string.Empty;
            foreach (var lever in result.Levers)
            {
                levers.Add(lever with { Rationale = prefix + lever.Rationale });
            }

            targets.Add(new PricedLineTarget(
                line.Description,
                result.OpeningTarget,
                result.AcceptableRangeLow,
                result.AcceptableRangeHigh,
                result.WalkAwayThreshold,
                result.Explanation));
        }

        return (levers, targets);
    }

    /// <summary>
    /// "Next steps" — <see cref="NextStepTemplates"/> filled in with
    /// <see cref="StrategyInputs.SupplierName"/> (falling back to "the supplier" when not resolved —
    /// Appendix C rule 10: never fabricate a name) and the cancellation-deadline due hint (an actual
    /// date when known, "the cancellation deadline" otherwise).
    /// </summary>
    private static IReadOnlyList<NextStep> BuildNextSteps(StrategyInputs inputs)
    {
        var supplier = string.IsNullOrWhiteSpace(inputs.SupplierName) ? "the supplier" : inputs.SupplierName;
        var cancellationHint = inputs.CancellationDeadline is { } deadline
            ? "by " + deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "by the cancellation deadline";

        return NextStepTemplates
            .Select(t => new NextStep(
                string.Format(CultureInfo.InvariantCulture, t.Label, supplier),
                string.Format(CultureInfo.InvariantCulture, t.DueHint, cancellationHint)))
            .ToList();
    }

    /// <summary>
    /// <see cref="StrategyPack.OpenWeakFacts"/> — the same weak-confidence threshold
    /// <see cref="CriticalityScoreCalculator.WeakFactConfidenceThreshold"/> uses, so a contract's
    /// weak facts read identically here and in its own criticality "open critical facts" component
    /// (the parent story's own "Ask, Contract 360 and Renewals show the same numbers").
    /// </summary>
    private static IReadOnlyList<string> BuildOpenWeakFacts(StrategyInputs inputs) =>
        inputs.CriticalFacts
            .Where(f => f.Confidence < CriticalityScoreCalculator.WeakFactConfidenceThreshold)
            .Select(f => InsightsCitationKeys.Fact(inputs.ContractId, f.FieldKey))
            .ToList();
}
