namespace Raffa.AiGateway.Configuration;

/// <summary>
/// One Foundry model binding for an AI Gateway role: which deployment (and model version) the
/// gateway calls for that role, plus the per-role request knobs the GPT-5.x family made
/// configuration-driven (ADR-004 amendment 2026-09-09). ADR-004 decision outcome: "each role is
/// bound to a configuration-selected model ID... model swap is config-only" — this record is that
/// binding. Bound from <c>AiGateway:Models:&lt;Role&gt;:*</c> (env var form
/// <c>AiGateway__Models__&lt;Role&gt;__ModelId</c> etc.), which Terraform publishes from the
/// deployment resources it creates (<c>infra/modules/foundry</c>).
/// </summary>
/// <param name="ModelId">Azure OpenAI <em>deployment</em> name (it is the path/body segment the
/// data plane routes on), or the Document Intelligence model id for the <c>ocr</c> role.</param>
/// <param name="ModelVersion">Model/deployment version string, recorded in every audit row.</param>
public sealed record AiModelSelection(string ModelId, string ModelVersion)
{
    /// <summary>
    /// Sampling temperature for a chat role, sent only when set. Clamped to
    /// <see cref="Foundry.FoundryChatCompletionsClient.MaxTemperature"/> (ADR-024: "temperature
    /// &lt;= 0.2") whenever it is sent. <see langword="null"/> (the default) omits the parameter:
    /// the GPT-5.x reasoning family rejects an explicit temperature unless reasoning is off, so the
    /// live probe of each deployment decides whether this knob is set at all.
    /// </summary>
    public double? Temperature { get; init; }

    /// <summary>
    /// GPT-5.x <c>reasoning_effort</c> for a chat role (<c>none</c>, <c>minimal</c>, <c>low</c>,
    /// <c>medium</c>, <c>high</c>), sent only when set.
    /// </summary>
    public string? ReasoningEffort { get; init; }

    /// <summary>
    /// <c>max_completion_tokens</c> for a chat role, sent only when set. A completion that stops
    /// at this limit (<c>finish_reason = length</c>) is a visible failure, never truncated JSON.
    /// </summary>
    public int? MaxCompletionTokens { get; init; }

    /// <summary>
    /// Requested vector width for the <c>embed</c> role (<c>dimensions</c> on text-embedding-3
    /// models), sent only when set. Defaults to <see cref="AiGatewayConstants.EmbeddingDimensions"/>
    /// so a wider model (text-embedding-3-large, 3072 natively) still fits the pgvector column.
    /// </summary>
    public int? Dimensions { get; init; }
}
