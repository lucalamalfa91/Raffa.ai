using System.Globalization;
using System.Text.RegularExpressions;
using Raffa.AiFlows.Shared.Guards;
using Raffa.Documents.Contracts.Application.Extraction;

namespace Raffa.AiFlows.DocumentExtraction.Validation;

/// <summary>Stable field names the cross-stage rules read (the store's own field names).</summary>
internal static class RuleFields
{
    public const string Currency = "currency";
    public const string AnnualSpend = "annualSpend";
    public const string TotalContractValue = "totalContractValue";
    public const string StartDate = "startDate";
    public const string EndDate = "endDate";
    public const string EffectiveDate = "effectiveDate";
    public const string CancellationDeadline = "cancellationDeadline";
    public const string NoticePeriodDays = ExtractionFieldNames.NoticePeriodDays;
}

/// <summary>One broken consistency check: a stable <see cref="RuleId"/> (e.g.
/// <c>date-order.end-before-start</c>), the fields it involves, and a human-readable message.</summary>
public sealed record CrossStageViolation(string RuleId, IReadOnlyList<string> Fields, string Message);

public enum RuleStatus
{
    /// <summary>The rule could not be evaluated (a needed field is missing or unreadable).</summary>
    NotApplicable = 0,

    /// <summary>At least one check was evaluated and none was violated.</summary>
    Passed = 1,

    /// <summary>At least one check was violated.</summary>
    Violated = 2,
}

public sealed record RuleEvaluation(RuleStatus Status, IReadOnlyList<CrossStageViolation> Violations)
{
    public static RuleEvaluation NotApplicable { get; } = new(RuleStatus.NotApplicable, []);

    public static RuleEvaluation Passed { get; } = new(RuleStatus.Passed, []);

    public static RuleEvaluation Violated(IReadOnlyList<CrossStageViolation> violations) =>
        new(RuleStatus.Violated, violations);
}

/// <summary>A pure, deterministic consistency rule over the scalar facts of one extraction run.</summary>
public interface ICrossStageRule
{
    /// <summary>Stable rule name; the prefix of every violation id it raises.</summary>
    string RuleId { get; }

    RuleEvaluation Evaluate(ScalarFactSet facts);
}

/// <summary>
/// The scalar facts (metadata, commercial terms, dates) of one run, by field. Only a fact that names a field of
/// its own stage's allow-list and carries a value counts, exactly as the store reads them; a later fact for the
/// same field wins, as it does in the store.
/// </summary>
public sealed class ScalarFactSet
{
    private readonly Dictionary<string, ExtractedFact> _facts = new(StringComparer.Ordinal);

    public ScalarFactSet(IEnumerable<StageFacts.Scalars> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        foreach (var stage in stages)
        {
            var allowed = ExtractionStageFields.For(stage.Stage);
            foreach (var fact in stage.Facts)
            {
                if (fact.Field is not null && allowed.Contains(fact.Field, StringComparer.Ordinal)
                    && !string.IsNullOrWhiteSpace(fact.Value))
                {
                    _facts[fact.Field] = fact;
                }
            }
        }
    }

    /// <summary>Helper for tests and callers holding plain values: field/value pairs, blanks ignored.</summary>
    public static ScalarFactSet Of(params (string Field, string? Value)[] values)
    {
        var set = new ScalarFactSet([]);
        foreach (var (field, value) in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                set._facts[field] = new ExtractedFact(field, value, null, null, null);
            }
        }

