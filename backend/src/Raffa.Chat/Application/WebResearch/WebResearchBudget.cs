using Raffa.Chat.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Chat.Application.WebResearch;

/// <summary>
/// Gate 3 of ADR-030: at most <see cref="WebResearchOptions.DailyCallsPerTenant"/> research calls
/// per tenant per UTC day. <see cref="TryConsumeAsync"/> is one atomic conditional upsert
/// (<c>INSERT … ON CONFLICT DO UPDATE … WHERE calls &lt; limit</c>), so two concurrent turns can
/// never both take the last call; it runs inside the tenant's RLS scope like every other write in
/// this module. <see cref="HasRemainingAsync"/> is the read the interview uses to decide whether
/// to offer the web at all — it never consumes.
/// </summary>
public interface IWebResearchBudget
{
    Task<bool> HasRemainingAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    Task<bool> TryConsumeAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

public sealed class WebResearchBudget(
    ChatDbContext dbContext, ITenantContext tenantContext, WebResearchOptions options, IClock clock) : IWebResearchBudget
{
    public async Task<bool> HasRemainingAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        var limit = options.DailyCallsPerTenant;
        if (limit <= 0)
        {
            return false;
        }

        using var tenantScope = tenantContext.BeginScope(tenantId);
        var day = Today();

        var calls = await dbContext.WebResearchUsage
            .AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.Day == day)
            .Select(u => (int?)u.Calls)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return (calls ?? 0) < limit;
    }

    /// <summary>Spends one call for today; <see langword="false"/> when the day's budget is gone
    /// (nothing is written in that case).</summary>
    public async Task<bool> TryConsumeAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        var limit = options.DailyCallsPerTenant;
        if (limit <= 0)
        {
            return false;
        }

        using var tenantScope = tenantContext.BeginScope(tenantId);
        var day = Today();
        var tenant = tenantId.Value;

        if (!dbContext.Database.IsRelational())
        {
            // The EF InMemory provider (API tests) has no SQL: a tracked read-modify-write is the
            // same counter without the atomic upsert, which only Postgres provides.
            return await TryConsumeTrackedAsync(tenantId, day, limit, cancellationToken).ConfigureAwait(false);
        }

        // Command tag "INSERT 0 1" when the row was inserted or updated, "INSERT 0 0" when the
        // DO UPDATE's WHERE refused the increment — that count is the whole verdict.
        var affected = await dbContext.Database
            .ExecuteSqlAsync(
                $"""
                INSERT INTO chat_web_research_usage (tenant_id, day, calls)
                VALUES ({tenant}, {day}, 1)
                ON CONFLICT (tenant_id, day) DO UPDATE
                SET calls = chat_web_research_usage.calls + 1
                WHERE chat_web_research_usage.calls < {limit}
                """,
                cancellationToken)
            .ConfigureAwait(false);

        return affected == 1;
    }

    private async Task<bool> TryConsumeTrackedAsync(TenantId tenantId, DateOnly day, int limit, CancellationToken cancellationToken)
    {
        var row = await dbContext.WebResearchUsage
            .SingleOrDefaultAsync(u => u.TenantId == tenantId && u.Day == day, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            dbContext.WebResearchUsage.Add(new Domain.WebResearch.WebResearchUsage { TenantId = tenantId, Day = day, Calls = 1 });
        }
        else if (row.Calls >= limit)
        {
            return false;
        }
        else
        {
            row.Calls++;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
}
