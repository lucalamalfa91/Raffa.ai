using System.Globalization;
using Raffa.Chat.Application.Guards;
using Raffa.Chat.Application.Language;
using Raffa.Chat.Application.Pack;
using Raffa.Chat.Application.Planning;

namespace Raffa.Chat.Application.Council;

/// <summary>What the verdict says. The first three are the calculators' feasibility scale, the last
/// three answer a turn with no goal ("is a meaningful saving available?").</summary>
public enum CouncilVerdictKind
{
    /// <summary>The grounded levers' high estimate covers the target.</summary>
    Reachable,

    /// <summary>The high estimate covers at least <see cref="CouncilVerdict.StretchCoverageFraction"/> of the target.</summary>
    Stretch,

    /// <summary>The grounded levers do not add up to the target.</summary>
    NotSupported,

    /// <summary>No goal: the high estimate is at least <see cref="CouncilVerdict.MeaningfulSavingFraction"/> of the annual spend.</summary>
    MeaningfulSaving,

    /// <summary>No goal: the high estimate is below that share of the annual spend.</summary>
    LimitedSaving,

    /// <summary>No goal and no spend to compare with: only the coverage range is stated.</summary>
    CoverageOnly,
}

/// <summary>
/// The one owner of the council's verdict on the goal (plan F2-D03 / F2-T08). Until now two parts
/// each said whether the target is reachable -- the savings calculator
/// (<c>calc:savings-target</c> / <c>calc:portfolio-target</c>) and the lever strategist's own
/// <c>targetReachable</c> flag -- and they could contradict each other, or the strategist could
/// judge "reachable" with no goal at all. The strategist no longer returns a verdict: this
/// deterministic component reads the calculators' pack items and produces
/// <c>calc:council:verdict</c>, so the verdict can only agree with them.
///
/// <list type="bullet">
/// <item>With a goal, the calculator's own feasibility label wins (<see cref="ReachableLabel"/> and
/// its siblings are the strings the calculators write; the Ask host builds its labels from these
/// constants, so they cannot drift). When the target item carries no label, the same rule is
/// applied to the numbers: reachable when the levers' high estimate covers the target, a stretch
/// from <see cref="StretchCoverageFraction"/> of it (the value in <c>SavingsLeverCalculator</c>),
/// otherwise not supported.</item>
/// <item>Without a goal nothing is "reachable": the verdict says whether a meaningful saving is
/// available -- the high estimate is at least <see cref="MeaningfulSavingFraction"/> of the annual
/// spend (explicit threshold, to be validated with the product owner) -- or, with no spend to
/// compare with, only states the range.</item>
/// <item>Every verdict is labelled an upper bound with the range coverageLow-coverageHigh, and
/// every figure in its text is also a value of the verdict item itself, so the numeric guard can
/// ground what the answer quotes.</item>
/// <item>The reason is built from the numbers and the biggest lever, never from the calculator's
/// explanation (an instruction to the model) and never from a model.</item>
/// </list>
/// </summary>
public static class CouncilVerdict
{
    public const string CitationKey = "calc:council:verdict";

    /// <summary>Mirrors <c>SavingsLeverCalculator.StretchCoverageFraction</c> (Raffa.Insights, which
    /// Raffa.Chat cannot reference): used only when a target item carries no feasibility label.</summary>
    public const decimal StretchCoverageFraction = 0.6m;

    /// <summary>Without a goal, the share of the annual spend from which a saving is "meaningful".</summary>
    public const decimal MeaningfulSavingFraction = 0.05m;

    /// <summary>The feasibility labels the calculators write in the target item's subtitle.</summary>
    public const string ReachableLabel = "target reachable";

    public const string StretchLabel = "target is a stretch";

    public const string NotSupportedLabel = "target not supported by the evidence";

    public const string NoTargetLabel = "no target named";

    private const string DefaultProvenance = "deterministic calculator";

    /// <summary>The verdict as a pack item (<c>calc:council:verdict</c>), or <see langword="null"/>
    /// when the pack holds nothing to base one on (no lever coverage at all).</summary>
    /// <param name="pack">The pack the council read, calculators' items included.</param>
    /// <param name="goal">The goal the planner parsed from the question, if any.</param>
    /// <param name="question">The question, for the language of the text.</param>
    public static PackItem? BuildItem(IReadOnlyList<PackItem> pack, SavingsGoal? goal, string question)
    {
        ArgumentNullException.ThrowIfNull(pack);

        var evaluation = Evaluate(pack, goal);
        if (evaluation is null)
        {
            return null;
        }

        var italian = QuestionLanguage.IsItalian(QuestionLanguage.Detect(question));
        var (title, reason) = Compose(evaluation, italian, includeLever: true);

        var values = new List<PackValue>();
        if (evaluation.Target is { } target)
        {
            values.Add(new PackValue("targetAmount", Amount(target), PackValueKind.Amount, evaluation.Currency));
        }

        if (evaluation.CoverageLow is { } coverageLow)
        {
            values.Add(new PackValue("coverageLow", Amount(coverageLow), PackValueKind.Amount, evaluation.Currency));
        }

        values.Add(new PackValue("coverageHigh", Amount(evaluation.CoverageHigh), PackValueKind.Amount, evaluation.Currency));

        var item = Item(title, reason, evaluation.Provenance, values);

        // Safety net: the figures are the item's own values, so this holds by construction; a lever
        // title that happens to carry a number the pack lacks is dropped rather than quoted.
        // (Its own snippet is left out of the check: a text must not ground itself.)
        if (!NumericGuard.Validate(reason, [.. pack, item with { Snippet = string.Empty }]).Passed)
        {
            item = Item(title, Compose(evaluation, italian, includeLever: false).Reason, evaluation.Provenance, values);
        }

        return item;
    }

