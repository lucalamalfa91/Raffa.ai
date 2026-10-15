using System.Text;
using Raffa.Market.Infrastructure;
using Raffa.Market.Infrastructure.Entities;
using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Raffa.Market.Ingestion;

/// <summary>
/// F7-T08: the lifecycle of the corpus versions (blue/green). A new version is created as
/// <see cref="MarketCorpusStates.Building"/> from a server-side copy of the active one, filled by the
/// ingestion job while readers keep seeing the active version, and made visible with
/// <see cref="SwitchAsync"/> -- one transaction that retires the old active and activates the new,
/// so a reader sees the old corpus or the new one, never a mix. The retired version stays until
/// <see cref="PurgeRetiredAsync"/>, which makes rollback the same atomic switch in the other direction.
/// </summary>
public sealed class MarketCorpusVersionService(MarketDbContext dbContext, IClock clock)
{
    /// <summary>The id of the active version; creates the initial one on a database that has none.</summary>
    public async Task<string> GetActiveVersionAsync(CancellationToken cancellationToken = default)
    {
        var active = await dbContext.CorpusVersions
            .AsNoTracking()
            .Where(v => v.State == MarketCorpusStates.Active)
            .Select(v => v.Version)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (active is not null)
        {
            return active;
        }

        // A database without the seeded row (never migrated through AddCorpusVersioning): make the
        // initial version exist so every row has a version to belong to.
        var now = clock.UtcNow;
        dbContext.CorpusVersions.Add(new MarketCorpusVersionEntity
        {
            Version = MarketCorpusVersions.Initial,
            State = MarketCorpusStates.Active,
            CreatedAt = now,
            ActivatedAt = now,
        });
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return MarketCorpusVersions.Initial;
    }

    /// <summary>All versions, newest first.</summary>
    public async Task<IReadOnlyList<MarketCorpusVersionEntity>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.CorpusVersions
            .AsNoTracking()
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// The version to build into: an unfinished <see cref="MarketCorpusStates.Building"/> one left by
    /// a crashed run (resumed, so nothing is rebuilt), otherwise a new one cloned from the active
    /// version -- records, payloads, hashes, filter columns and embeddings -- in one transaction.
    /// </summary>
    public async Task<string> BeginBuildAsync(string? feedVersion, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.CorpusVersions
            .Where(v => v.State == MarketCorpusStates.Building)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing.Version;
        }

        var active = await GetActiveVersionAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        var version = $"v{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}";

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        dbContext.CorpusVersions.Add(new MarketCorpusVersionEntity
        {
            Version = version,
            State = MarketCorpusStates.Building,
            CreatedAt = now,
            FeedVersion = feedVersion,
        });
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await CloneAsync<MarketRecordEntity>(active, version, null, cancellationToken).ConfigureAwait(false);
        await CloneAsync<MarketEmbeddingEntity>(
                active, version, nameof(MarketEmbeddingEntity.Id), cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return version;
    }

    /// <summary>
    /// Makes <paramref name="version"/> the active one, atomically: the current active is retired and
    /// <paramref name="version"/> activated in a single transaction (the partial unique index
    /// guarantees there is never more than one). Refuses an unknown or empty version.
    /// </summary>
    public async Task SwitchAsync(string version, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var target = await dbContext.CorpusVersions
            .SingleOrDefaultAsync(v => v.Version == version, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Corpus version '{version}' does not exist.");
        if (target.State == MarketCorpusStates.Active)
        {
            return;
        }

        var records = await dbContext.MarketRecords
            .IgnoreQueryFilters()
            .CountAsync(r => r.CorpusVersion == version, cancellationToken)
            .ConfigureAwait(false);
        if (records == 0)
        {
            throw new InvalidOperationException($"Corpus version '{version}' is empty; refusing to activate it.");
        }

        var now = clock.UtcNow;
        await dbContext.CorpusVersions
            .Where(v => v.State == MarketCorpusStates.Active)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.State, MarketCorpusStates.Retired), cancellationToken)
            .ConfigureAwait(false);
        await dbContext.CorpusVersions
            .Where(v => v.Version == version)
            .ExecuteUpdateAsync(
                s => s.SetProperty(v => v.State, MarketCorpusStates.Active).SetProperty(v => v.ActivatedAt, now),
                cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes retired versions beyond the newest <paramref name="keep"/> (records and, by cascade,
    /// their embeddings). Returns how many versions were removed.
    /// </summary>
    public async Task<int> PurgeRetiredAsync(int keep, CancellationToken cancellationToken = default)
    {
        var retired = await dbContext.CorpusVersions
            .Where(v => v.State == MarketCorpusStates.Retired)
            .OrderByDescending(v => v.ActivatedAt)
            .Select(v => v.Version)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var doomed = retired.Skip(Math.Max(0, keep)).ToList();
        foreach (var version in doomed)
        {
            await DeleteAsync(version, cancellationToken).ConfigureAwait(false);
        }

        return doomed.Count;
    }

    /// <summary>Deletes a non-active version and everything in it.</summary>
    public async Task DeleteAsync(string version, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var state = await dbContext.CorpusVersions
            .Where(v => v.Version == version)
            .Select(v => v.State)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (state == MarketCorpusStates.Active)
        {
            throw new InvalidOperationException("The active corpus version cannot be deleted.");
        }

        // Embeddings go with their records (ON DELETE CASCADE).
        await dbContext.MarketRecords
            .IgnoreQueryFilters()
            .Where(r => r.CorpusVersion == version)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        await dbContext.CorpusVersions
            .Where(v => v.Version == version)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>INSERT ... SELECT</c> of every row of <typeparamref name="T"/> in <paramref name="from"/> as
    /// <paramref name="to"/>. The column list comes from the EF model, so a column added later is
    /// cloned too; <paramref name="freshGuidProperty"/> gets a new uuid per row instead of a copy.
    /// </summary>
    private async Task CloneAsync<T>(string from, string to, string? freshGuidProperty, CancellationToken cancellationToken)
    {
        var entityType = dbContext.Model.FindEntityType(typeof(T))!;
        var table = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());

        var columns = new StringBuilder();
        var values = new StringBuilder();
        foreach (var property in entityType.GetProperties())
        {
            var column = property.GetColumnName(table)!;
            if (columns.Length > 0)
            {
                columns.Append(", ");
                values.Append(", ");
            }

            columns.Append('"').Append(column).Append('"');
            if (property.Name == nameof(MarketRecordEntity.CorpusVersion))
            {
                values.Append("{1}");
            }
            else if (property.Name == freshGuidProperty)
            {
                values.Append("gen_random_uuid()");
            }
            else
            {
                values.Append('"').Append(column).Append('"');
            }
        }

        var sql = $"INSERT INTO \"{table.Name}\" ({columns}) SELECT {values} FROM \"{table.Name}\" WHERE \"corpus_version\" = {{0}}";
#pragma warning disable EF1002 // Identifiers come from the EF model, values are parameters.
        await dbContext.Database.ExecuteSqlRawAsync(sql, [from, to], cancellationToken).ConfigureAwait(false);
#pragma warning restore EF1002
    }
}
