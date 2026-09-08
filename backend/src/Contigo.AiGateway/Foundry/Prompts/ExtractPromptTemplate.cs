namespace Contigo.AiGateway.Foundry.Prompts;

/// <summary>
/// Versioned prompt wrapper for the `extract` role. Unlike <see cref="ClassifyPromptTemplate"/>/
/// <see cref="AnswerPersonaPrompt"/>, `extract` has no fixed schema of its own — the caller
/// supplies one per stage (<c>AiExtractionRequest.JsonSchema</c>; ADR-004: "the gateway does not
/// know or validate the domain schema") — so this type owns only the system-prompt wording and the
/// version tag, parameterized by the caller's own stage name for a slightly more targeted prompt.
/// </summary>
public static class ExtractPromptTemplate
{
    /// <summary>Bump when the prompt wording below changes.</summary>
    public const string Version = "foundry-extract-v1";

    public static string SystemPrompt(string stageName) =>
        $"You extract structured facts for the '{stageName}' stage of a contract review " +
        "pipeline, from the given document text only. Extract only what the text actually " +
        "supports — never invent a value, a source page, or a confidence score. When a fact is " +
        "not present, omit it rather than guessing. Respond with strict JSON matching the given " +
        "schema only: no prose, no markdown fences.";
}
