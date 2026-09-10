namespace Raffa.AiGateway.Configuration;

/// <summary>
/// AI Gateway compliance configuration (ADR-011: "the Foundry deployment/model selected must be a
/// no-training endpoint, and the gateway is the single choke point that proves it"; product spec
/// §14.2 "no training on public/shared models"; locked-decisions.md "AI" row: Foundry only via the
/// AI Gateway). A composition root binds this the same way as
/// <see cref="AiGatewayModelOptions"/> — e.g.
/// <c>configuration.GetSection(AiGatewayComplianceOptions.SectionName).Bind(options)</c> — no host
/// does that yet (same gap <see cref="AiGatewayModelOptions"/>'s own doc comment records); task
/// E02/F01/US02/T01 (staged extraction) is the first caller for both options types.
///
/// <see cref="NoTraining"/> defaults to <see langword="true"/> because ADR-011 treats no-training
/// as a locked requirement, not an opt-in preference — a deployment must prove compliance, not
/// default into non-compliance by omission.
/// <see cref="Logging.LoggingAiGateway"/> refuses to construct when this is
/// <see langword="false"/> (fails fast at composition-root wiring time, not per call), which is
/// what makes it ADR-011's "single choke point that proves it" in practice.
/// </summary>
public sealed class AiGatewayComplianceOptions
{
    /// <summary>Conventional configuration section path for binding this options object.</summary>
    public const string SectionName = "AiGateway:Compliance";

    /// <summary>
    /// Whether the configured Foundry endpoint is a no-training endpoint (ADR-011 "Assumptions":
    /// "if the only available model trains on shared data, the AI Gateway must be configured to
    /// opt out (or the cheapest compliant model selected)"). Defaults to <see langword="true"/>.
    /// </summary>
    public bool NoTraining { get; init; } = true;
}