        return set;
    }

    public string? Value(string field) => _facts.TryGetValue(field, out var fact) ? fact.Value!.Trim() : null;

    public bool TryDate(string field, out DateOnly date)
    {
        date = default;
        return Value(field) is { } raw
            && DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    public bool TryInt(string field, out int value)
    {
        value = 0;
        return Value(field) is { } raw
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>The contract currency stated by the metadata stage (upper-case ISO code), or null.</summary>
    public string? ContractCurrency => Value(RuleFields.Currency)?.ToUpperInvariant();

    /// <summary>Reads an amount with the codebase's locale-aware number reader. An ISO code or a currency
    /// symbol inside the value is taken as the amount's own currency; otherwise it is null.</summary>
    public bool TryAmount(string field, out decimal amount, out string? currency)
    {
        amount = 0m;
        currency = null;
        return Value(field) is { } raw && AmountReader.TryRead(raw, out amount, out currency);
    }
}

internal static partial class AmountReader
{
    private static readonly (string Symbol, string Code)[] Symbols = [("€", "EUR"), ("$", "USD"), ("£", "GBP")];

    [GeneratedRegex("(?<![A-Za-z])[A-Za-z]{3}(?![A-Za-z])")]
    private static partial Regex IsoCode();

    public static bool TryRead(string raw, out decimal amount, out string? currency)
    {
        currency = null;
        var text = raw;

        var iso = IsoCode().Match(text);
        if (iso.Success)
        {
            currency = iso.Value.ToUpperInvariant();
            text = text.Remove(iso.Index, iso.Length);
        }

        foreach (var (symbol, code) in Symbols)
        {
            if (text.Contains(symbol, StringComparison.Ordinal))
            {
                currency ??= code;
                text = text.Replace(symbol, string.Empty, StringComparison.Ordinal);
            }
        }

        return LocaleNumberParser.TryParsePackValue(text.Trim(), out amount);
    }
}

/// <summary>start &lt;= end, and signature (effective) date &lt;= start. Equal dates are consistent.</summary>
public sealed class DateOrderRule : ICrossStageRule
{
    public const string Id = "date-order";

    public string RuleId => Id;

    public RuleEvaluation Evaluate(ScalarFactSet facts)
    {
        var evaluated = false;
        var violations = new List<CrossStageViolation>();

        if (facts.TryDate(RuleFields.StartDate, out var start) && facts.TryDate(RuleFields.EndDate, out var end))
        {
            evaluated = true;
            if (end < start)
            {
                violations.Add(new CrossStageViolation(
                    $"{Id}.end-before-start", [RuleFields.StartDate, RuleFields.EndDate],
                    $"endDate {end:yyyy-MM-dd} is before startDate {start:yyyy-MM-dd}."));
            }
        }

        if (facts.TryDate(RuleFields.EffectiveDate, out var effective) && facts.TryDate(RuleFields.StartDate, out start))
        {
            evaluated = true;
            if (effective > start)
            {
                violations.Add(new CrossStageViolation(
                    $"{Id}.effective-after-start", [RuleFields.EffectiveDate, RuleFields.StartDate],
                    $"effectiveDate {effective:yyyy-MM-dd} is after startDate {start:yyyy-MM-dd}."));
            }
        }

        return !evaluated ? RuleEvaluation.NotApplicable
            : violations.Count == 0 ? RuleEvaluation.Passed
            : RuleEvaluation.Violated(violations);
    }
}

/// <summary>cancellationDeadline &lt;= endDate, and, when the notice period is known, cancellationDeadline ==
/// endDate - noticePeriodDays (F5-D08).</summary>
public sealed class DeadlineConsistencyRule : ICrossStageRule
{
    public const string Id = "deadline-consistency";

    public string RuleId => Id;

    public RuleEvaluation Evaluate(ScalarFactSet facts)
    {
        if (!facts.TryDate(RuleFields.CancellationDeadline, out var deadline) || !facts.TryDate(RuleFields.EndDate, out var end))
        {
            return RuleEvaluation.NotApplicable;
        }

        if (deadline > end)
        {
            return RuleEvaluation.Violated(
            [
                new CrossStageViolation(
                    $"{Id}.cancellation-after-end", [RuleFields.CancellationDeadline, RuleFields.EndDate],
                    $"cancellationDeadline {deadline:yyyy-MM-dd} is after endDate {end:yyyy-MM-dd}."),
            ]);
        }

        if (facts.TryInt(RuleFields.NoticePeriodDays, out var days) && days >= 0)
        {
            var expected = end.AddDays(-days);
            if (deadline != expected)
            {
                return RuleEvaluation.Violated(
                [
                    new CrossStageViolation(
                        $"{Id}.notice-period-mismatch",
                        [RuleFields.CancellationDeadline, RuleFields.EndDate, RuleFields.NoticePeriodDays],
                        $"cancellationDeadline {deadline:yyyy-MM-dd} is not endDate {end:yyyy-MM-dd} minus {days} days ({expected:yyyy-MM-dd})."),
                ]);
            }
        }

        return RuleEvaluation.Passed;
    }
}

/// <summary>totalContractValue &gt;= annualSpend when the term is at least one year. Amounts are read with the
/// locale-aware number reader; amounts in two different currencies are not comparable (not applicable).</summary>
public sealed class TcvVersusAnnualSpendRule : ICrossStageRule
{
    public const string Id = "tcv-vs-annual-spend";

    public string RuleId => Id;

    public RuleEvaluation Evaluate(ScalarFactSet facts)
    {
        if (!facts.TryAmount(RuleFields.AnnualSpend, out var annual, out var annualCurrency)
            || !facts.TryAmount(RuleFields.TotalContractValue, out var total, out var totalCurrency)
            || !facts.TryDate(RuleFields.StartDate, out var start)
            || !facts.TryDate(RuleFields.EndDate, out var end)
            || annual < 0m
            || total < 0m
            || end < start)
        {
            return RuleEvaluation.NotApplicable;
        }

        // An amount without its own currency is in the contract currency; two known, different currencies
        // cannot be compared. An unknown currency on either side is read leniently.
        var annualIn = annualCurrency ?? facts.ContractCurrency;
        var totalIn = totalCurrency ?? facts.ContractCurrency;
        if (annualIn is not null && totalIn is not null && !string.Equals(annualIn, totalIn, StringComparison.Ordinal))
        {
            return RuleEvaluation.NotApplicable;
        }

        // The end date is inclusive in contracts ("1 Jan - 31 Dec" is one year), hence AddDays(1).
        if (end.AddDays(1) < start.AddYears(1))
        {
            return RuleEvaluation.NotApplicable;
        }

        return total >= annual
            ? RuleEvaluation.Passed
            : RuleEvaluation.Violated(
            [
                new CrossStageViolation(
                    $"{Id}.tcv-below-annual-spend",
                    [RuleFields.TotalContractValue, RuleFields.AnnualSpend, RuleFields.StartDate, RuleFields.EndDate],
                    $"totalContractValue {total} is below annualSpend {annual} for a term of a year or more."),
            ]);
    }
}
