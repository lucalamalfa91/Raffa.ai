using System.Globalization;
using System.Text.RegularExpressions;
using Raffa.SharedKernel.Contracts;

namespace Raffa.AiFlows.ContractSituation;

/// <summary>The model's answer, as the schema shapes it.</summary>
public sealed record ContractSituationAnswer(
    string? HeadlineEn,
    string? HeadlineIt,
    string? WhyEn,
    string? WhyIt,
    string? RecommendedAction,
    string? Urgency,
    string? SourceKey);

/// <summary>What the answer may say: the deterministic facts it is checked against.</summary>
/// <param name="Literals">Names that may contain digits (supplier, contract, document names); removed before the number check.</param>
public sealed record ContractSituationFacts(
    string Status,
    IReadOnlyCollection<DateOnly> Dates,
    IReadOnlyCollection<string> Numbers,
    IReadOnlyCollection<string> EvidenceKeys,
    IReadOnlyCollection<string> Literals);

/// <summary>
/// Validates the model's situation assessment against the deterministic facts before anything is
/// shown: every date it writes must be one Raffa computed or read, every number one Raffa gave it,
/// no amount or percentage at all, the action must fit the status, the source key must be a real
/// evidence key, and the texts must be present and short. Any failure returns a reason, and the
/// caller falls back to the deterministic copy.
/// </summary>
public static partial class ContractAssessmentGuard
{
    public const int MaxHeadline = 120;
    public const int MaxWhy = 700;

    private static readonly Dictionary<string, int> Months = BuildMonths();

    /// <returns>null when the answer is safe to show; otherwise why it was rejected.</returns>
    public static string? Check(ContractSituationAnswer? answer, ContractSituationFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (answer is null)
        {
            return "no answer";
        }

        if (answer.RecommendedAction is not { } action || !SituationAction.AllowedFor(facts.Status).Contains(action))
        {
            return $"action '{answer.RecommendedAction}' does not fit status '{facts.Status}'";
        }

        if (answer.Urgency is not { } urgency || !SituationUrgency.All.Contains(urgency))
        {
            return $"unknown urgency '{answer.Urgency}'";
        }

        if (facts.Status == ContractTermStatus.Expired && urgency == SituationUrgency.Now)
        {
            return "an expired contract cannot be urgent";
        }

        if (!string.IsNullOrEmpty(answer.SourceKey) && !facts.EvidenceKeys.Contains(answer.SourceKey))
        {
            return $"unknown source key '{answer.SourceKey}'";
        }

        foreach (var (name, text, max) in new[]
                 {
                     ("headlineEn", answer.HeadlineEn, MaxHeadline), ("headlineIt", answer.HeadlineIt, MaxHeadline),
                     ("whyEn", answer.WhyEn, MaxWhy), ("whyIt", answer.WhyIt, MaxWhy),
                 })
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return $"{name} is empty";
            }

            if (text.Length > max)
            {
                return $"{name} is too long";
            }

            if (CheckText(text, facts) is { } reason)
            {
                return $"{name}: {reason}";
            }
        }

        return null;
    }

    private static string? CheckText(string text, ContractSituationFacts facts)
    {
        var rest = text;
        foreach (var literal in facts.Literals.Where(l => !string.IsNullOrWhiteSpace(l)).OrderByDescending(l => l.Length))
        {
            rest = rest.Replace(literal, " ", StringComparison.OrdinalIgnoreCase);
        }

        if (Money().IsMatch(rest))
        {
            return "mentions an amount or a percentage";
        }

        var dates = facts.Dates.ToHashSet();
        foreach (var pattern in new[] { DayMonthYear(), MonthDayYear() })
        {
            foreach (Match m in pattern.Matches(rest))
            {
                var day = m.Groups["d"].Value;
                var month = m.Groups["m"].Value.TrimEnd('.').ToLowerInvariant();
                if (!Months.TryGetValue(month, out var mm))
                {
                    continue;
                }

                if (!int.TryParse(day, NumberStyles.None, CultureInfo.InvariantCulture, out var dd)
                    || !int.TryParse(m.Groups["y"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var yy)
                    || dd < 1 || dd > DateTime.DaysInMonth(Math.Clamp(yy, 1, 9999), mm)
                    || !dates.Contains(new DateOnly(yy, mm, dd)))
                {
                    return $"invented date '{m.Value}'";
                }

                rest = rest.Replace(m.Value, " ", StringComparison.Ordinal);
            }
        }

        foreach (Match m in NumericDate().Matches(rest))
        {
            if (!DateOnly.TryParseExact(m.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso) || !dates.Contains(iso))
            {
                return $"invented date '{m.Value}'";
            }

            rest = rest.Replace(m.Value, " ", StringComparison.Ordinal);
        }

        var years = dates.Select(d => d.Year.ToString(CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
        foreach (Match m in Number().Matches(rest))
        {
            if (!facts.Numbers.Contains(m.Value) && !years.Contains(m.Value))
            {
                return $"invented number '{m.Value}'";
            }
        }

        return null;
    }

    private static Dictionary<string, int> BuildMonths()
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        string[] en = ["january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december"];
        string[] it = ["gennaio", "febbraio", "marzo", "aprile", "maggio", "giugno", "luglio", "agosto", "settembre", "ottobre", "novembre", "dicembre"];
        string[] itShort = ["gen", "feb", "mar", "apr", "mag", "giu", "lug", "ago", "set", "ott", "nov", "dic"];
        for (var i = 0; i < 12; i++)
        {
            map[en[i]] = i + 1;
            map.TryAdd(en[i][..3], i + 1);
            map[it[i]] = i + 1;
            map.TryAdd(itShort[i], i + 1);
        }

        map["sept"] = 9;
        return map;
    }

    [GeneratedRegex(@"(?<d>\d{1,2})\s+(?<m>[A-Za-zÀ-ÿ]{3,10}\.?)\s+(?<y>\d{4})")]
    private static partial Regex DayMonthYear();

    [GeneratedRegex(@"(?<m>[A-Za-zÀ-ÿ]{3,10}\.?)\s+(?<d>\d{1,2}),?\s+(?<y>\d{4})")]
    private static partial Regex MonthDayYear();

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}\b|\b\d{1,2}[/.]\d{1,2}[/.]\d{2,4}\b")]
    private static partial Regex NumericDate();

    [GeneratedRegex(@"\d+(?:[.,]\d+)*")]
    private static partial Regex Number();

    [GeneratedRegex(@"%|\bCHF\b|\bEUR\b|\bUSD\b|€|\$|£|\bpercent\b|\bper cento\b", RegexOptions.IgnoreCase)]
    private static partial Regex Money();
}
