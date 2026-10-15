using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Raffa.AiFlows.WebResearch.Configuration;

/// <summary>
/// F3-T10: <see cref="IOptionsMonitor{TOptions}"/> over <see cref="WebResearchOptions"/>, so the
/// kill switch (and the budget and bounds) change without a restart. The value returned is the one
/// <see cref="WebResearchOptions"/> singleton the host (or a test) registered: when the configuration
/// reloads, the <c>Chat:WebResearch</c> section is bound to a fresh default instance (a key removed from
/// the configuration goes back to its default, so deleting <c>Enabled</c> closes the gate) and copied
/// onto that singleton in place. Everything that holds the singleton directly — the daily budget
/// (<see cref="Raffa.Chat.Application.WebResearch.IWebResearchBudgetLimit"/>) and the workspace
/// settings endpoint — therefore sees the same live values as the flows reading this monitor. Without
/// an <see cref="IConfiguration"/> (unit tests, tools) it never changes by itself, like a static monitor.
/// </summary>
public sealed class WebResearchOptionsMonitor : IOptionsMonitor<WebResearchOptions>, IDisposable
{
    private readonly WebResearchOptions _live;
    private readonly IConfiguration? _configuration;
    private readonly IDisposable? _registration;
    private readonly object _gate = new();
    private readonly List<Action<WebResearchOptions, string?>> _listeners = [];

    public WebResearchOptionsMonitor(WebResearchOptions live, IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(live);
        _live = live;
        _configuration = configuration;
        if (configuration is not null)
        {
            _registration = ChangeToken.OnChange(configuration.GetReloadToken, Reload);
        }
    }

    /// <summary>A fixed options instance (unit tests, tools): never re-read.</summary>
    public static IOptionsMonitor<WebResearchOptions> Static(WebResearchOptions options) =>
        new WebResearchOptionsMonitor(options);

    public WebResearchOptions CurrentValue => _live;

    public WebResearchOptions Get(string? name) => _live;

    public IDisposable? OnChange(Action<WebResearchOptions, string?> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        lock (_gate)
        {
            _listeners.Add(listener);
        }

        return new Subscription(this, listener);
    }

    /// <summary>Re-reads the section now. Public so a host can force it; also what the configuration's
    /// reload token calls.</summary>
    public void Reload()
    {
        if (_configuration is null)
        {
            return;
        }

        var fresh = new WebResearchOptions();
        _configuration.GetSection(WebResearchOptions.SectionName).Bind(fresh);
        _live.CopyFrom(fresh);

        Action<WebResearchOptions, string?>[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _listeners];
        }

        foreach (var listener in snapshot)
        {
            listener(_live, null);
        }
    }

    public void Dispose() => _registration?.Dispose();

    private sealed class Subscription(WebResearchOptionsMonitor owner, Action<WebResearchOptions, string?> listener) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._gate)
            {
                owner._listeners.Remove(listener);
            }
        }
    }
}
