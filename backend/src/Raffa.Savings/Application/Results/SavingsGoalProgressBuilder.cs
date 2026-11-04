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

/// <summary>
/// "Verso il tuo obiettivo". <see cref="FromSpend"/> is what the endpoint serves: the goal read on the
/// same fiscal periods as Spesa e risparmi. <see cref="Build"/> is the prototype's <c>gl</c> port
/// (annual run-rate, results counted by signing date), kept for hosts without the spend engine. Pure.
/// </summary>
public static class SavingsGoalProgressBuilder
{
    /// <summary>
    /// The goal on the periods it names (<paramref name="periodIndexes"/> of <paramref name="spend"/>):
    /// the target is the goal's amount, or its percent of the in-scope spend of those periods (accrual,
    /// everything the contracts run on if nothing changes); "signed" is what signed results save in
    /// those periods (a result signed in November that takes effect in January counts for January's
    /// year); "objectives" what the open cases' targets would save in them. Rows: the open contracts in
    /// scope with their next decision. A contract the spend report leaves out (no dates, value or
    /// budget rate) is not counted.
    /// </summary>
    public static SavingsGoalProgress FromSpend(
        SavingsGoalView goal,
        Spend.SpendReport spend,
        IReadOnlyList<int> periodIndexes,
        IReadOnlyList<ResultsContract> validated,
        IReadOnlyList<CaseObjectiveInfo> caseObjectives)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(spend);
        ArgumentNullException.ThrowIfNull(periodIndexes);
        var supplier = goal.Scope.Supplier;
        var cats = goal.Scope.Categories is { Count: > 0 } c ? c : null;
        var ids = DeterministicSavingsGoalParser.InScope(validated, supplier, cats);
        var rowsById = spend.Contracts.ToDictionary(r => r.ContractId);
        decimal Sum(IReadOnlyList<decimal> values) => periodIndexes.Where(i => i >= 0 && i < values.Count).Sum(i => values[i]);
        decimal SpendOf(Spend.SpendContractRow r) => Sum(r.Actual) + Sum(r.Committed) + Sum(r.AutoRenewal) + Sum(r.ToDecide);
        decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

        var inScope = ids.Where(x => rowsById.ContainsKey(x.ContractId)).Select(x => (Contract: x, Row: rowsById[x.ContractId])).ToList();
        var total = Round(inScope.Sum(x => SpendOf(x.Row)));
        var pct = goal.Pct ?? 0m;
        var target = goal.Annual is { } a && a != 0 ? a : Round(total * pct / 100m);
        var everything = supplier is null && cats is null;
        var signed = Round(inScope.Sum(x => Sum(x.Row.SavingsContractual)) + (everything && spend.OtherSavings is { } o ? Sum(o.SavingsContractual) : 0m));

        var caseByContract = caseObjectives.GroupBy(x => x.ContractId).ToDictionary(g => g.Key, g => g.Last());
        bool Closed(Guid id) => caseByContract.TryGetValue(id, out var x) && !x.IsOpen;
        var rows = inScope
            .Where(x => !Closed(x.Contract.ContractId))
            .Select(x =>
            {
                caseByContract.TryGetValue(x.Contract.ContractId, out var cc);
                var decideBy = x.Row.Decision.DecideBy;
                var days = decideBy is { } d ? d.DayNumber - spend.Today.DayNumber : x.Contract.Days;
                var contractSpend = Round(SpendOf(x.Row));
                return new SavingsGoalActionRow(
                    x.Contract.ContractId,
                    cc?.CaseId,
                    x.Contract.Supplier,
                    contractSpend,
                    cc is not null,
                    cc?.Kind,
                    cc?.TargetPercent ?? 0,
                    Round(Sum(x.Row.Objective)),
                    x.Contract.Uplift,
                    x.Contract.AutoRenewal,
                    Round(contractSpend * pct / 100m),
                    days,
                    decideBy ?? x.Contract.NoticeDeadline,
                    days <= 120);
            })
            .OrderBy(r => r.Days)
            .ToList();
        var objectives = rows.Sum(r => r.ObjectiveAmount);

        var gap = Math.Max(0m, target - signed - objectives);
        var scale = Math.Max(Math.Max(target, signed + objectives), 1m);
        var signedWidth = Math.Min(100m, signed / scale * 100m);
        var objectivesWidth = Math.Min(100m - signedWidth, objectives / scale * 100m);

        return new SavingsGoalProgress(
            goal,
            total,
            inScope.Count,
            pct,
            target,
            signed,
            objectives,
            gap,
            Math.Round(signedWidth, 1, MidpointRounding.AwayFromZero),
            Math.Round(objectivesWidth, 1, MidpointRounding.AwayFromZero),
            inScope.Count == 0 ? "no_contracts" : gap == 0 ? "covered" : "gap",
            rows.Sum(r => r.IfHit),
            rows);
    }

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
