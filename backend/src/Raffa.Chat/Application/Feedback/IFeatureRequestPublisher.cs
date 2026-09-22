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

/// <summary>The module's default: nothing is published, every request stays "recorded".</summary>
public sealed class NullFeatureRequestPublisher : IFeatureRequestPublisher
{
    public bool IsConfigured => false;

    public Task<FeatureRequestPublishResult> TryPublishAsync(FeatureRequestIssue issue, CancellationToken cancellationToken = default) =>
        Task.FromResult(FeatureRequestPublishResult.Failed("no feature-request publisher is configured."));
}
