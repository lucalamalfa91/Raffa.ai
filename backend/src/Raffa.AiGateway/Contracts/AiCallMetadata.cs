namespace Raffa.AiGateway.Contracts;

/// <summary>
/// Reproducibility metadata every AI Gateway role call returns (product spec §8/§14.2, ADR-004,
/// ADR-011): model id + version, prompt version, response timestamp, and a content hash of the
/// input — never the raw prompt or retrieved contract text itself. ADR-011 "Assumptions": input
/// hash is a content hash (e.g. SHA-256) of the retrieved evidence/prompt, so a given
/// model/version can be verified against a given input without storing the confidential input.
/// Task E02/F01/US01/T02 (ai-gateway-logging) persists this record; this task only guarantees
/// every role call produces one.
/// </summary>
/// <param name="ModelId">Config-selected model id for the role that produced this result (ADR-004).</param>
/// <param name="ModelVersion">Model/deployment version, as reported by the provider (or the fixture, pre-Foundry).</param>
/// <param name="PromptVersion">Version tag of the prompt template used for this call.</param>
/// <param name="RespondedAtUtc">When the call completed, from <see cref="Raffa.SharedKernel.IClock"/> (deterministic in tests).</param>
/// <param name="InputHash">SHA-256 hex digest of the input text — never the input itself.</param>
/// <param name="Usage">Token usage the provider reported for this call, when it reports one (chat
/// and embedding roles on Foundry); <see langword="null"/> for the fixture and for OCR. Logged so
/// spend is observable per call (Appendix C rule 8).</param>
public sealed record AiCallMetadata(
    string ModelId,
    string ModelVersion,
    string PromptVersion,
    DateTimeOffset RespondedAtUtc,
    string InputHash,
    AiTokenUsage? Usage = null);
