using Microsoft.EntityFrameworkCore;
using Raffa.Chat.Application.WebResearch;
using Raffa.Chat.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Chat.Tests.WebResearch;

/// <summary>
/// F3-T03 — the daily web-research budget reserves a call before the research call and hands it
/// back when that call fails for transport or configuration reasons. Postgres-backed (the
/// reservation is an atomic upsert): needs Docker or <c>RAFFA_TEST_POSTGRES_ADMIN</c>, see
/// <see cref="ChatPostgres"/>.
/// </summary>
public sealed class WebResearchBudgetTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    private ChatPostgres _postgres = null!;

    public async Task InitializeAsync() => _postgres = await ChatPostgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private WebResearchBudget NewBudget(TenantContext tenantContext, int limit, out Raffa.Chat.Infrastructure.ChatDbContext db, DateTimeOffset? now = null)
    {
        db = _postgres.CreateDbContext(tenantContext);
        return new WebResearchBudget(db, tenantContext, new FixedLimit(limit), new FixedClock(now ?? Now));
    }

    private async Task<int> CallsAsync(TenantContext tenantContext, TenantId tenant, DateOnly day)
    {
        await using var db = _postgres.CreateDbContext(tenantContext);
        using var scope = tenantContext.BeginScope(tenant);
        return await db.WebResearchUsage.AsNoTracking()
            .Where(u => u.TenantId == tenant && u.Day == day)
            .Select(u => u.Calls)
            .SingleOrDefaultAsync();
    }

    private static DateOnly Today => DateOnly.FromDateTime(Now.UtcDateTime);

    [Fact]
    public async Task A_reservation_takes_one_call_until_the_daily_limit_then_is_refused()
    {
        var tenantContext = new TenantContext();
        var tenant = TenantId.New();

        for (var i = 0; i < 3; i++)
        {
            var budget = NewBudget(tenantContext, 3, out var db);
            await using var _ = db;
            Assert.NotNull(await budget.TryReserveAsync(tenant));
        }

        var last = NewBudget(tenantContext, 3, out var lastDb);
        await using var __ = lastDb;
        Assert.Null(await last.TryReserveAsync(tenant));
        Assert.False(await last.HasRemainingAsync(tenant));
        Assert.Equal(3, await CallsAsync(tenantContext, tenant, Today));
    }

    [Fact]
    public async Task A_released_reservation_gives_the_call_back()
    {
        var tenantContext = new TenantContext();
        var tenant = TenantId.New();

        var first = NewBudget(tenantContext, 1, out var db1);
        await using var _ = db1;
        var reservation = await first.TryReserveAsync(tenant);
        Assert.NotNull(reservation);
        Assert.False(await first.HasRemainingAsync(tenant));

        await first.ReleaseAsync(reservation.Value);

        Assert.Equal(0, await CallsAsync(tenantContext, tenant, Today));
        Assert.True(await first.HasRemainingAsync(tenant));
        Assert.NotNull(await first.TryReserveAsync(tenant));
    }

    [Fact]
    public async Task Release_never_takes_the_counter_below_zero_and_is_a_no_op_without_a_row()
    {
        var tenantContext = new TenantContext();
        var tenant = TenantId.New();
        var budget = NewBudget(tenantContext, 5, out var db);
        await using var _ = db;

        // No row at all for this tenant/day.
        await budget.ReleaseAsync(new WebResearchReservation(tenant, Today));
        Assert.Equal(0, await CallsAsync(tenantContext, tenant, Today));

        var reservation = await budget.TryReserveAsync(tenant);
        await budget.ReleaseAsync(reservation!.Value);
        await budget.ReleaseAsync(reservation.Value);
        await budget.ReleaseAsync(reservation.Value);

        Assert.Equal(0, await CallsAsync(tenantContext, tenant, Today));
    }

    [Fact]
    public async Task A_release_after_midnight_lands_on_the_day_the_call_was_reserved()
    {
        var tenantContext = new TenantContext();
        var tenant = TenantId.New();
        var lateEvening = new DateTimeOffset(2026, 10, 8, 23, 59, 58, TimeSpan.Zero);
        var nextMorning = lateEvening.AddMinutes(2);

        var evening = NewBudget(tenantContext, 5, out var db1, lateEvening);
        await using var _ = db1;
        var reservation = await evening.TryReserveAsync(tenant);
        Assert.Equal(new DateOnly(2026, 10, 8), reservation!.Value.Day);

        // Today's own call, taken after midnight, must survive the release of yesterday's.
        var morning = NewBudget(tenantContext, 5, out var db2, nextMorning);
        await using var __ = db2;
        Assert.NotNull(await morning.TryReserveAsync(tenant));

        await morning.ReleaseAsync(reservation.Value);

        Assert.Equal(0, await CallsAsync(tenantContext, tenant, new DateOnly(2026, 10, 8)));
        Assert.Equal(1, await CallsAsync(tenantContext, tenant, new DateOnly(2026, 10, 9)));
    }

    [Fact]
    public async Task Concurrent_reservations_never_exceed_the_limit_and_releases_restore_exactly_what_was_given()
    {
        const int limit = 5;
        const int attempts = 24;
        var tenantContext = new TenantContext();
        var tenant = TenantId.New();

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, attempts)
            .Select(_ => Task.Run(async () =>
            {
                var budget = NewBudget(tenantContext, limit, out var db);
                await using var __ = db;
                await start.Task;
                return await budget.TryReserveAsync(tenant);
            }))
            .ToArray();
        start.SetResult();
        var reservations = (await Task.WhenAll(tasks)).Where(r => r is not null).Select(r => r!.Value).ToList();

        Assert.Equal(limit, reservations.Count);
        Assert.Equal(limit, await CallsAsync(tenantContext, tenant, Today));

        // Two of them fail upstream and are released: exactly two calls come back.
        var releaser = NewBudget(tenantContext, limit, out var releaseDb);
        await using var ___ = releaseDb;
        await releaser.ReleaseAsync(reservations[0]);
        await releaser.ReleaseAsync(reservations[1]);

        Assert.Equal(limit - 2, await CallsAsync(tenantContext, tenant, Today));
    }

    [Fact]
    public async Task A_zero_limit_closes_the_gate_and_reserves_nothing()
    {
        var tenantContext = new TenantContext();
        var tenant = TenantId.New();
        var budget = NewBudget(tenantContext, 0, out var db);
        await using var _ = db;

        Assert.Null(await budget.TryReserveAsync(tenant));
    }

    /// <summary>The one number the budget needs; the flow's options type is not this module's concern.</summary>
    private sealed class FixedLimit(int dailyCallsPerTenant) : IWebResearchBudgetLimit
    {
        public int DailyCallsPerTenant { get; } = dailyCallsPerTenant;
    }
}
