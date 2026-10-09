using Raffa.Chat.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Chat.Application.WebResearch;

/// <summary>
/// Gate 3 of ADR-030: at most <see cref="IWebResearchBudgetLimit.DailyCallsPerTenant"/> research calls
/// per tenant per UTC day. <see cref="TryReserveAsync"/> is one atomic conditional upsert
/// (<c>INSERT … ON CONFLICT DO UPDATE … WHERE calls &lt; limit</c>), so two concurrent turns can
/// never both take the last call; it runs inside the tenant's RLS scope like every other write in
/// this module. A call is reserved <em>before</em> the research call and handed back with
/// <see cref="ReleaseAsync"/> when that call fails for transport or configuration reasons (F3-T03),
/// so an outage never eats the tenant's day. <see cref="HasRemainingAsync"/> is the read the
/// interview uses to decide whether to offer the web at all — it never consumes.
/// </summary>
public interface IWebResearchBudget
{
    Task<bool> HasRemainingAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>Reserves one call for today, or <see langword="null"/> when the day's budget is gone
    /// (nothing is written in that case). The reservation remembers the UTC day it was taken on, so a
    /// release after midnight still lands on the right row.</summary>
    Task<WebResearchReservation?> TryReserveAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>Hands a reserved call back (never below zero). Releasing a reservation at most once
    /// is the caller's job.</summary>
    Task ReleaseAsync(WebResearchReservation reservation, CancellationToken cancellationToken = default);
}

/// <summary>One reserved research call: whose budget and which UTC day's row it was taken from.</summary>
public readonly record struct WebResearchReservation(TenantId TenantId, DateOnly Day);

public sealed class WebResearchBudget(
    ChatDbContext dbContext, ITenantContext tenantContext, IWebResearchBudgetLimit limitSource, IClock clock) : IWebResearchBudget
{
    public async Task<bool> HasRemainingAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        var limit = limitSource.DailyCallsPerTenant;
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

    /// <summary>Reserves one call for today; <see langword="null"/> when the day's budget is gone
    /// (nothing is written in that case).</summary>
    public async Task<WebResearchReservation?> TryReserveAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        var limit = limitSource.DailyCallsPerTenant;
        if (limit <= 0)
        {
            return null;
        }

        using var tenantScope = tenantContext.BeginScope(tenantId);
        var day = Today();
        var tenant = tenantId.Value;

        if (!dbContext.Database.IsRelational())
        {
            // The EF InMemory provider (API tests) has no SQL: a tracked read-modify-write is the
            // same counter without the atomic upsert, which only Postgres provides.
            return await TryReserveTrackedAsync(tenantId, day, limit, cancellationToken).ConfigureAwait(false);
        }

        int affected;
        try
        {
            // Command tag "INSERT 0 1" when the row was inserted or updated, "INSERT 0 0" when the
            // DO UPDATE's WHERE refused the increment — that count is the whole verdict.
            affected = await dbContext.Database
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
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Relational-specific methods can only be used", StringComparison.Ordinal))
        {
            // Some test hosts swap the DbContext late enough that the relational guard above still
            // reports true, but the eventual provider is EF InMemory and cannot execute SQL.
            return await TryReserveTrackedAsync(tenantId, day, limit, cancellationToken).ConfigureAwait(false);
        }

        return affected == 1 ? new WebResearchReservation(tenantId, day) : null;
    }

    /// <summary>Gives one reserved call back: one decrement on the reservation's own day, guarded so
    /// the counter never goes below zero. A missing row is a no-op.</summary>
    public async Task ReleaseAsync(WebResearchReservation reservation, CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(reservation.TenantId);
        var tenant = reservation.TenantId.Value;
        var day = reservation.Day;

        if (!dbContext.Database.IsRelational())
        {
            await ReleaseTrackedAsync(reservation, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await dbContext.Database
                .ExecuteSqlAsync(
                    $"""
                    UPDATE chat_web_research_usage
                    SET calls = calls - 1
                    WHERE tenant_id = {tenant} AND day = {day} AND calls > 0
                    """,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Relational-specific methods can only be used", StringComparison.Ordinal))
        {
            await ReleaseTrackedAsync(reservation, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReleaseTrackedAsync(WebResearchReservation reservation, CancellationToken cancellationToken)
    {
        var row = await dbContext.WebResearchUsage
            .SingleOrDefaultAsync(u => u.TenantId == reservation.TenantId && u.Day == reservation.Day, cancellationToken)
            .ConfigureAwait(false);

        if (row is { Calls: > 0 })
        {
            row.Calls--;
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<WebResearchReservation?> TryReserveTrackedAsync(
        TenantId tenantId, DateOnly day, int limit, CancellationToken cancellationToken)
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
            return null;
        }
        else
        {
            row.Calls++;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new WebResearchReservation(tenantId, day);
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
}
