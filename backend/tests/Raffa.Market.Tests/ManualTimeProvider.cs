namespace Raffa.Market.Tests;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock only moves when a test says so: timers created through
/// it (and so <c>Task.Delay(delay, timeProvider)</c>) fire during <see cref="Advance"/>.
/// </summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Number of timers that are armed and have not fired yet.</summary>
    public int PendingTimers
    {
        get
        {
            lock (_timers)
            {
                return _timers.Count(t => t.Armed);
            }
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_timers)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    public void Advance(TimeSpan by)
    {
        _now += by;
        ManualTimer[] due;
        lock (_timers)
        {
            due = _timers.Where(t => t.Armed && t.DueAt <= _now).ToArray();
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public bool Armed { get; private set; }

        public DateTimeOffset DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Armed = dueTime != Timeout.InfiniteTimeSpan;
            DueAt = Armed ? owner._now + dueTime : default;
            return true;
        }

        public void Fire()
        {
            Armed = false;
            callback(state);
        }

        public void Dispose() => Armed = false;

        public ValueTask DisposeAsync()
        {
            Armed = false;
            return ValueTask.CompletedTask;
        }
    }
}
