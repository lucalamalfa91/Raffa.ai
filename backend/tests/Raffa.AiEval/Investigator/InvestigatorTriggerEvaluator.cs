using System.Globalization;
using System.Text;
using Raffa.AiFlows.Ask.Gate;
using Raffa.AiFlows.Ask.Planning;
using Raffa.AiFlows.Ask.Routing;
using Raffa.AiFlows.CapabilityGaps.Investigation;
using Raffa.Chat.Application.Planning;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Domain;

namespace Raffa.AiEval.Investigator;

/// <summary>What the offline evaluation observed for one phrase.</summary>
internal sealed record InvestigatorObservation(
    InvestigatorEvalCase Case,
    GateLabel Gate,
    TriggerVerdict Verdict,
    bool PlannerCoversLanguage)
{
    /// <summary>In production the investigator is only asked about an in-domain turn the fixed
    /// catalog did not claim. Informational here: the gate reads Italian and English, so for the
    /// other languages it routinely stops a phrase before the investigator (a capitalised noun
    /// reads as an unknown supplier); that is the gate's own multilingual work, not the trigger's.
    /// The metrics below measure the trigger on every phrase, as if the gate had let it through.</summary>
    public bool ReachesInvestigator => Gate == GateLabel.InDomain;
}

/// <summary>Counts and ratios of one trigger over a group of observations.</summary>
internal sealed record TriggerMetrics(string Name, int Observed, int TruePositives, int FalsePositives, int FalseNegatives, int Total)
{
    /// <summary>Of the phrases the trigger fired on, the share that really are gaps.</summary>
    public double? Precision => TruePositives + FalsePositives == 0 ? null : (double)TruePositives / (TruePositives + FalsePositives);

    /// <summary>Of the phrases that really are gaps, the share the trigger fired on.</summary>
    public double? Recall => TruePositives + FalseNegatives == 0 ? null : (double)TruePositives / (TruePositives + FalseNegatives);

    /// <summary>Share of phrases on which the trigger fired (the investigator calls it costs).</summary>
    public double FireRate => Total == 0 ? 0 : (double)Observed / Total;
}

/// <summary>
/// INV-04 / F4-D08: the offline evaluator of the investigator's deterministic trigger. It runs
/// <see cref="InvestigatorTrigger"/> (T3 from the lexicon, T1 from the real <see cref="IntentPlanner"/>,
/// T2 from the phrase's simulated reply outcome) over the labelled set and reports precision and
/// recall — per trigger, per language and overall — against the post-release targets of plan 5.4
/// (recall of the real gaps 90, precision of the starts 60, reduction of <c>gaps-v1</c> calls 70
/// percent). The targets are monitored with an alarm, never a gate: the test suite asserts that the
/// set is well formed and that T3 and T2 behave exactly as labelled; the metrics are reported.
/// No model, no network, no database: it cannot call the AI gateway because it never holds one.
/// </summary>
internal static class InvestigatorTriggerEvaluator
{
    public const double RecallTarget = 0.90;
    public const double PrecisionTarget = 0.60;
    public const double ReductionTarget = 0.70;

    /// <summary>The languages <see cref="IntentPlanner"/> has lexicons for; for the others trigger
    /// T1 fires on nearly every phrase and is reported separately.</summary>
    private static readonly HashSet<string> PlannerLanguages = new(StringComparer.Ordinal) { "it", "en" };

    /// <summary>The suppliers the phrases name, as a tenant with contracts for them would have them.</summary>
    private static readonly IReadOnlyCollection<KnownSupplierName> KnownSuppliers =
        new[] { "Amazon", "Salesforce", "Oracle", "Zeta" }.Select(name => new KnownSupplierName(name, name.ToLowerInvariant())).ToList();

