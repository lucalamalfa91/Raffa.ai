namespace Raffa.Renewals.Configuration;

/// <summary>
/// Configurable day-count windows <see cref="Raffa.Renewals.Application.RenewalThresholdScheduler"/>
/// checks each contract's renewal date and cancellation deadline against (task
/// E03/F02/US01/T01, the wave-spec's <c>threshold-scheduler</c> artifact; parent story
/// us-01-threshold-scheduler AC-1: "Threshold windows 365/270/180/120/90/60/30 days,
/// configurable"; product spec §9.1: "Default threshold windows: 365 / 270 / 180 / 120 / 90 / 60 /
/// 30 days; keep configurable").
///
/// A composition root binds this from configuration, e.g.
/// <c>configuration.GetSection(ThresholdWindowOptions.SectionName).Bind(options)</c> — the same
/// "always-usable default, config only overlays what is present" convention
/// <see cref="Raffa.AiGateway.Configuration.AiGatewayModelOptions"/> and
/// <see cref="Raffa.AiGateway.Configuration.AiGatewayOcrOptions"/> already use (see
/// <c>Raffa.Renewals.Infrastructure.ServiceCollectionExtensions.AddRenewalsModule</c>).
/// </summary>
public sealed class ThresholdWindowOptions
{
    /// <summary>Conventional configuration section path for binding this options object.</summary>
    public const string SectionName = "Renewals:Thresholds";

    /// <summary>
    /// Product spec §9.1's own default ladder, descending from a year out to a month out —
    /// nothing here is hard-coded into
    /// <see cref="Raffa.Renewals.Application.RenewalThresholdScheduler"/>'s logic, which treats
    /// this list as an opaque set of day-counts to match against, in any order, any length.
    ///
    /// Typed as a concrete <c>int[]</c>, not <c>IReadOnlyList&lt;int&gt;</c>: the
    /// <c>Microsoft.Extensions.Configuration</c> binder only knows how to rebuild a handful of
    /// concrete/interface collection shapes (arrays, <c>List&lt;T&gt;</c>,
    /// <c>ICollection&lt;T&gt;</c>/<c>IList&lt;T&gt;</c>) from indexed keys like
    /// <c>Renewals:Thresholds:DaysBeforeDeadline:0</c> — a bare <c>IReadOnlyList&lt;T&gt;</c>
    /// target silently binds nothing (proven by an earlier, failing attempt at
    /// <c>Raffa.Renewals.Tests.ServiceCollectionExtensionsTests
    /// .AddRenewalsModule_binds_thresholds_from_the_configured_Renewals_Thresholds_section</c>).
    ///
    /// A second, less obvious binder quirk: even with the <c>int[]</c> type, binding directly onto
    /// an instance whose array property already holds this default (via
    /// <c>IConfiguration.Bind(object)</c>) <em>appends</em> the configured items after the seven
    /// defaults instead of replacing them — arrays merge, they do not index-overwrite.
    /// <c>ServiceCollectionExtensions.AddRenewalsModule</c>'s own binding factory works around this
    /// deliberately (starts from an empty array whenever the configuration section actually exists,
    /// so a configured override replaces this default in full); this property initializer only
    /// needs to supply a sensible value for direct construction (every test in
    /// <c>Raffa.Renewals.Tests.RenewalThresholdSchedulerTests</c> constructs
    /// <see cref="ThresholdWindowOptions"/> directly, not through DI).
    /// </summary>
    public int[] DaysBeforeDeadline { get; init; } = [365, 270, 180, 120, 90, 60, 30];
}
