using System.Globalization;
using Raffa.Chat.Application.Guards;
using Raffa.Chat.Application.Language;
using Raffa.Chat.Application.Pack;
using Raffa.Chat.Application.Planning;

namespace Raffa.Chat.Application.Council;

/// <summary>What the verdict says: the first three are the calculators' feasibility scale, the last
/// three answer a turn with no goal ("is a meaningful saving available?").</summary>
public enum CouncilVerdictKind
{
    Reachable,
    Stretch,
    NotSupported,
    MeaningfulSaving,
    LimitedSaving,
    CoverageOnly,
}

/// <summary>
/// The one owner of the council's verdict on the goal (plan F2-D03 / F2-T08): the lever strategist
/// returns plays only, and this deterministic component reads the calculators' pack items
/// (<c>calc:savings-target</c> / <c>calc:portfolio-target</c> / <c>calc:lever[..]</c>) to produce
/// <c>calc:council:verdict</c>, so the verdict can only agree with them. With a goal the
/// calculator's own feasibility label wins (the numbers decide only when the item has no label);
/// without a goal nothing is "reachable": the verdict says whether the high estimate is a
/// meaningful share of the annual spend, or just states the range. Every verdict is an upper bound,
/// every figure in its text is a value of the item itself (so the numeric guard grounds it), and
/// the text is built from the numbers and the biggest lever, never from a model.
/// </summary>
public static class CouncilVerdict
{
    public const string CitationKey = "calc:council:verdict";

    /// <summary>Mirrors <c>SavingsLeverCalculator.StretchCoverageFraction</c> (Raffa.Insights, which
    /// Raffa.Chat cannot reference): used only when a target item carries no feasibility label.</summary>
    private const decimal StretchCoverageFraction = 0.6m;

    /// <summary>Without a goal, the share of the annual spend from which a saving is "meaningful".</summary>
    private const decimal MeaningfulSavingFraction = 0.05m;

    /// <summary>The feasibility labels the calculators write in the target item's subtitle.</summary>
    public const string ReachableLabel = "target reachable";

    public const string StretchLabel = "target is a stretch";

    public const string NotSupportedLabel = "target not supported by the evidence";

    public const string NoTargetLabel = "no target named";

    private const string DefaultProvenance = "deterministic calculator";

    /// <summary>The verdict as a pack item (<c>calc:council:verdict</c>), or <see langword="null"/>
    /// when the pack holds nothing to base one on (no lever coverage at all).</summary>
    public static PackItem? BuildItem(IReadOnlyList<PackItem> pack, SavingsGoal? goal, string question)
    {
        if (Evaluate(pack, goal) is not { } e)
        {
            return null;
        }

        var italian = QuestionLanguage.IsItalian(QuestionLanguage.Detect(question));
        var (title, reason) = Compose(e, italian);

        PackValue Money(string key, decimal value) => new(key, Amount(value), PackValueKind.Amount, e.Currency);
        var values = new List<PackValue>();
        if (e.Target is { } target)
        {
            values.Add(Money("targetAmount", target));
        }

        if (e.CoverageLow is { } coverageLow)
        {
            values.Add(Money("coverageLow", coverageLow));
        }

        values.Add(Money("coverageHigh", e.CoverageHigh));

        var lever = string.IsNullOrWhiteSpace(e.BiggestLeverTitle)
            ? string.Empty
            : italian ? $" La leva più grande: {e.BiggestLeverTitle.Trim()}." : $" The biggest lever: {e.BiggestLeverTitle.Trim()}.";
        var item = new PackItem(CitationKey, PackCorpus.Calc, title, e.Provenance, null, null, reason + lever, null, null, null, e.Provenance, values);

        // Safety net: the figures are the item's own values, so this holds by construction; a lever
        // title that happens to carry a number the pack lacks is dropped rather than quoted.
        // (Its own snippet is left out of the check: a text must not ground itself.)
        return NumericGuard.Validate(item.Snippet, [.. pack, item with { Snippet = string.Empty }]).Passed
            ? item
            : item with { Snippet = reason };
    }

    /// <summary>The verdict's kind and figures, or <see langword="null"/> without a basis. Public so
    /// tests can read the decision without the text.</summary>
    public static CouncilVerdictEvaluation? Evaluate(IReadOnlyList<PackItem> pack, SavingsGoal? goal)
    {
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

        var declared = hasGoal ? targetItem?.Subtitle?.Trim().ToLowerInvariant() switch
        {
            ReachableLabel => CouncilVerdictKind.Reachable,
            StretchLabel => CouncilVerdictKind.Stretch,
            NotSupportedLabel => CouncilVerdictKind.NotSupported,
            _ => (CouncilVerdictKind?)null,
        } : null;

        var kind = declared
            ?? (hasGoal && target is { } known
                ? coverageHigh >= known ? CouncilVerdictKind.Reachable
                    : coverageHigh >= known * StretchCoverageFraction ? CouncilVerdictKind.Stretch
                    : CouncilVerdictKind.NotSupported
                : spend is > 0
                    ? coverageHigh >= spend * MeaningfulSavingFraction ? CouncilVerdictKind.MeaningfulSaving : CouncilVerdictKind.LimitedSaving
                    : CouncilVerdictKind.CoverageOnly);

        var currency = targetItem?.Values.FirstOrDefault(v => v.Key == "coverageHigh")?.Currency
            ?? levers.SelectMany(l => l.Values).FirstOrDefault(v => v.Key == "estimatedHigh")?.Currency
            ?? goal?.Currency;

        var biggest = levers.Where(l => ValueOf(l, "estimatedHigh") > 0).MaxBy(l => ValueOf(l, "estimatedHigh"));

        return new CouncilVerdictEvaluation(
            kind, target, coverageLow, coverageHigh.Value, currency, biggest?.Title, targetItem?.Provenance ?? DefaultProvenance);
    }

    private static (string Title, string Reason) Compose(CouncilVerdictEvaluation e, bool italian)
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

        return (title, reason);
    }

    private static decimal? ValueOf(PackItem? item, string key) =>
        item?.Values.FirstOrDefault(v => string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase)) is { } value
        && decimal.TryParse(value.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static decimal? Sum(IEnumerable<PackItem> items, string key) =>
        items.Select(i => ValueOf(i, key)).OfType<decimal>().ToList() is { Count: > 0 } values ? values.Sum() : null;

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
