using Raffa.AiGateway.Contracts;
using Raffa.SharedKernel;

namespace Raffa.AiGateway.Fixtures;

/// <summary>
/// The deterministic double of the `agent` role for <see cref="FixtureAiGateway"/>: a host or a
/// test registers one and it decides, from the request alone (no network, no clock), which tool
/// calls the "model" asks for next or what final JSON it writes. The fixture gateway has no
/// opinion of its own because the final schema and the tool set are the caller's; without a script
/// the role is unavailable, which the orchestrator treats as "fall back to the deterministic path".
/// </summary>
public interface IFixtureAgentScript
{
    Result<AiConversationResult> Next(AiConversationRequest request, AiCallMetadata metadata);
}
