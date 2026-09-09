namespace Contigo.AiGateway.Configuration;

/// <summary>
/// Config-selected model ids for the four roles this task implements (ADR-004 AC-1: "Gateway
/// exposes ... interfaces (config-selected model IDs)"). A composition root binds this from
/// configuration, e.g. <c>configuration.GetSection(AiGatewayModelOptions.SectionName).Bind(options)</c>
/// — no host does that yet (nothing in this wave constructs the AI Gateway via a DI container),
/// so <see cref="SectionName"/> is recorded here as the one canonical section path for whichever
/// task wires it (task E02/F01/US02/T01, staged extraction, is the first caller).
///
/// Property values default to ADR-004's own candidate table — "cheapest model that meets the
/// role" placeholders, explicitly NOT final selections (ADR-004 "Assumptions": "candidate names
/// ... are placeholders, not final selections"). Exact ids and per-1k-token prices in the target
/// region (North Europe, ADR-006) are cloud-architect's lane to confirm before the demo-wiring
/// task; until then these defaults keep the gateway usable out of the box, matching ADR-004's own
/// "Implications for the decomposition": "a fixture gateway adapter satisfies R0 scaffolding".
/// A config value always overrides these defaults — nothing here is hard-coded into gateway
/// *logic*, only into the options object's own defaults, which is what "defaulting to the
/// cheapest model" in ADR-004's decision outcome describes.
/// </summary>
public sealed class AiGatewayModelOptions
{
    /// <summary>Conventional configuration section path for binding this options object.</summary>
    public const string SectionName = "AiGateway:Models";

    /// <summary>ADR-004 candidate: "Small instruction model (e.g. GPT-4o-mini / Phi-class)". On
    /// Azure the deployment name comes from configuration (ADR-004 amendment 2026-09-09);
    /// <see cref="AiModelSelection.MaxCompletionTokens"/> is small because the verdict is two
    /// fields.</summary>
    public AiModelSelection Classify { get; init; } = new("gpt-4o-mini", "unconfirmed") { MaxCompletionTokens = 2048 };

    /// <summary>ADR-004 candidate: "Structured-output-capable (e.g. GPT-4o-mini with JSON-schema mode)".
    /// A list stage over a long contract can legitimately return thousands of tokens of JSON, so the
    /// completion cap is generous; hitting it is a visible stage failure, never truncated JSON.</summary>
    public AiModelSelection Extract { get; init; } = new("gpt-4o-mini", "unconfirmed") { MaxCompletionTokens = 16384 };

    /// <summary>ADR-004 candidate: "text-embedding-3-small" — "small dimension preferred for cost/size".
    /// <see cref="AiModelSelection.Dimensions"/> pins the vector width to the pgvector column so a
    /// wider deployment (demo's text-embedding-3-large) is reduced on the wire, never at insert time.</summary>
    public AiModelSelection Embed { get; init; } = new("text-embedding-3-small", "unconfirmed") { Dimensions = AiGatewayConstants.EmbeddingDimensions };

    /// <summary>ADR-004 candidate: "Same instruction model as extract, or one tier up if citation quality is insufficient".</summary>
    public AiModelSelection Answer { get; init; } = new("gpt-4o-mini", "unconfirmed") { MaxCompletionTokens = 4096 };

    /// <summary>
    /// ADR-017 candidate: Azure AI Document Intelligence <c>prebuilt-read</c> — the cheapest model
    /// that meets the `ocr` role's evidence requirement (full-document text + page map). Added by
    /// task E02/F01/US02/T02 (hybrid-ocr): ADR-017 "gains a fifth role, `ocr`, bound through
    /// configuration like the other roles" — this class already existed for the first four
    /// (task E02/F01/US01/T01), so the fifth is one more property, not a new options type. A
    /// caller that also needs table/section layout (not just full-document text) swaps this to
    /// `prebuilt-layout` via configuration, same as any other role.
    /// </summary>
    public AiModelSelection Ocr { get; init; } = new("prebuilt-read", "unconfirmed");
}
