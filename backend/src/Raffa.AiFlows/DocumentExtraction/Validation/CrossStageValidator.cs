using System.Diagnostics.Metrics;
using Raffa.Documents.Contracts.Application.Extraction;

namespace Raffa.AiFlows.DocumentExtraction.Validation;

public sealed record CrossStageRuleReport(string RuleId, RuleEvaluation Evaluation);

/// <summary>The outcome of all rules for one run.</summary>
public sealed record CrossStageReport(IReadOnlyList<CrossStageRuleReport> Rules)
{
    public IReadOnlyList<CrossStageViolation> Violations =>
        Rules.SelectMany(r => r.Evaluation.Violations).ToList();
}

/// <summary>
/// F5-D19: runs the cross-stage rules over the parsed scalar stages. Pure and deterministic; a rule that throws
/// is reported as not applicable and never fails the run. In Gate mode the involved facts are returned as
/// copies carrying a review reason (the store turns it into <c>review_required</c>); values are never touched.
/// </summary>
public static class CrossStageValidator
{
    public const string MeterName = "Raffa.AiFlows.Extraction";

    /// <summary>Prefix of the review reason a Gate-flagged fact carries.</summary>
    public const string ReasonPrefix = "cross-stage:";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> Checks = Meter.CreateCounter<long>(
        "raffa.extraction.cross_stage_checks",
        description: "Cross-stage consistency rule evaluations, by rule, outcome and mode.");

    private static readonly Counter<long> Violations = Meter.CreateCounter<long>(
        "raffa.extraction.cross_stage_violations",
        description: "Cross-stage consistency violations, by violation rule id and mode.");

    public static IReadOnlyList<ICrossStageRule> DefaultRules { get; } =
        [new DateOrderRule(), new DeadlineConsistencyRule(), new TcvVersusAnnualSpendRule()];

    public static CrossStageReport Evaluate(IEnumerable<StageFacts.Scalars> scalarStages, IEnumerable<ICrossStageRule>? rules = null)
    {
        var facts = new ScalarFactSet(scalarStages);
        var reports = new List<CrossStageRuleReport>();
        foreach (var rule in rules ?? DefaultRules)
        {
            RuleEvaluation evaluation;
            try
            {
                evaluation = rule.Evaluate(facts);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                evaluation = RuleEvaluation.NotApplicable;
            }

            reports.Add(new CrossStageRuleReport(rule.RuleId, evaluation));
        }

        return new CrossStageReport(reports);
    }

    public static void Record(CrossStageReport report, ValidationMode mode)
    {
        foreach (var rule in report.Rules)
        {
            Checks.Add(
                1,
                new KeyValuePair<string, object?>("rule", rule.RuleId),
                new KeyValuePair<string, object?>("outcome", rule.Evaluation.Status switch
                {
                    RuleStatus.Passed => "ok",
                    RuleStatus.Violated => "violation",
                    _ => "not_applicable",
                }),
                new KeyValuePair<string, object?>("mode", mode.ToString()));

            foreach (var violation in rule.Evaluation.Violations)
            {
                Violations.Add(
                    1,
                    new KeyValuePair<string, object?>("rule", violation.RuleId),
                    new KeyValuePair<string, object?>("mode", mode.ToString()));
            }
        }
    }

    /// <summary>Gate: copies of the scalar facts whose field a violation involves, flagged with the sorted,
    /// distinct violation ids as the named reason. Everything else, and every value, is untouched.</summary>
    public static IReadOnlyList<StageFacts.Scalars> ApplyGate(IEnumerable<StageFacts.Scalars> scalarStages, CrossStageReport report)
    {
        var reasons = report.Violations
            .SelectMany(v => v.Fields.Select(field => (Field: field, v.RuleId)))
            .GroupBy(x => x.Field, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => ReasonPrefix + string.Join(';', g.Select(x => x.RuleId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
                StringComparer.Ordinal);

        return scalarStages
            .Select(stage => reasons.Count == 0
                ? stage
                : new StageFacts.Scalars(
                    stage.Stage,
                    stage.Facts.Select(f => f.Field is not null && !string.IsNullOrWhiteSpace(f.Value) && reasons.TryGetValue(f.Field, out var reason)
                        ? f with { ReviewReason = reason }
                        : f).ToList()))
            .ToList();
    }
}
