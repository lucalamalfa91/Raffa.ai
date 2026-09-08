using System.Text.Json;
using Contigo.AiGateway.Contracts;

namespace Contigo.AiGateway.Foundry.Prompts;

/// <summary>
/// Versioned prompt + JSON schema for the `classify` role (ADR-004: fixed label set;
/// ADR-024: "classify is reused for the document admission gate and for the Ask domain gate with
/// fixed label sets"). The label list is generated from <see cref="AiDocumentType"/> itself, never
/// duplicated by hand, so the fixed set the model is constrained to always matches the enum this
/// gateway's own contract exposes.
/// </summary>
public static class ClassifyPromptTemplate
{
    /// <summary>Bump when the prompt or schema text below changes — recorded in every
    /// <see cref="AiCallMetadata.PromptVersion"/> this role produces.</summary>
    public const string Version = "foundry-classify-v1";

    private static readonly string[] Labels = Enum.GetNames<AiDocumentType>();

    public static readonly string SystemPrompt =
        "You classify a business document into exactly one of these types: " +
        string.Join(", ", Labels) +
        ". Use \"Other\" when the document does not match any other listed type. Base the " +
        "classification only on the given document text — never guess from the file name. " +
        "Respond with strict JSON matching the given schema only: no prose, no markdown fences.";

    /// <summary>
    /// <c>{ documentType: enum(AiDocumentType names), confidence: number[0,1] }</c>. Parsed once at
    /// type-initialization time and never disposed — the underlying <see cref="JsonDocument"/>
    /// stays reachable for the process lifetime because <see cref="Schema"/> (a
    /// <see cref="JsonElement"/>) keeps its own internal reference to it, the standard pattern for
    /// a long-lived constant parsed once (see <see cref="AnswerPersonaPrompt.Schema"/> for the same
    /// convention).
    /// </summary>
    public static readonly JsonElement Schema = JsonDocument.Parse(
        $$"""
        {
          "type": "object",
          "properties": {
            "documentType": { "type": "string", "enum": {{JsonSerializer.Serialize(Labels)}} },
            "confidence": { "type": "number", "minimum": 0, "maximum": 1 }
          },
          "required": ["documentType", "confidence"],
          "additionalProperties": false
        }
        """).RootElement;
}
