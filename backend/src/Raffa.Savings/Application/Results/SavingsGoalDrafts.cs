using System.Text.RegularExpressions;

namespace Raffa.Savings.Application.Results;

/// <summary>A goal's scope: one supplier, or categories, or (informational) the contracts in scope
/// when the goal was parsed. Filtering uses <see cref="Supplier"/> and <see cref="Categories"/>.</summary>
public sealed record SavingsGoalScope(string? Supplier, IReadOnlyList<string>? Categories, IReadOnlyList<Guid> ContractIds);

/// <summary>A key/value row of the "Cosa ho capito" draft.</summary>
public sealed record SavingsGoalRow(ResultsMsg K, ResultsMsg V);

/// <summary>
/// <c>POST /api/savings/goals/parse</c>: the draft the user confirms with "Sì, è questo" (prototype
/// <c>parseGoal</c> shape). <see cref="Ok"/> false = the one question to ask is <see cref="Question"/>.
/// </summary>
public sealed record SavingsGoalDraft(
    bool Ok,
    string Text,
    decimal? Pct,
    decimal? Annual,
    SavingsGoalScope Scope,
    string Period,
    ResultsMsg PeriodLabel,
    ResultsMsg Label,
    decimal Spend,
    int ContractCount,
    ResultsMsg Kicker,
    ResultsMsg Title,
    IReadOnlyList<SavingsGoalRow> Rows,
    ResultsMsg? Question,
    string Source);

/// <summary>What a parser understood from the text, before the scope is applied to the contracts.
/// <see cref="Period"/>: <c>current</c> | <c>next</c> | <c>quarter</c>.</summary>
public sealed record SavingsGoalInterpretation(
    decimal? Pct,
    decimal? Annual,
    string? Supplier,
    IReadOnlyList<string>? Categories,
    string? LabelId,
    string Period);

/// <summary>Turns free text into a <see cref="SavingsGoalDraft"/>. Production: LLM-backed with this
/// module's <see cref="DeterministicSavingsGoalParser"/> as fallback; fixture mode: deterministic.</summary>
public interface ISavingsGoalParser
{
    Task<SavingsGoalDraft?> ParseAsync(string text, IReadOnlyList<ResultsContract> validated, DateOnly today, CancellationToken cancellationToken = default);
}

/// <summary>
/// The prototype's <c>parseGoal(text)</c> (AskRaffa V4.dc.html), identical regexes; also the shared
/// draft builder every parser ends with (<see cref="BuildDraft"/>), so the shape and texts never
/// depend on which parser read the text.
/// </summary>
public sealed class DeterministicSavingsGoalParser : ISavingsGoalParser
{
    public const string IdItContracts = "IT contracts (software and cloud)";
    public const string IdAllContracts = "all validated contracts";
    public const string IdCloudContracts = "cloud contracts";
    public const string IdCrmContracts = "CRM contracts";
    public const string IdProductivityContracts = "productivity contracts";

    public static readonly IReadOnlyList<string> ItCategories = ["CRM", "Productivity", "Cloud infrastructure"];

