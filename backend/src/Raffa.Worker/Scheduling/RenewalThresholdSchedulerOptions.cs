namespace Raffa.Worker.Scheduling;

/// <summary>
/// How often <see cref="RenewalThresholdSchedulerHostedService"/> ticks (task E03/F02/US01/T01:
/// product spec §9.1 "Daily scheduler for each active contract"). Internal: host-composition
/// wiring, not a public API surface (same reasoning as this folder's other types).
///
/// A composition root binds this from configuration, e.g.
/// <c>configuration.GetSection(RenewalThresholdSchedulerOptions.SectionName).Bind(options)</c> —
/// same lazy bind-with-defaults pattern
/// <see cref="Raffa.Renewals.Configuration.ThresholdWindowOptions"/> and
/// <c>Raffa.AiGateway.Configuration.AiGatewayModelOptions</c> already use. Kept independently
/// overridable (rather than a hard-coded constant like
/// <c>Raffa.Worker.Queue.QueueConsumerHostedService.PollDelay</c>) because the real default is
/// 24 hours — too long to wait out in a test — so
/// <c>Raffa.Worker.Tests.RenewalThresholdSchedulerHostedServiceTests</c> pre-registers a
/// millisecond-scale instance before calling <c>AddWorkerHost</c> (the same
/// override-before-registering convention
/// <c>Raffa.Renewals.Tests.ServiceCollectionExtensionsTests
/// .AddRenewalsModule_does_not_override_an_already_registered_IClock</c> already proves for
/// <c>IClock</c>).
/// </summary>
internal sealed class RenewalThresholdSchedulerOptions
{
    /// <summary>Conventional configuration section path for binding this options object.</summary>
    public const string SectionName = "Worker:RenewalThresholdScheduler";

    /// <summary>Product spec §9.1 names a daily cadence; 24 hours is the literal reading of
    /// "daily". A config value always overrides this default.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(24);
}
