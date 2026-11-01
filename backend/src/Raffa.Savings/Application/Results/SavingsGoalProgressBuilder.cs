using Raffa.SharedKernel.Savings;

namespace Raffa.Savings.Application.Results;

/// <summary>The active goal as the page shows it (the goal bar's summary is <see cref="Title"/>).</summary>
public sealed record SavingsGoalView(
    Guid Id,
    string Text,
    decimal? Pct,
    decimal? Annual,
    SavingsGoalScope Scope,
    string Period,
    ResultsMsg PeriodLabel,
    ResultsMsg Label,
    ResultsMsg Title,
    DateTimeOffset CreatedAt);

/// <summary>A contract the user can act on to reach the goal ("Contratti su cui puoi agire").</summary>
public sealed record SavingsGoalActionRow(
    Guid ContractId,
    Guid? CaseId,
    string Supplier,
    decimal Spend,
    bool HasCase,
    string? ObjectiveKind,
    int ObjectivePercent,
    decimal ObjectiveAmount,
    string Uplift,
    bool AutoRenewal,
    decimal IfHit,
    int Days,
    DateOnly? NoticeDeadline,
    bool Primary);

/// <summary>
/// <c>GET /api/savings/goals/active/progress</c> ("Verso il tuo obiettivo"): the target, what is
/// signed in scope this fiscal year, the objectives fixed in open cases (user targets, not estimates),
/// what is still to cover, bar widths (percent of the scale), the verdict and the actionable contracts.
/// <see cref="Verdict"/>: <c>no_contracts</c> | <c>covered</c> | <c>gap</c>.
/// </summary>
public sealed record SavingsGoalProgress(
    SavingsGoalView Goal,
    decimal Spend,
    int ContractCount,
    decimal Pct,
    decimal Target,
    decimal Signed,
    decimal Objectives,
    decimal Gap,
    decimal SignedWidth,
    decimal ObjectivesWidth,
    string Verdict,
    decimal IfAllHit,
    IReadOnlyList<SavingsGoalActionRow> Rows);

/// <summary>Port of the prototype's <c>gl</c> (renderVals, "obiettivo (testo libero) → pagina adattata"). Pure.</summary>
public static class SavingsGoalProgressBuilder
{
    public static SavingsGoalProgress Build(
        SavingsGoalView goal,
        DateOnly today,
        IReadOnlyList<ResultsContract> validated,
        IReadOnlyList<ResultsReportItem> results,
        IReadOnlyList<CaseObjectiveInfo> caseObjectives)
    {
        var supplier = goal.Scope.Supplier;
        var cats = goal.Scope.Categories is { Count: > 0 } c ? c : null;
        var ids = DeterministicSavingsGoalParser.InScope(validated, supplier, cats);
        var spend = ids.Sum(x => x.Spend);
        var pct = goal.Pct ?? 0m;
        var annual = goal.Annual is { } a && a != 0 ? a : ResultsReportBuilder.PercentOf(spend, pct);

        var caseByContract = caseObjectives.GroupBy(o => o.ContractId).ToDictionary(g => g.Key, g => g.Last());
        bool Closed(Guid id) => caseByContract.TryGetValue(id, out var o) && !o.IsOpen;

        bool HistInScope(ResultsReportItem h) =>
            (cats is null || (h.Category is not null && cats.Contains(h.Category)))
            && (supplier is null || string.Equals(h.Supplier, supplier, StringComparison.OrdinalIgnoreCase));
        var signed = results
            .Where(h => h.Kind != "volume" && HistInScope(h) && ResultsReportBuilder.InPeriod(h.SignedOn, ResultsReportBuilder.PeriodFiscalYear, today))
            .Sum(h => h.Amount);

        var open = ids.Where(x => !Closed(x.ContractId)).ToList();
        var rows = open
            .Select(x =>
            {
                caseByContract.TryGetValue(x.ContractId, out var cc);
                var p = cc?.TargetPercent ?? 0;
                return new SavingsGoalActionRow(
                    x.ContractId,
                    cc?.CaseId,
                    x.Supplier,
                    x.Spend,
                    cc is not null,
                    cc?.Kind,
                    p,
                    ResultsReportBuilder.PercentOf(x.Spend, p),
                    x.Uplift,
                    x.AutoRenewal,
                    ResultsReportBuilder.PercentOf(x.Spend, pct),
                    x.Days,
                    x.NoticeDeadline,
                    x.Days <= 120);
            })
            .OrderBy(r => r.Days)
            .ToList();
        var objectives = rows.Sum(r => r.ObjectiveAmount);

        var gap = Math.Max(0m, annual - signed - objectives);
        var scale = Math.Max(Math.Max(annual, signed + objectives), 1m);
        var signedWidth = Math.Min(100m, signed / scale * 100m);
        var objectivesWidth = Math.Min(100m - signedWidth, objectives / scale * 100m);

        return new SavingsGoalProgress(
            goal,
            spend,
            ids.Count,
            pct,
            annual,
            signed,
            objectives,
            gap,
            Math.Round(signedWidth, 1, MidpointRounding.AwayFromZero),
            Math.Round(objectivesWidth, 1, MidpointRounding.AwayFromZero),
            ids.Count == 0 ? "no_contracts" : gap == 0 ? "covered" : "gap",
            rows.Sum(r => r.IfHit),
            rows);
    }
}
