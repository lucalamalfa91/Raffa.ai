namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// <c>Market:Pricing</c>: how the per-line market comparison is kept ready (<see cref="LineItemMarketPriceService"/>).
///
/// <para>
/// A contract read used to wait for any due re-pricing to finish, including a model call that can take
/// tens of seconds. Two things take that wait off the read path without changing what a read returns once
/// the comparison is ready: the re-pricing is started ahead of the read (a job for the workspace, queued
/// when someone enters it), and a read that still finds a re-pricing due waits only
/// <see cref="ReadWaitBudgetMs"/> for it, then answers with the comparison already stored while the
/// re-pricing finishes in the background.
/// </para>
///
/// <para>
/// Every default is the intended production value. <c>Market__Pricing__ReadWaitBudgetMs=-1</c> restores the
/// previous behaviour exactly (a read waits for the whole re-pricing), and
/// <c>Market__Pricing__PrewarmOnWorkspaceEntry=false</c> stops the job being queued.
/// </para>
/// </summary>
public sealed class MarketPricingOptions
{
    public const string SectionName = "Market:Pricing";

    /// <summary>The <see cref="ReadWaitBudgetMs"/> value that means "wait for the whole re-pricing".</summary>
    public const int UnboundedReadWait = -1;

    /// <summary>
    /// Milliseconds a read waits for a due re-pricing before answering with the stored comparison.
    /// <c>-1</c> waits for it to finish (the previous behaviour); <c>0</c> never waits. Default 2000, which
    /// the matching against the cached market corpus always fits; only a model estimate can exceed it.
    /// </summary>
    public int ReadWaitBudgetMs { get; set; } = 2000;

    /// <summary>Whether entering a workspace queues a job that re-prices its due contracts ahead of any read.</summary>
    public bool PrewarmOnWorkspaceEntry { get; set; } = true;

    /// <summary>Least minutes between two pre-warm requests for the same workspace, per API process.</summary>
    public int PrewarmMinIntervalMinutes { get; set; } = 10;

    /// <summary>Most contracts one pre-warm job re-prices; whatever is left is picked up by the next one.</summary>
    public int MaxContractsPerJob { get; set; } = 200;

    /// <summary>Hard stop, in seconds, for a re-pricing that outlived the read that started it.</summary>
    public int DetachedRefreshTimeoutSeconds { get; set; } = 300;
}
