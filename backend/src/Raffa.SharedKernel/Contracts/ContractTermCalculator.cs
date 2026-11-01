using System.Globalization;
using System.Text.RegularExpressions;

namespace Raffa.SharedKernel.Contracts;

/// <summary>Where a contract's term stands today (<see cref="EffectiveContractTerm.Status"/>).</summary>
public static class ContractTermStatus
{
    /// <summary>The notice deadline is today or later: nothing has happened by itself yet.</summary>
    public const string Active = "active";

    /// <summary>The notice deadline passed and the contract renews by itself: the term rolled forward.</summary>
    public const string RolledOver = "rolledOver";

    /// <summary>The notice deadline passed, no automatic renewal, the end date is still ahead.</summary>
    public const string Ending = "ending";

    /// <summary>No automatic renewal and the end date has passed.</summary>
    public const string Expired = "expired";

    /// <summary>The notice deadline passed, the contract renews by itself, but the renewal term is unknown.</summary>
    public const string NeedsReview = "needsReview";

    /// <summary>Neither an end date nor a notice deadline was read.</summary>
    public const string Unknown = "unknown";
}

/// <summary>The contract facts the term arithmetic needs.</summary>
/// <param name="CancellationDeadline">An explicit last day to give notice; else <c>End - NoticeDays</c>.</param>
public sealed record ContractTermInputs(
    DateOnly? Start,
    DateOnly? End,
    int? NoticeDays,
    bool AutoRenew,
    int? RenewalTermMonths,
    DateOnly? CancellationDeadline);

/// <summary>
/// The term a contract runs on today, computed deterministically from its validated facts (never by
/// a model). <see cref="Start"/>/<see cref="End"/>/<see cref="NoticeDeadline"/> are the effective
/// (possibly rolled-over) dates; the <c>Original*</c> ones are the dates as read from the contract.
/// </summary>
/// <param name="Cycles">How many times the term rolled forward by itself.</param>
/// <param name="RenewedOn">The day the current (rolled-over) term began: the previous end date. May be
/// after today when the notice passed but the old term has not ended yet.</param>
/// <param name="LockedOn">The last notice deadline that passed (when the renewal became binding).</param>
public sealed record EffectiveContractTerm(
    string Status,
    DateOnly Today,
    DateOnly? Start,
    DateOnly? End,
    DateOnly? NoticeDeadline,
    bool AutoRenew,
    int? RenewalTermMonths,
    int NoticePeriodDays,
    bool RolledOver,
    int Cycles,
    DateOnly? RenewedOn,
    DateOnly? LockedOn,
    DateOnly? OriginalEnd,
    DateOnly? OriginalNoticeDeadline)
{
    /// <summary>Days from today to the effective notice deadline (negative once it passed and nothing rolled).</summary>
    public int? NoticeDays => NoticeDeadline is { } d ? d.DayNumber - Today.DayNumber : null;

    /// <summary>Days from today to the effective end date.</summary>
    public int? EndDays => End is { } d ? d.DayNumber - Today.DayNumber : null;

    /// <summary>The notice deadline is still ahead (today included): a real, actionable deadline.</summary>
    public bool NoticeOpen => NoticeDays is >= 0;

    /// <summary>Days since the renewal took effect (negative while it is still ahead); null when nothing rolled.</summary>
    public int? DaysSinceRenewal => RenewedOn is { } d ? Today.DayNumber - d.DayNumber : null;
}

/// <summary>
/// The term roll-over arithmetic (AskRaffa 4.0 situation assessment, deterministic half): from the
/// validated start/end, notice period, auto-renewal, renewal term and today, the effective current
/// term. Pure and total: it never throws on missing facts, it reports them as a status.
/// </summary>
public static partial class ContractTermCalculator
{
    /// <summary>Safety cap: a 1-month term from 1900 would otherwise loop a long time.</summary>
    public const int MaxCycles = 600;