    /// <summary>The verdict's kind and figures, or <see langword="null"/> without a basis. Public so
    /// tests (and later the investigator's telemetry) can read the decision without the text.</summary>
    public static CouncilVerdictEvaluation? Evaluate(IReadOnlyList<PackItem> pack, SavingsGoal? goal)
    {
        ArgumentNullException.ThrowIfNull(pack);

        var targetItem = pack.FirstOrDefault(i => i.CitationKey is "calc:savings-target" or "calc:portfolio-target");
        var levers = pack.Where(i => i.CitationKey.StartsWith("calc:lever[", StringComparison.Ordinal)).ToList();

        var coverageHigh = ValueOf(targetItem, "coverageHigh") ?? Sum(levers, "estimatedHigh");
        if (coverageHigh is null)
        {
            return null;
        }

        var coverageLow = ValueOf(targetItem, "coverageLow") ?? Sum(levers, "estimatedLow");
        var spend = ValueOf(targetItem, "annualSpend");
        var hasGoal = goal is { HasTarget: true };

        var target = hasGoal
            ? ValueOf(targetItem, "targetAmount")
                ?? goal!.TargetAmount
                ?? (goal.TargetPercent is { } percent && spend is { } annual
                    ? Math.Round(annual * percent / 100m, 0, MidpointRounding.AwayFromZero)
                    : null)
            : null;

        CouncilVerdictKind kind;
        if (hasGoal && FromLabel(targetItem?.Subtitle) is { } declared)
        {
            kind = declared;
        }
        else if (hasGoal && target is { } known)
        {
            kind = FromNumbers(coverageHigh.Value, known);
        }
        else if (spend is > 0)
        {
            kind = coverageHigh.Value >= spend.Value * MeaningfulSavingFraction
                ? CouncilVerdictKind.MeaningfulSaving
                : CouncilVerdictKind.LimitedSaving;
        }
        else
        {
            kind = CouncilVerdictKind.CoverageOnly;
        }

        var currency = targetItem?.Values.FirstOrDefault(v => v.Key == "coverageHigh")?.Currency
            ?? levers.SelectMany(l => l.Values).FirstOrDefault(v => v.Key == "estimatedHigh")?.Currency
            ?? goal?.Currency;

        var biggest = levers
            .Select(l => (Item: l, High: ValueOf(l, "estimatedHigh") ?? 0m))
            .Where(x => x.High > 0)
            .OrderByDescending(x => x.High)
            .Select(x => x.Item)
            .FirstOrDefault();

        return new CouncilVerdictEvaluation(
            kind, target, coverageLow, coverageHigh.Value, currency, biggest?.Title, targetItem?.Provenance ?? DefaultProvenance);
    }

    /// <summary>The calculators' feasibility label, as a kind; <see langword="null"/> for "no target named" or any other text.</summary>
    internal static CouncilVerdictKind? FromLabel(string? label) => label?.Trim().ToLowerInvariant() switch
    {
        ReachableLabel => CouncilVerdictKind.Reachable,
        StretchLabel => CouncilVerdictKind.Stretch,
        NotSupportedLabel => CouncilVerdictKind.NotSupported,
        _ => null,
    };

    private static CouncilVerdictKind FromNumbers(decimal coverageHigh, decimal target) =>
        coverageHigh >= target ? CouncilVerdictKind.Reachable
        : coverageHigh >= target * StretchCoverageFraction ? CouncilVerdictKind.Stretch
        : CouncilVerdictKind.NotSupported;