    private const RegexOptions Js = RegexOptions.ECMAScript | RegexOptions.CultureInvariant;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);
    private static readonly Regex Cloud = new("cloud|infrastru|aws|hosting", Js, Timeout);
    private static readonly Regex Crm = new("crm|vendit|sales", Js, Timeout);
    private static readonly Regex Productivity = new("produttiv|office|m365|collab|firma", Js, Timeout);
    private static readonly Regex It = new(@"\bit\b|software|saas|tecnolog|licen|digital|informatic", Js, Timeout);
    private static readonly Regex Percent = new(@"(\d+(?:[.,]\d+)?)\s*%", Js, Timeout);
    private static readonly Regex AmountWithUnit = new(@"(?:chf|eur|€)?\s*(\d+(?:[.,]\d+)?)\s*(k|m\b|mila|milion|million)", Js, Timeout);
    private static readonly Regex AmountWithCurrency = new(@"(?:chf|eur|€)\s*(\d[\d'.,]*)", Js, Timeout);
    private static readonly Regex ThousandsSeparator = new(@"['.,](?=\d{3})", Js, Timeout);
    private static readonly Regex Quarter = new("trimestr|quarter", Js, Timeout);
    private static readonly Regex LeadingNumber = new(@"^\d+(\.\d+)?", Js, Timeout);

    public Task<SavingsGoalDraft?> ParseAsync(string text, IReadOnlyList<ResultsContract> validated, DateOnly today, CancellationToken cancellationToken = default) =>
        Task.FromResult(Parse(text, validated, today));

    public static SavingsGoalDraft? Parse(string text, IReadOnlyList<ResultsContract> validated, DateOnly today)
    {
        var interpretation = Interpret(text, validated, today);
        return interpretation is null ? null : BuildDraft(text, interpretation, validated, today, "deterministic");
    }

    /// <summary>The regex reading of the text (null for blank text).</summary>
    public static SavingsGoalInterpretation? Interpret(string text, IReadOnlyList<ResultsContract> validated, DateOnly today)
    {
        var t = (text ?? "").ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(t)) return null;

        var supplier = validated.Select(c => c.Supplier).Distinct().FirstOrDefault(s => s.Length > 0 && t.Contains(s.ToLowerInvariant(), StringComparison.Ordinal));
        IReadOnlyList<string>? cats = null;
        string? labelId = supplier is null ? IdAllContracts : null;
        if (supplier is null)
        {
            if (Cloud.IsMatch(t)) { cats = ["Cloud infrastructure"]; labelId = IdCloudContracts; }
            else if (Crm.IsMatch(t)) { cats = ["CRM"]; labelId = IdCrmContracts; }
            else if (Productivity.IsMatch(t)) { cats = ["Productivity"]; labelId = IdProductivityContracts; }
            else if (It.IsMatch(t)) { cats = ItCategories; labelId = IdItContracts; }
        }

        decimal? pct = null;
        var pm = Percent.Match(t);
        if (pm.Success) pct = ParseJsFloat(pm.Groups[1].Value.Replace(',', '.'));

        decimal? annual = null;
        var am = AmountWithUnit.Match(t);
        if (!am.Success) am = AmountWithCurrency.Match(t);
        if (am.Success)
        {
            var raw = ThousandsSeparator.Replace(am.Groups[1].Value, "").Replace(',', '.');
            var n = ParseJsFloat(raw);
            var unit = am.Groups.Count > 2 ? am.Groups[2].Value : "";
            if (n is { } value)
            {
                annual = Regex.IsMatch(unit, "^m|milion|million", Js, Timeout) ? value * 1_000_000m
                    : Regex.IsMatch(unit, "k|mila", Js, Timeout) ? value * 1_000m
                    : value;
            }
        }

        var next = (today.Year + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var period = Regex.IsMatch(t, "prossimo anno|anno prossimo|" + next + "|next year", Js, Timeout) ? "next"
            : Quarter.IsMatch(t) ? "quarter"
            : "current";

        return new SavingsGoalInterpretation(pct, annual, supplier, cats, labelId, period);
    }

    /// <summary>The draft for an interpretation: scope applied to the validated contracts, the missing
    /// percent/amount derived from the spend in scope (a user target, not an estimate), the texts.</summary>
    public static SavingsGoalDraft BuildDraft(string text, SavingsGoalInterpretation i, IReadOnlyList<ResultsContract> validated, DateOnly today, string source)
    {
        var supplier = i.Supplier is { } s ? validated.Select(c => c.Supplier).FirstOrDefault(x => string.Equals(x, s, StringComparison.OrdinalIgnoreCase)) ?? s : null;
        var cats = supplier is null && i.Categories is { Count: > 0 } ? i.Categories : null;
        var inScope = InScope(validated, supplier, cats);
        var spend = inScope.Sum(c => c.Spend);
        var labelId = supplier is not null ? null : cats is null ? IdAllContracts : i.LabelId ?? LabelFor(cats);
        var label = supplier is not null ? ResultsMsg.Of("{supplier}", ("supplier", supplier)) : ResultsMsg.Of(labelId!);

        var pct = i.Pct;
        var annual = i.Annual;
        if (pct is not null && annual is null && spend != 0) annual = ResultsReportBuilder.PercentOf(spend, pct.Value);
        if (annual is not null && pct is null && spend != 0) pct = Math.Round(annual.Value / spend * 1000m, 0, MidpointRounding.AwayFromZero) / 10m;

        var (periodCode, periodLabel) = PeriodOf(i.Period, today);
        var scope = new SavingsGoalScope(supplier, cats, inScope.Select(c => c.ContractId).ToList());

        if (pct is null && annual is null)
        {
            var question = ResultsMsg.Of("Tell me how much: a percentage (“20%”) or an amount (“CHF 300k”).");
            return new SavingsGoalDraft(
                false, text, null, null, scope, periodCode, periodLabel, label, spend, inScope.Count,
                ResultsMsg.Of("I need one more thing"),
                question,
                [new SavingsGoalRow(ResultsMsg.Of("Example"), ResultsMsg.Of("“I want to save 20% on IT contracts next year”"))],
                question,
                source);
        }

        return new SavingsGoalDraft(
            true, text, pct, annual, scope, periodCode, periodLabel, label, spend, inScope.Count,
            ResultsMsg.Of("What I understood"),
            Title(pct, annual, label, periodLabel),
            [
                new SavingsGoalRow(ResultsMsg.Of("Measured on"), MeasuredOn(spend, inScope.Count)),
                new SavingsGoalRow(ResultsMsg.Of("Counts as a result"), ResultsMsg.Of("only the signed price against the previous contract, same quantities — never an estimate")),
                new SavingsGoalRow(ResultsMsg.Of("How I help"), ResultsMsg.Of("I show the contracts in scope with their deadline and what each is worth if you hit the target; you set the per-case target in the renewal chat")),
            ],
            null,
            source);
    }

    /// <summary>"Risparmiare 20% — CHF 128k l’anno su contratti IT (software e cloud) · AF 2027."</summary>
    public static ResultsMsg Title(decimal? pct, decimal? annual, ResultsMsg label, ResultsMsg period)
    {
        var hasAnnual = annual is { } a && a != 0;
        ResultsMsg target = pct is { } p
            ? hasAnnual
                ? ResultsMsg.Of("{pct}% — {amount} a year", ("pct", ResultsFormat.Js(p)), ("amount", ResultsFormat.K(annual!.Value)))
                : ResultsMsg.Of("{pct}%", ("pct", ResultsFormat.Js(p)))
            : ResultsMsg.Of("{amount} a year", ("amount", hasAnnual ? ResultsFormat.K(annual!.Value) : ""));
        return ResultsMsg.Of("Save {target} on {scope} · {period}.", ("target", target), ("scope", label), ("period", period));
    }

    public static ResultsMsg MeasuredOn(decimal spend, int count) =>
        spend == 0
            ? ResultsMsg.Of("no validated contract in this scope — it stays a figure")
            : count == 1
                ? ResultsMsg.Of("{spend} of annual spend · 1 validated contract", ("spend", ResultsFormat.K(spend)))
                : ResultsMsg.Of("{spend} of annual spend · {n} validated contracts", ("spend", ResultsFormat.K(spend)), ("n", count));

    /// <summary>The contracts a goal covers (prototype: category filter, supplier filter).</summary>
    public static IReadOnlyList<ResultsContract> InScope(IReadOnlyList<ResultsContract> validated, string? supplier, IReadOnlyList<string>? categories) =>
        validated
            .Where(c => (categories is null || (c.Category is not null && categories.Contains(c.Category)))
                        && (supplier is null || string.Equals(c.Supplier, supplier, StringComparison.OrdinalIgnoreCase)))
            .ToList();

    /// <summary>Stored period code and its message: <c>FY yyyy</c> (current or next fiscal year) or
    /// <c>this quarter</c>.</summary>
    public static (string Code, ResultsMsg Label) PeriodOf(string period, DateOnly today)
    {
        if (period == "quarter") return ("this quarter", ResultsMsg.Of("this quarter"));
        var year = period == "next" ? today.Year + 1 : today.Year;
        return ("FY " + year.ToString(System.Globalization.CultureInfo.InvariantCulture), FiscalYear(year));
    }

    /// <summary>The message of a stored period code.</summary>
    public static ResultsMsg PeriodLabelOf(string code) =>
        code.StartsWith("FY ", StringComparison.Ordinal) && int.TryParse(code.AsSpan(3), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var year)
            ? FiscalYear(year)
            : ResultsMsg.Of(code);

    private static ResultsMsg FiscalYear(int year) =>
        ResultsMsg.Of("FY {year}", ("year", year.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private static string LabelFor(IReadOnlyList<string> cats) =>
        cats.Count == 1
            ? cats[0] switch
            {
                "Cloud infrastructure" => IdCloudContracts,
                "CRM" => IdCrmContracts,
                "Productivity" => IdProductivityContracts,
                _ => IdItContracts,
            }
            : IdItContracts;

    private static decimal? ParseJsFloat(string raw)
    {
        var m = LeadingNumber.Match(raw);
        return m.Success && decimal.TryParse(m.Value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
