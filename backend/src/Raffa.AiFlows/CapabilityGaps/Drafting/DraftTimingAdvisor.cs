namespace Raffa.AiFlows.CapabilityGaps.Drafting;

/// <summary>How much of the notice window a contract has left (plan F4-D09).</summary>
public enum DraftWindow
{
    /// <summary>No notice deadline is known (and the contract has not ended).</summary>
    Unknown,

    /// <summary>More than <see cref="DraftTimingAdvisor.UrgentDays"/> days to the deadline.</summary>
    Open,

    /// <summary>The deadline is within <see cref="DraftTimingAdvisor.UrgentDays"/> days.</summary>
    Urgent,

    /// <summary>The deadline is today.</summary>
    LastDay,

    /// <summary>The deadline (or, without one, the end date) is behind us.</summary>
    Passed,
}

/// <summary>The timing rule's reading of a contract: the window, the days left (negative once passed;
/// <see langword="null"/> without a deadline) and the communication it suggests.</summary>
public sealed record DraftTimingAdvice(DraftWindow Window, int? DaysToDeadline, DraftType SuggestedType);

/// <summary>
/// The timing rule from dates to the type of communication (plan F4-D09), pure and without a clock of its
/// own (the caller passes today). It only suggests a type: the user's own words
/// (<see cref="DraftTypeClassifier"/>) always win.
/// <list type="bullet">
/// <item><b>Open</b> (more than <see cref="UrgentDays"/> days to the notice deadline): there is time to
/// negotiate, so <see cref="DraftType.Renegotiation"/>.</item>
/// <item><b>Urgent / last day</b>: the only lever left is the exit, and the right to leave is lost
/// if the deadline passes, so <see cref="DraftType.CancellationNonRenewal"/> first (negotiation can go on
/// beside it).</item>
/// <item><b>Passed</b>: a non-renewal notice can no longer be given in time, so
/// <see cref="DraftType.Renegotiation"/> is the only communication left to make.</item>
/// <item><b>Unknown</b>: <see cref="DraftType.Renegotiation"/>, the default of every draft.</item>
/// </list>
/// </summary>
public static class DraftTimingAdvisor
{
    /// <summary>Days to the deadline at or below which the window is <see cref="DraftWindow.Urgent"/>.</summary>
    public const int UrgentDays = 30;

    public static DraftTimingAdvice Advise(DateOnly today, DateOnly? noticeDeadline, DateOnly? endDate)
    {
        if (noticeDeadline is not { } deadline)
        {
            return endDate is { } end && end < today
                ? new DraftTimingAdvice(DraftWindow.Passed, null, DraftType.Renegotiation)
                : new DraftTimingAdvice(DraftWindow.Unknown, null, DraftType.Renegotiation);
        }

        var days = deadline.DayNumber - today.DayNumber;
        return days switch
        {
            < 0 => new DraftTimingAdvice(DraftWindow.Passed, days, DraftType.Renegotiation),
            0 => new DraftTimingAdvice(DraftWindow.LastDay, days, DraftType.CancellationNonRenewal),
            <= UrgentDays => new DraftTimingAdvice(DraftWindow.Urgent, days, DraftType.CancellationNonRenewal),
            _ => new DraftTimingAdvice(DraftWindow.Open, days, DraftType.Renegotiation),
        };
    }
}
