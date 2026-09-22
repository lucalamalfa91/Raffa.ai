using System.Text.RegularExpressions;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// Binds the <c>Feedback</c> configuration section (ADR-030 D5) — the keys
/// <c>infra/modules/containerapps</c> publishes onto the API container app:
/// <c>Feedback__Environment</c>, <c>Feedback__GitHub__Enabled</c>, <c>Feedback__GitHub__Repository</c>,
/// <c>Feedback__GitHub__Token</c> (a Key Vault secret, handle <c>gh-feedback</c>) and
/// <c>Feedback__GitHub__Labels</c>. Same posture as <see cref="InvitationHostOptions"/>: every key
/// is optional at bind time so a dev box still boots with the module's own
/// <c>NullFeatureRequestPublisher</c>, and the one combination that must never run — the GitHub
/// publisher enabled with no token, or an unparseable repository — fails closed at startup.
/// </summary>
internal sealed partial class FeedbackHostOptions
{
    public const string SectionName = "Feedback";

    /// <summary>Written into every issue ("dev", "demo", "local").</summary>
    public string Environment { get; set; } = "local";

    public GitHubOptions GitHub { get; set; } = new();

    public sealed class GitHubOptions
    {
        /// <summary>A product switch: a valid token behind <see langword="false"/> is harmless and
        /// every submission stays "recorded".</summary>
        public bool Enabled { get; set; }

        /// <summary><c>owner/repo</c> the issues are opened in.</summary>
        public string Repository { get; set; } = "lucalamalfa91/Raffa.ai";

        /// <summary>A fine-grained personal access token with Issues: write on that one
        /// repository, sourced from Key Vault; never logged.</summary>
        public string? Token { get; set; }

        /// <summary>Comma-separated labels applied to every issue; missing labels are retried
        /// without labels (GitHub answers 422 for an unknown label).</summary>
        public string Labels { get; set; } = "feedback,ask-raffa";

        public IReadOnlyList<string> LabelList =>
            Labels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    [GeneratedRegex(@"^[\w.-]+/[\w.-]+$")]
    private static partial Regex RepositoryPattern();

    /// <summary>Fail closed at startup on the composed pair, and only on it.</summary>
    public void ValidateOrThrow()
    {
        if (!GitHub.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(GitHub.Token))
        {
            throw new InvalidOperationException(
                "Feedback__GitHub__Enabled is true but Feedback__GitHub__Token is missing. Refusing to " +
                "start: a publisher that cannot authenticate must not be registered as the feedback " +
                "transport (ADR-030 D5).");
        }

        if (!RepositoryPattern().IsMatch(GitHub.Repository))
        {
            throw new InvalidOperationException(
                $"Feedback__GitHub__Repository '{GitHub.Repository}' is not an owner/repo pair (ADR-030 D5).");
        }
    }
}