    public static IReadOnlyList<InvestigatorObservation> Evaluate(
        IReadOnlyList<InvestigatorEvalCase> cases, InvestigatorTrigger? trigger = null)
    {
        trigger ??= InvestigatorTrigger.Default;
        var gate = new DomainGate();
        var planner = new IntentPlanner();

        var observations = new List<InvestigatorObservation>(cases.Count);
        foreach (var evalCase in cases)
        {
            var classified = gate.Classify(evalCase.Text, KnownSuppliers);
            var label = classified.Label;
            var plan = planner.Plan(evalCase.Text, classified.NamedSupplier);
            var outcome = new ReplyOutcome(
                string.Equals(evalCase.Outcome, "abstain", StringComparison.Ordinal) ? ReplyKind.Abstain : ReplyKind.Answer,
                GuardIntervened: false,
                FallbackUsed: false);

            observations.Add(new InvestigatorObservation(
                evalCase,
                label,
                trigger.Evaluate(plan, outcome, evalCase.Text),
                PlannerLanguages.Contains(evalCase.Language)));
        }

        return observations;
    }

    public static TriggerMetrics Measure(string name, IEnumerable<InvestigatorObservation> observations, Func<TriggerVerdict, bool> fired)
    {
        var rows = observations.ToList();
        var tp = rows.Count(o => fired(o.Verdict) && o.Case.GapWorthy);
        var fp = rows.Count(o => fired(o.Verdict) && !o.Case.GapWorthy);
        var fn = rows.Count(o => !fired(o.Verdict) && o.Case.GapWorthy);
        return new TriggerMetrics(name, tp + fp, tp, fp, fn, rows.Count);
    }

    public static IReadOnlyList<TriggerMetrics> MeasureAll(IEnumerable<InvestigatorObservation> observations)
    {
        var rows = observations.ToList();
        return Triggers.Select(trigger => Measure(trigger.Name, rows, trigger.Fired)).ToList();
    }

    /// <summary>Renders the markdown report: set shape, metrics overall / per language / per
    /// trigger, the targets with their alarms, and every phrase where the observed T3 or T2
    /// differs from its label.</summary>
    public static string RenderReport(IReadOnlyList<InvestigatorObservation> observations)
    {
        var report = new StringBuilder();
        var invariant = CultureInfo.InvariantCulture;

        string Percent(double? value) => value is null ? "n/a" : (value.Value * 100).ToString("0.0", invariant) + "%";

        report.AppendLine("# Investigator trigger — offline evaluation (INV-04)");
        report.AppendLine();
        report.AppendLine($"- Evaluation set: `{InvestigatorEvalSet.FileName}` v{InvestigatorEvalSet.Version}, {observations.Count} phrases");
        report.AppendLine($"- Trigger lexicon: v{InvestigatorTriggerLexicon.Default.Version}");
        report.AppendLine("- Mode: offline, deterministic — no model call, no network (T3 lexicon, T1 from the real IntentPlanner, T2 from a simulated reply outcome)");
        report.AppendLine("- Targets (plan 5.4, monitored with an alarm, not a gate): recall of real gaps >= 90%, precision of starts >= 60%, reduction of gaps-v1 calls >= 70% versus Always");
        report.AppendLine();

        report.AppendLine("## Gate (informational)");
        report.AppendLine();
        report.AppendLine(
            "The metrics below measure the trigger on every phrase, as if the gate had let it through. In production the investigator " +
            "is only asked about an in-domain turn the fixed catalog did not claim; the gate reads Italian and English, so for the other " +
            "languages it stops many phrases before the investigator (a capitalised noun reads as an unknown supplier).");
        report.AppendLine();
        report.AppendLine("| Language | Phrases the gate lets through | Other gate labels |");
        report.AppendLine("|---|---:|---|");
        foreach (var language in InvestigatorEvalSet.Languages)
        {
            var rows = observations.Where(o => o.Case.Language == language).ToList();
            var others = rows
                .Where(o => !o.ReachesInvestigator)
                .GroupBy(o => o.Gate)
                .OrderBy(g => g.Key.ToString(), StringComparer.Ordinal)
                .Select(g => $"{g.Key} {g.Count()}");
            report.AppendLine($"| {language} | {rows.Count(o => o.ReachesInvestigator)} of {rows.Count} | {string.Join(", ", others)} |");
        }

        report.AppendLine();

        void Table(string title, IEnumerable<InvestigatorObservation> rows)
        {
            var materialised = rows.ToList();
            report.AppendLine($"## {title}");
            report.AppendLine();
            report.AppendLine("| Trigger | Fired | TP | FP | FN | Precision | Recall | Fire rate |");
            report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var metrics in MeasureAll(materialised))
            {
                var name = metrics.Name;
                report.AppendLine(
                    $"| {name} | {metrics.Observed} | {metrics.TruePositives} | {metrics.FalsePositives} | {metrics.FalseNegatives} | " +
                    $"{Percent(metrics.Precision)} | {Percent(metrics.Recall)} | {Percent(metrics.FireRate)} |");
            }

            report.AppendLine();
        }

