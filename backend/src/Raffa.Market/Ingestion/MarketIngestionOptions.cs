namespace Raffa.Market.Ingestion;

/// <summary>
/// Knobs of the market ingestion job (T-05 / F7-T03). Bound from <c>Market:Ingestion</c>. Every
/// default reproduces the behaviour before batching existed: one embed call per changed record, no
/// rate limit, a single <c>SaveChanges</c> at the end of the run.
/// </summary>
public sealed class MarketIngestionOptions
{
    /// <summary>Configuration section these options bind from.</summary>
    public const string SectionName = "Market:Ingestion";

    /// <summary>
    /// How many changed records are embedded per gateway call. <c>1</c> (the default) keeps today's
    /// one-call-per-record behaviour through <c>IAiGateway.EmbedAsync</c>; a larger value uses
    /// <c>IAiGateway.EmbedBatchAsync</c> (Azure OpenAI accepts up to 2,048 inputs per call).
    /// </summary>
    public int EmbedBatchSize { get; set; } = 1;

    /// <summary>
    /// Token budget per rolling minute for the embed deployment (TPM). <c>0</c> (the default) means
    /// no limiter. When set, the job waits before a call that would exceed the budget instead of
    /// being throttled by the provider.
    /// </summary>
    public int EmbedTokensPerMinute { get; set; }

    /// <summary>
    /// Commit after every embedding batch instead of once at the end of the run, so a crash keeps
    /// the batches already embedded and the next run (idempotent) only does what is left. Off by
    /// default: a failed run then persists nothing, as before.
    /// </summary>
    public bool CheckpointPerBatch { get; set; }

    /// <summary>
    /// F7-T08: build the corpus as a new version next to the live one and switch to it atomically at
    /// the end (blue/green), instead of updating the live corpus in place. The new version is a
    /// server-side copy of the active one, updated to match the feed, so records absent from the feed
    /// disappear with the switch and embeddings are only paid for what changed. A run that changes
    /// nothing discards its copy. Off by default: in-place ingestion, as before.
    /// </summary>
    public bool BlueGreen { get; set; }

    /// <summary>How many retired corpus versions are kept for rollback after a switch (blue/green only).</summary>
    public int RetiredVersionsToKeep { get; set; } = 1;
}
