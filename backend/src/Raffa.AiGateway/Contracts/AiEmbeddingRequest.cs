namespace Raffa.AiGateway.Contracts;

/// <summary>Input to the `embed` role.</summary>
/// <param name="Text">Text chunk to embed (product spec §8.3: chunked contract sections/clauses).</param>
public sealed record AiEmbeddingRequest(string Text);