    private static (string Title, string Reason) Compose(CouncilVerdictEvaluation e, bool italian, bool includeLever)
    {
        var high = Money(e.Currency, e.CoverageHigh);
        var low = e.CoverageLow is { } l ? Money(e.Currency, l) : null;
        var range = low is null
            ? (italian ? $"fino a {high}" : $"up to {high}")
            : (italian ? $"tra {low} e {high}" : $"between {low} and {high}");
        var from = low is null ? string.Empty : (italian ? $" (da {low})" : $" (from {low})");
        var goal = e.Target is { } t
            ? (italian ? $"l'obiettivo di {Money(e.Currency, t)}" : $"the {Money(e.Currency, t)} target")
            : (italian ? "l'obiettivo" : "the goal");

        var (title, reason) = (e.Kind, italian) switch
        {
            (CouncilVerdictKind.Reachable, false) => (
                "Council verdict — the goal is reachable (upper bound)",
                $"The grounded levers are worth {range} a year; at best they cover {goal}. The high end assumes the levers are obtained together, so read it as an upper bound."),
            (CouncilVerdictKind.Reachable, true) => (
                "Verdetto del consiglio — l'obiettivo è raggiungibile (limite superiore)",
                $"Le leve documentate valgono {range} l'anno; nel caso migliore coprono {goal}. L'estremo alto presuppone di ottenere le leve insieme: va letto come limite superiore."),
            (CouncilVerdictKind.Stretch, false) => (
                "Council verdict — the goal is a stretch (upper bound)",
                $"The grounded levers are worth {range} a year: even at the high end they cover only part of {goal}, so it takes every lever and more. Upper bound, not a forecast."),
            (CouncilVerdictKind.Stretch, true) => (
                "Verdetto del consiglio — l'obiettivo è ambizioso (limite superiore)",
                $"Le leve documentate valgono {range} l'anno: anche all'estremo alto coprono solo in parte {goal}, servono tutte le leve e qualcosa in più. Limite superiore, non una previsione."),
            (CouncilVerdictKind.NotSupported, false) => (
                "Council verdict — the goal is not supported by the grounded levers",
                $"At the high end the grounded levers reach {high} a year{from} against {goal}: the evidence does not support the goal as things stand."),
            (CouncilVerdictKind.NotSupported, true) => (
                "Verdetto del consiglio — l'obiettivo non è sostenuto dalle leve documentate",
                $"All'estremo alto le leve documentate arrivano a {high} l'anno{from} contro {goal}: allo stato attuale i dati non sostengono l'obiettivo."),
            (CouncilVerdictKind.MeaningfulSaving, false) => (
                "Council verdict — a meaningful saving is available (upper bound)",
                $"No goal was named. The grounded levers are worth {range} a year, a meaningful share of the annual spend. Upper bound."),
            (CouncilVerdictKind.MeaningfulSaving, true) => (
                "Verdetto del consiglio — c'è un risparmio significativo (limite superiore)",
                $"Nessun obiettivo indicato. Le leve documentate valgono {range} l'anno, una quota significativa della spesa annua. Limite superiore."),
            (CouncilVerdictKind.LimitedSaving, false) => (
                "Council verdict — no meaningful saving found on the grounded levers",
                $"No goal was named. The grounded levers add up to at most {high} a year{from}, a small share of the annual spend."),
            (CouncilVerdictKind.LimitedSaving, true) => (
                "Verdetto del consiglio — nessun risparmio significativo sulle leve documentate",
                $"Nessun obiettivo indicato. Le leve documentate sommano al massimo {high} l'anno{from}, una quota ridotta della spesa annua."),
            (_, false) => (
                "Council verdict — savings range on the grounded levers (upper bound)",
                $"No goal was named. The grounded levers are worth {range} a year. Upper bound."),
            (_, true) => (
                "Verdetto del consiglio — intervallo di risparmio sulle leve documentate (limite superiore)",
                $"Nessun obiettivo indicato. Le leve documentate valgono {range} l'anno. Limite superiore."),
        };

        if (includeLever && !string.IsNullOrWhiteSpace(e.BiggestLeverTitle))
        {
            reason += italian
                ? $" La leva più grande: {e.BiggestLeverTitle.Trim()}."
                : $" The biggest lever: {e.BiggestLeverTitle.Trim()}.";
        }

        return (title, reason);
    }

    private static PackItem Item(string title, string reason, string provenance, IReadOnlyList<PackValue> values) =>
        new(CitationKey, PackCorpus.Calc, title, provenance, null, null, reason, null, null, null, provenance, values);

    private static decimal? ValueOf(PackItem? item, string key) =>
        item?.Values.FirstOrDefault(v => string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase)) is { } value
        && decimal.TryParse(value.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static decimal? Sum(IReadOnlyList<PackItem> items, string key)
    {
        decimal? total = null;
        foreach (var item in items)
        {
            if (ValueOf(item, key) is { } value)
            {
                total = (total ?? 0m) + value;
            }
        }

        return total;
    }

    private static string Amount(decimal value) =>
        Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

    private static string Money(string? currency, decimal value) =>
        string.IsNullOrWhiteSpace(currency) ? Amount(value) : $"{currency} {Amount(value)}";
}

/// <summary>The verdict's decision and figures, before the text: <see cref="Target"/> is
/// <see langword="null"/> when no goal amount is known and <see cref="CoverageLow"/> when no lower estimate is.</summary>
public sealed record CouncilVerdictEvaluation(
    CouncilVerdictKind Kind,
    decimal? Target,
    decimal? CoverageLow,
    decimal CoverageHigh,
    string? Currency,
    string? BiggestLeverTitle,
    string Provenance);
