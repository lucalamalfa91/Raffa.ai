using Microsoft.EntityFrameworkCore;

namespace Raffa.Identity.Workspace.Infrastructure;

/// <summary>Unmapped row of <c>fx_reference_rate</c> (snake_case aliases, as for the platform rows).</summary>
public sealed class FxReferenceRateRow
{
    public string Currency { get; set; } = string.Empty;
    public decimal PerEur { get; set; }
    public DateOnly RateDate { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}

/// <summary>
/// The latest official reference rates: units of each currency per one euro (the euro itself is 1),
/// the day they were published and when Raffa last downloaded them. Empty when none were ever stored.
/// </summary>
public sealed record FxReferenceRates(
    IReadOnlyDictionary<string, decimal> PerEur,
    DateOnly? RateDate,
    DateTimeOffset? FetchedAt)
{
    public const string Euro = "EUR";

    public static FxReferenceRates Empty { get; } = new(new Dictionary<string, decimal>(StringComparer.Ordinal), null, null);

    /// <summary>
    /// Units of <paramref name="reportingCurrency"/> per one unit of each other currency known here, crossed
    /// through the euro: <c>perEur[reporting] / perEur[other]</c>. Empty when the reporting currency itself
    /// has no reference rate (it cannot be crossed).
    /// </summary>
    public IReadOnlyDictionary<string, decimal> RatesInto(string reportingCurrency)
    {
        var target = reportingCurrency.Trim().ToUpperInvariant();
        var all = new Dictionary<string, decimal>(PerEur, StringComparer.Ordinal);
        if (all.Count > 0)
        {
            all[Euro] = 1m;
        }

        if (!all.TryGetValue(target, out var targetPerEur) || targetPerEur <= 0)
        {
            return new Dictionary<string, decimal>(StringComparer.Ordinal);
        }

        return all
            .Where(kv => kv.Key != target && kv.Value > 0)
            .ToDictionary(kv => kv.Key, kv => Math.Round(targetPerEur / kv.Value, 8, MidpointRounding.ToEven), StringComparer.Ordinal);
    }
}

/// <summary>
/// Reads and replaces the shared <c>fx_reference_rate</c> table (see the <c>AddFxReferenceRates</c>
/// migration). Platform-level public data: no tenant scope, no RLS. On a non-Postgres provider (the
/// in-memory test hosts) reads return <see cref="FxReferenceRates.Empty"/> and writes do nothing.
/// </summary>
public sealed class FxReferenceRateStore(IdentityWorkspaceDbContext db)
{
    private bool IsPostgres => db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>True when this store reads and writes a real table.</summary>
    public bool IsAvailable => IsPostgres;

    public async Task<FxReferenceRates> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        if (!IsPostgres)
        {
            return FxReferenceRates.Empty;
        }

        var rows = await db.Database
            .SqlQueryRaw<FxReferenceRateRow>(
                "SELECT currency AS \"currency\", per_eur AS \"per_eur\", rate_date AS \"rate_date\", fetched_at AS \"fetched_at\" FROM fx_reference_rate")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return FxReferenceRates.Empty;
        }

        return new FxReferenceRates(
            rows.Where(r => r.PerEur > 0).ToDictionary(r => r.Currency.Trim().ToUpperInvariant(), r => r.PerEur, StringComparer.Ordinal),
            rows.Max(r => r.RateDate),
            rows.Max(r => r.FetchedAt));
    }

    /// <summary>
    /// Stores <paramref name="perEur"/> as the rates of <paramref name="rateDate"/> (one upsert per currency,
    /// in one transaction). A currency the source no longer publishes keeps its last rate and date.
    /// </summary>
    public async Task SaveAsync(
        DateOnly rateDate, IReadOnlyDictionary<string, decimal> perEur, string source, DateTimeOffset fetchedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(perEur);
        if (!IsPostgres || perEur.Count == 0)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var (rawCode, rate) in perEur)
        {
            var code = rawCode.Trim().ToUpperInvariant();
            if (code.Length != 3 || rate <= 0 || code == FxReferenceRates.Euro)
            {
                continue;
            }

            await db.Database.ExecuteSqlAsync(
                    $"""
                    INSERT INTO fx_reference_rate (currency, per_eur, rate_date, source, fetched_at)
                    VALUES ({code}, {rate}, {rateDate}, {source}, {fetchedAt})
                    ON CONFLICT (currency) DO UPDATE
                        SET per_eur = EXCLUDED.per_eur, rate_date = EXCLUDED.rate_date,
                            source = EXCLUDED.source, fetched_at = EXCLUDED.fetched_at
                    WHERE fx_reference_rate.rate_date <= EXCLUDED.rate_date
                    """,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
