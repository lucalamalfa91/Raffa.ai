using Microsoft.Extensions.Options;

namespace Raffa.Chat.Application.Gaps;

/// <summary>An <see cref="IOptionsMonitor{TOptions}"/> over one fixed <see cref="GapInvestigationOptions"/>
/// instance — for unit tests and tools that build the investigator or the dispatcher by hand.
/// <see cref="Set"/> swaps the instance, which is what a configuration reload does in the host, so
/// a test can prove that a mode or kill-switch change applies without a restart.</summary>
public sealed class StaticGapInvestigationOptions : IOptionsMonitor<GapInvestigationOptions>
{
    private GapInvestigationOptions _current;

    public StaticGapInvestigationOptions(GapInvestigationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _current = options;
    }

    public GapInvestigationOptions CurrentValue => Volatile.Read(ref _current);

    public static IOptionsMonitor<GapInvestigationOptions> Monitor(GapInvestigationOptions options) => new StaticGapInvestigationOptions(options);

    /// <summary>Replaces the instance every later <see cref="CurrentValue"/> read returns.</summary>
    public void Set(GapInvestigationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Volatile.Write(ref _current, options);
    }

    public GapInvestigationOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<GapInvestigationOptions, string?> listener) => null;
}
