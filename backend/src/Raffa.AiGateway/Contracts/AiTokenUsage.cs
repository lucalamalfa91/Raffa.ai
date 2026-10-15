namespace Raffa.AiGateway.Contracts;

/// <summary>Token counts a provider reported for one call (Azure OpenAI <c>usage</c>).</summary>
/// <param name="PromptTokens">Input tokens billed for the call.</param>
/// <param name="CompletionTokens">Output tokens billed for the call (0 for embeddings).</param>
/// <param name="CachedTokens">F5-T03: of <paramref name="PromptTokens"/>, how many the provider served from its
/// prompt cache (<c>usage.prompt_tokens_details.cached_tokens</c>); 0 when it reports none.</param>
public sealed record AiTokenUsage(int PromptTokens, int CompletionTokens, int CachedTokens = 0);
