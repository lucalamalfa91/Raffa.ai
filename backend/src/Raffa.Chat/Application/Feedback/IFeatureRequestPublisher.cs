namespace Raffa.Chat.Application.Feedback;

/// <summary>What the publisher returned: opened (number + url), or not (a named failure).</summary>
public sealed record FeatureRequestPublishResult(bool Published, int? IssueNumber, string? IssueUrl, string? Failure)
{
    public static FeatureRequestPublishResult Opened(int issueNumber, string issueUrl) => new(true, issueNumber, issueUrl, null);

    public static FeatureRequestPublishResult Failed(string failure) => new(false, null, null, failure);
}

/// <summary>
/// The outbound seam of the feedback loop (ADR-030 D5): turns a <see cref="FeatureRequestIssue"/>
/// into a tracker issue the developers pick up. The one production implementation lives in the
/// host (<c>Raffa.Api.Infrastructure.GitHubIssueFeatureRequestPublisher</c>, a provider adapter,
/// ADR-002) and is registered only when a token is configured; this module's own default is
/// <see cref="NullFeatureRequestPublisher"/>, so a workspace without the secret still records
/// every request. Never throws for a remote failure — the caller stores first, publishes
/// best-effort, and treats a failure as "recorded".
/// </summary>
public interface IFeatureRequestPublisher
{
    bool IsConfigured { get; }

    Task<FeatureRequestPublishResult> TryPublishAsync(FeatureRequestIssue issue, CancellationToken cancellationToken = default);
}

/// <summary>
/// F4-T01: the names the free text of a feedback answer must not carry into a public issue — the
/// tenant's suppliers. The module cannot read them itself (its allow-list excludes the modules that
/// hold suppliers and contracts), so the host supplies them through this seam; the module's
/// default knows none, and the scrub then works by the shape of a name alone.
/// </summary>
public interface IFeedbackNameSource
{
    /// <summary>Every supplier name this tenant has on file. Best effort: the caller treats a
    /// failure as "none known".</summary>
    Task<IReadOnlyList<string>> GetSupplierNamesAsync(Raffa.SharedKernel.TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>The module's default: no names are known.</summary>
public sealed class NullFeedbackNameSource : IFeedbackNameSource
{
    public Task<IReadOnlyList<string>> GetSupplierNamesAsync(Raffa.SharedKernel.TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}

/// <summary>The module's default: nothing is published, every request stays "recorded".</summary>
public sealed class NullFeatureRequestPublisher : IFeatureRequestPublisher
{
    public bool IsConfigured => false;

    public Task<FeatureRequestPublishResult> TryPublishAsync(FeatureRequestIssue issue, CancellationToken cancellationToken = default) =>
        Task.FromResult(FeatureRequestPublishResult.Failed("no feature-request publisher is configured."));
}
