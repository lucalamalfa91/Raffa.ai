namespace Raffa.AiGateway.Configuration;

/// <summary>
/// Jev (TypeSafe AI System One model, reached through OpenRouter's Decisions API) as a pilot
/// backend for the <c>classify</c> role only (document-admission taxonomy, us-01-ai-gateway-classification).
/// This is a dev-only trial, not an ADR-004 model swap: Jev answers a different question shape
/// (a typed "choice" decision with a calibrated confidence, not a chat completion) than every
/// other role this gateway serves, so it is wired as an opt-in decorator
/// (<see cref="Jev.JevAiGateway"/>) in front of the existing Foundry/Fixture gateway, never as a
/// value of <see cref="AiGatewayModelOptions.Classify"/> itself. Every other role (extract, embed,
/// answer, ocr, analyst, research) is untouched and keeps calling Foundry exactly as today.
///
/// <para>
/// <b>Why OpenRouter, not TypeSafe's own API directly:</b> OpenRouter is the one place Jev's
/// request/response contract is publicly documented (<c>POST /api/v1/systemone</c>, model
/// <c>typesafe/jev-1.13</c>): a <c>state</c> (the document text) plus a map of typed
/// <c>questions</c> (here, one <c>choice</c> question over the fixed <see cref="Contracts.AiDocumentType"/>
/// taxonomy), answered with a chosen option, per-option probabilities and a confidence — no
/// arbitrary free-value or multi-field JSON output exists in this API, which is exactly why this
/// pilot is scoped to <c>classify</c> and not <c>extract</c> (see this task's own plan: extraction
/// needs free-value fields — dates, amounts, verbatim clause text — that none of Jev's three
/// question primitives, Choice/Noul/Score, can produce).
/// </para>
///
/// <para>
/// <b>Unverified contract, flagged on purpose:</b> this options type and
/// <see cref="Jev.JevHttpJsonClient"/> were built from OpenRouter's published documentation and
/// third-party write-ups, not from a live call against a real API key (this development
/// environment could not reach <c>openrouter.ai</c> to confirm the exact response field names).
/// The very first thing to do with a real <see cref="ApiKey"/> is a manual smoke test against one
/// known document before trusting any dev upload to this path — <see cref="Jev.JevHttpJsonClient"/>
/// fails loudly (a <see cref="SharedKernel.Result{T}"/> failure naming the unexpected shape) rather
/// than silently misreading a field, precisely because this contract is not yet proven.
/// </para>
/// </summary>
public sealed class AiGatewayJevOptions
{
    /// <summary>Conventional configuration section path for binding this options object.</summary>
    public const string SectionName = "AiGateway:Jev";

    /// <summary>
    /// The pilot's own kill switch — independent of <see cref="AiGatewayFoundryOptions.Endpoint"/>.
    /// <see langword="false"/> (the default, and the only value demo/prod ever carry) means
    /// <see cref="ServiceCollectionExtensions.AddAiGatewayModule"/> registers the Foundry/Fixture
    /// gateway exactly as before this pilot existed — this option adds no behavior at all until an
    /// operator deliberately turns it on for a specific environment (dev, for this trial).
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// OpenRouter API key (never a TypeSafe key directly — see type doc comment). A Container Apps
    /// secret reference in every real deployment (ADR-011: never a raw secret in a plain env var or
    /// in this file), read here as plain configuration the same way every other AiGateway option is.
    /// Blank while <see cref="Enabled"/> is <see langword="true"/> is a per-call failure, not a
    /// startup crash: <see cref="Jev.JevHttpJsonClient"/> reports it as a graceful
    /// <see cref="SharedKernel.Result{T}"/> failure the first time classify is actually called
    /// (this <see cref="IAiGateway"/> is shared with the Worker, which never calls the `classify`
    /// role but does resolve <see cref="IAiGateway"/> for every other role — a startup-time throw
    /// here would take the Worker down for a pilot it never exercises).
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>OpenRouter's base address for the Decisions/System One API.</summary>
    public string Endpoint { get; init; } = "https://openrouter.ai/api/v1/systemone";

    /// <summary>OpenRouter model id for the `classify` role's Jev question. <c>jev-1.13</c> is the
    /// pinned version (not <c>jev-router</c>/<c>jev-latest</c>) so a result recorded today stays
    /// reproducible if TypeSafe ships a new Jev version tomorrow (same reproducibility posture
    /// <see cref="Contracts.AiCallMetadata"/> already documents for Foundry).</summary>
    public string Model { get; init; } = "typesafe/jev-1.13";

    /// <summary>OpenRouter's recommended attribution headers (<c>HTTP-Referer</c>/<c>X-Title</c>) —
    /// optional, informational only, never required for the call to succeed.</summary>
    public string? HttpReferer { get; init; }

    /// <summary>OpenRouter's recommended <c>X-Title</c> attribution header — see <see cref="HttpReferer"/>'s
    /// own doc comment.</summary>
    public string? AppTitle { get; init; } = "Raffa";

    /// <summary>Same representative-prefix cap <see cref="AiGatewayFoundryOptions.ClassifyMaxInputChars"/>
    /// applies today, reused rather than duplicated so a Jev call is billed for the same input size
    /// a Foundry call would have been -- the two are meant to be compared apples to apples.</summary>
    public int ClassifyMaxInputChars { get; init; } = 40_000;
}
