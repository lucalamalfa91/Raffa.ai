namespace Contigo.AiGateway.Contracts;

/// <summary>Token counts a provider reported for one call (Azure OpenAI <c>usage</c>).</summary>
/// <param name="PromptTokens">Input tokens billed for the call.</param>
/// <param name="CompletionTokens">Output tokens billed for the call (0 for embeddings).</param>
public sealed record AiTokenUsage(int PromptTokens, int CompletionTokens);
