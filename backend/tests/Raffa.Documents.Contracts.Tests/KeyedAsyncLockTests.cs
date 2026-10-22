using Raffa.Documents.Contracts.Application;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// The per-contract lock behind <see cref="LineItemMarketPriceService"/>'s single-flight refresh:
/// mutual exclusion per key, independence between keys, cancellation of a waiter, and no entry left
/// behind. Pure in-memory, no database.
/// </summary>
public sealed class KeyedAsyncLockTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(150);

    [Fact]
    public async Task A_second_caller_for_the_same_key_waits_until_the_first_releases()
    {
        var locks = new KeyedAsyncLock<string>();
        var first = await locks.AcquireAsync("contract-1", CancellationToken.None);

        var second = locks.AcquireAsync("contract-1", CancellationToken.None);
        await Task.Delay(Short);
        Assert.False(second.IsCompleted);

        first.Dispose();
        using var held = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, locks.ActiveKeys);
    }

    [Fact]
    public async Task Different_keys_do_not_block_each_other()
    {
        var locks = new KeyedAsyncLock<(Guid, Guid)>();
        var tenant = Guid.NewGuid();
        using var first = await locks.AcquireAsync((tenant, Guid.NewGuid()), CancellationToken.None);

        using var other = await locks.AcquireAsync((tenant, Guid.NewGuid()), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, locks.ActiveKeys);
    }

    [Fact]
    public async Task Entries_are_dropped_when_the_last_holder_leaves()
    {
        var locks = new KeyedAsyncLock<int>();
        var a = await locks.AcquireAsync(1, CancellationToken.None);
        var b = locks.AcquireAsync(1, CancellationToken.None);
        var c = await locks.AcquireAsync(2, CancellationToken.None);

        a.Dispose();
        var bHeld = await b;
        Assert.Equal(2, locks.ActiveKeys);

        bHeld.Dispose();
        c.Dispose();
        Assert.Equal(0, locks.ActiveKeys);
    }

    [Fact]
    public async Task A_cancelled_waiter_gives_its_place_back_and_leaves_nothing_behind()
    {
        var locks = new KeyedAsyncLock<string>();
        var holder = await locks.AcquireAsync("k", CancellationToken.None);
        using var cts = new CancellationTokenSource();
        var waiter = locks.AcquireAsync("k", cts.Token);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);

        // The holder is unaffected, and a later caller is not stuck behind the cancelled one.
        holder.Dispose();
        using var next = await locks.AcquireAsync("k", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, locks.ActiveKeys);
    }

    [Fact]
    public async Task Disposing_twice_releases_once()
    {
        var locks = new KeyedAsyncLock<string>();
        var first = await locks.AcquireAsync("k", CancellationToken.None);
        var second = locks.AcquireAsync("k", CancellationToken.None);

        first.Dispose();
        first.Dispose();
        var held = await second.WaitAsync(TimeSpan.FromSeconds(5));
        var third = locks.AcquireAsync("k", CancellationToken.None);
        await Task.Delay(Short);

        // The double Dispose did not hand the lock to the third caller while the second still holds it.
        Assert.False(third.IsCompleted);
        held.Dispose();
        using var thirdHeld = await third.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Concurrent_callers_for_one_key_never_overlap()
    {
        var locks = new KeyedAsyncLock<string>();
        var inside = 0;
        var maxInside = 0;

        await Task.WhenAll(Enumerable.Range(0, 40).Select(async _ =>
        {
            using var held = await locks.AcquireAsync("contract", CancellationToken.None);
            var now = Interlocked.Increment(ref inside);
            maxInside = Math.Max(maxInside, now);
            await Task.Yield();
            Interlocked.Decrement(ref inside);
        }));

        Assert.Equal(1, maxInside);
        Assert.Equal(0, locks.ActiveKeys);
    }
}