        Table("Overall", observations);

        foreach (var language in InvestigatorEvalSet.Languages)
        {
            Table(
                $"Language: {language}" + (PlannerLanguages.Contains(language) ? string.Empty : " (the planner has no lexicon for this language: T1 fires on nearly every phrase)"),
                observations.Where(o => o.Case.Language == language));
        }

        var overall = Measure("Triggered", observations, v => v.ShouldRun);
        var reduction = 1 - overall.FireRate;
        report.AppendLine("## Targets");
        report.AppendLine();
        report.AppendLine($"- Recall of real gaps (T1+T2+T3): {Percent(overall.Recall)} — target {Percent(RecallTarget)} — {(overall.Recall >= RecallTarget ? "met" : "ALARM")}");
        report.AppendLine($"- Precision of starts: {Percent(overall.Precision)} — target {Percent(PrecisionTarget)} — {(overall.Precision >= PrecisionTarget ? "met" : "ALARM")}");
        report.AppendLine($"- Reduction of gaps-v1 calls versus Always: {Percent(reduction)} — target {Percent(ReductionTarget)} — {(reduction >= ReductionTarget ? "met" : "ALARM")}");

        var withoutPlanner = Measure("T2+T3", observations, v => v.T2 || v.T3);
        report.AppendLine(
            $"- Without the planner trigger (T2+T3 only): recall {Percent(withoutPlanner.Recall)}, precision {Percent(withoutPlanner.Precision)}, " +
            $"reduction {Percent(1 - withoutPlanner.FireRate)}");
        report.AppendLine();

        var mismatches = observations
            .Where(o => o.Verdict.T3 != o.Case.Expect.T3 || o.Verdict.T2 != o.Case.Expect.T2)
            .ToList();
        report.AppendLine("## Phrases where the trigger differs from its label");
        report.AppendLine();
        if (mismatches.Count == 0)
        {
            report.AppendLine("None: every T3 and T2 verdict equals its label.");
        }
        else
        {
            foreach (var mismatch in mismatches)
            {
                report.AppendLine(
                    $"- `{mismatch.Case.Id}` expected T3={mismatch.Case.Expect.T3}/T2={mismatch.Case.Expect.T2}, observed T3={mismatch.Verdict.T3}/T2={mismatch.Verdict.T2}: {mismatch.Case.Text}");
            }
        }

        report.AppendLine();
        report.AppendLine("## Gap-worthy phrases the triggers miss (recall)");
        report.AppendLine();
        var misses = observations.Where(o => o.Case.GapWorthy && !o.Verdict.ShouldRun).ToList();
        if (misses.Count == 0)
        {
            report.AppendLine("None.");
        }
        else
        {
            foreach (var miss in misses)
            {
                report.AppendLine($"- `{miss.Case.Id}` ({miss.Case.Category}): {miss.Case.Text}");
            }
        }

        report.AppendLine();
        report.AppendLine("## Non-gap phrases the triggers start on (precision)");
        report.AppendLine();
        var falseStarts = observations.Where(o => !o.Case.GapWorthy && o.Verdict.ShouldRun).ToList();
        if (falseStarts.Count == 0)
        {
            report.AppendLine("None.");
        }
        else
        {
            foreach (var start in falseStarts)
            {
                report.AppendLine($"- `{start.Case.Id}` ({start.Verdict.Reason}): {start.Case.Text}");
            }
        }

        return report.ToString();
    }

    private static readonly (string Name, Func<TriggerVerdict, bool> Fired)[] Triggers =
    [
        ("T1 no intent (planner)", v => v.T1),
        ("T2 could not answer (simulated outcome)", v => v.T2),
        ("T3 operational request (lexicon)", v => v.T3),
        ("T1+T2+T3 (Triggered)", v => v.ShouldRun),
        ("T2+T3 (without the planner)", v => v.T2 || v.T3),
    ];
}
