namespace Raffa.SharedKernel.Contracts;

/// <summary>Where a <see cref="DocumentedContractTerm"/> comes from.</summary>
public static class DocumentedTermSource
{
    /// <summary>The contract document, as read and validated.</summary>
    public const string Contract = "contract";

    /// <summary>The renewed terms recorded when a case closed as renewed (<c>contract_term_version</c>).</summary>
    public const string Renewal = "renewal";
}

/// <summary>
/// One term of a contract that a document states: its dates and its value, never a value Raffa
/// worked out. <see cref="AnnualValue"/> is the recurring annual value; <see cref="TotalValue"/>
/// the value over the whole term, used only when no annual value is stated.
/// </summary>
/// <param name="RecordedAt">When the term was recorded: on overlapping terms the later record wins.</param>
public sealed record DocumentedContractTerm(
    DateOnly From,
    DateOnly To,
    decimal? AnnualValue,
    decimal? TotalValue,
    string Currency,
    string Source,
    DateTimeOffset RecordedAt);

/// <summary>A contract's documented terms, oldest first, and how its priced lines are billed.</summary>
/// <param name="BillingPeriod">One of <see cref="BillingPeriods"/> when every priced line that
/// states a billing period agrees; null when none states one or they disagree.</param>
public sealed record ContractTermHistory(Guid ContractId, IReadOnlyList<DocumentedContractTerm> Terms, string? BillingPeriod);

/// <summary>
/// The documented term history of validated contracts (the original document, then each renewal
/// recorded on it). Implemented by <c>Raffa.Documents.Contracts</c>; read by Spesa e risparmi to put
/// a contract's value in the periods it covers. Tenant-scoped (RLS).
/// </summary>
public interface IContractTermHistoryReader
{
    /// <summary>The history of each of <paramref name="contractIds"/> that has one.</summary>
    Task<IReadOnlyDictionary<Guid, ContractTermHistory>> ListAsync(
        TenantId tenantId, IReadOnlyCollection<Guid> contractIds, CancellationToken cancellationToken = default);
}

/// <summary>The billing periods a contract's priced lines can state, and how many months each covers.</summary>
public static class BillingPeriods
{
    public const string Annual = "annual";
    public const string SemiAnnual = "semiannual";
    public const string Quarterly = "quarterly";
    public const string Monthly = "monthly";

    /// <summary>Months one invoice covers; null for an unknown period.</summary>
    public static int? Months(string? period) => period switch
    {
        Annual => 12,
        SemiAnnual => 6,
        Quarterly => 3,
        Monthly => 1,
        _ => null,
    };

    /// <summary>The period a line's free text states ("Annually", "per quarter", "monthly in
    /// advance"), or null when it states none of the four. Deliberately literal: a text it does not
    /// recognise stays unknown rather than being guessed.</summary>
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var s = text.Trim().ToLowerInvariant();
        // "every 12 months", "ogni 3 mesi": the number decides, not the word "month".
        var every = System.Text.RegularExpressions.Regex.Match(s, @"(\d{1,2})\s*(months?|mesi|mese)");
        if (every.Success)
        {
            return every.Groups[1].Value switch
            {
                "12" => Annual,
                "6" => SemiAnnual,
                "3" => Quarterly,
                "1" => Monthly,
                _ => null,
            };
        }

        if (s.Contains("semi", StringComparison.Ordinal) || s.Contains("half", StringComparison.Ordinal)
            || s.Contains("biannual", StringComparison.Ordinal) || s.Contains("bi-annual", StringComparison.Ordinal)
            || s.Contains("semestr", StringComparison.Ordinal))
        {
            return SemiAnnual;
        }

        if (s.Contains("quarter", StringComparison.Ordinal) || s.Contains("trimestr", StringComparison.Ordinal))
        {
            return Quarterly;
        }

        if (s.Contains("month", StringComparison.Ordinal) || s.Contains("mensil", StringComparison.Ordinal))
        {
            return Monthly;
        }

        if (s.Contains("annual", StringComparison.Ordinal) || s.Contains("year", StringComparison.Ordinal)
            || s.Contains("annu", StringComparison.Ordinal))
        {
            return Annual;
        }

        return null;
    }

    /// <summary>The one period every line that states a period agrees on; null when none states
    /// one or they disagree.</summary>
    public static string? Common(IEnumerable<string?> lineTexts)
    {
        ArgumentNullException.ThrowIfNull(lineTexts);
        var stated = lineTexts.Select(Normalize).Where(p => p is not null).Distinct(StringComparer.Ordinal).ToList();
        return stated.Count == 1 ? stated[0] : null;
    }
}
