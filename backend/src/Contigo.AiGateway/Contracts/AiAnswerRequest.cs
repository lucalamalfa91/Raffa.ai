namespace Contigo.AiGateway.Contracts;

/// <summary>
/// Input to the `answer` role: a question plus the evidence the caller has already retrieved and
/// authorized (ADR-011 "authorization before retrieval"). An empty <see cref="Evidence"/> list is
/// valid input, not an error — it is how a caller represents "authorized retrieval found
/// nothing", which the gateway must answer with "cannot determine" rather than guessing (spec
/// §8.4; Appendix C rule 10).
///
/// <see cref="SystemPrompt"/> and <see cref="PackJson"/> are ADR-024's (epic-13/ask-v2) additions,
/// both optional and defaulted so every existing call site (<c>Contigo.Chat.Application
/// .RagAnswerService</c>, every <c>Contigo.Chat.Tests</c>/<c>Contigo.AiGateway.Tests</c> fixture
/// test) keeps compiling unchanged (task E13/F01/US01/T02, foundry-gateway). They exist for the
/// Foundry-backed `answer` role's new, versioned-persona-prompt-plus-context-pack shape (ADR-024
/// "a versioned persona prompt"); the gap note on that task names the caller that will actually
/// populate them: "F06 replaces the chunk-concat by supplying a prompt + pack" — until then both
/// stay <see langword="null"/> and <see cref="Foundry.FoundryAnswerClient"/> falls back to its own
/// built-in persona prompt plus <see cref="Evidence"/>, the same evidence-grounded shape the
/// fixture already implements.
/// </summary>
/// <param name="Question">The user's question, already scoped by the caller's own intent routing (spec §8.3).</param>
/// <param name="Evidence">Pre-retrieved, pre-authorized evidence to ground the answer in.</param>
/// <param name="SystemPrompt">
/// Caller-supplied, versioned persona system prompt (ADR-024). <see langword="null"/> keeps
/// today's fixture/RAG-evidence path unchanged — <see cref="Foundry.FoundryAnswerClient"/> then
/// uses its own default persona prompt instead of failing or sending an empty system message.
/// </param>
/// <param name="PackJson">
/// Caller-assembled context pack JSON (ADR-024: "context pack assembled in Contigo.Api from the
/// three sources with citationKey/corpus/provenance"). <see langword="null"/> means no pack was
/// assembled yet — the Foundry `answer` role then grounds only in <see cref="Evidence"/>, same as
/// the fixture.
/// </param>
public sealed record AiAnswerRequest(
    string Question,
    IReadOnlyList<AiEvidenceSnippet> Evidence,
    string? SystemPrompt = null,
    string? PackJson = null);