    public static EffectiveContractTerm Compute(ContractTermInputs input, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(input);
        var notice = Math.Max(0, input.NoticeDays ?? 0);
        var end = input.End;
        var deadline = input.CancellationDeadline ?? end?.AddDays(-notice);
        // Keep the contract's own gap between notice deadline and end across cycles (an explicit
        // cancellation deadline may not be exactly End - notice days).
        var gap = end is { } e && deadline is { } d ? e.DayNumber - d.DayNumber : notice;
        var months = input.RenewalTermMonths is > 0 ? input.RenewalTermMonths : null;

        EffectiveContractTerm Make(string status, DateOnly? start, DateOnly? newEnd, DateOnly? newDeadline, int cycles = 0, DateOnly? renewedOn = null, DateOnly? lockedOn = null) =>
            new(status, today, start, newEnd, newDeadline, input.AutoRenew, months, gap, cycles > 0, cycles, renewedOn, lockedOn, end, deadline);

        if (end is null && deadline is null)
        {
            return Make(ContractTermStatus.Unknown, input.Start, null, null);
        }

        if (deadline is { } open && open >= today)
        {
            return Make(ContractTermStatus.Active, input.Start, end, deadline);
        }

        if (!input.AutoRenew)
        {
            if (end is null)
            {
                return Make(ContractTermStatus.NeedsReview, input.Start, end, deadline);
            }

            return Make(end.Value >= today ? ContractTermStatus.Ending : ContractTermStatus.Expired, input.Start, end, deadline);
        }

        if (months is null || end is null)
        {
            return Make(ContractTermStatus.NeedsReview, input.Start, end, deadline);
        }

        var curStart = input.Start;
        var curEnd = end.Value;
        var curDeadline = deadline!.Value;
        var cycles = 0;
        DateOnly locked = curDeadline;
        while (curDeadline < today && cycles < MaxCycles)
        {
            locked = curDeadline;
            curStart = curEnd;
            curEnd = curEnd.AddMonths(months.Value);
            curDeadline = curEnd.AddDays(-gap);
            cycles++;
        }

        return Make(ContractTermStatus.RolledOver, curStart, curEnd, curDeadline, cycles, curStart, locked);
    }

    /// <summary>The effective term of validated facts (dates from the ISO fields, else the display strings).</summary>
    public static EffectiveContractTerm Compute(ContractFacts facts, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return Compute(Inputs(facts), today);
    }

    public static ContractTermInputs Inputs(ContractFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return new ContractTermInputs(
            facts.StartDate ?? ParseDate(facts.Start),
            facts.EndDate ?? ParseDate(facts.End),
            facts.Notice,
            IsAuto(facts.Auto),
            RenewalMonths(facts),
            facts.NoticeDeadline ?? ParseDate(facts.Cancel));
    }

    public static bool IsAuto(string? auto) =>
        string.Equals(auto, "Yes", StringComparison.OrdinalIgnoreCase) || string.Equals(auto, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>The renewal term in months: <see cref="ContractFacts.RenewalTermMonths"/>, else the
    /// "Renewal term" key term ("12 months, automatic", "1 year"); null when not stated or "None".</summary>
    public static int? RenewalMonths(ContractFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.RenewalTermMonths is > 0)
        {
            return facts.RenewalTermMonths;
        }

        var text = facts.Deep.Terms.FirstOrDefault(t => string.Equals(t.Label, "Renewal term", StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var m = MonthsPattern().Match(text);
        if (!m.Success)
        {
            return null;
        }

        var n = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var unit = m.Groups[2].Value.ToLowerInvariant();
        var result = unit.StartsWith("y", StringComparison.Ordinal) || unit.StartsWith("ann", StringComparison.Ordinal) ? n * 12 : n;
        return result > 0 ? result : null;
    }

    /// <summary>"18 Oct 2026" (the display form the facts carry).</summary>
    public static DateOnly? ParseDate(string? s) =>
        !string.IsNullOrWhiteSpace(s) && DateOnly.TryParseExact(s, "dd MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    [GeneratedRegex(@"(\d{1,3})\s*(months?|mesi|mese|years?|anni|anno)", RegexOptions.IgnoreCase)]
    private static partial Regex MonthsPattern();
}
