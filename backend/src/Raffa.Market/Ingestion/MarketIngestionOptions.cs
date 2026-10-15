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
}
