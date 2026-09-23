using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.Chat.Application.Feedback;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// The real <see cref="IFeatureRequestPublisher"/> (ADR-030 D5): opens one issue per submitted
/// feature request on the configured GitHub repository through the REST API
/// (<c>POST /repos/{owner}/{repo}/issues</c>) with a fine-grained token from Key Vault. A host
/// adapter, not a module member (ADR-002: an outbound provider integration lives in the host, like
/// <see cref="AcsInvitationMailer"/>); plain <see cref="HttpClient"/>, no SDK. Registered by
/// <c>Program.cs</c> only while <c>Feedback__GitHub__Enabled</c> is true, so the module's
/// <c>TryAdd</c>ed <see cref="NullFeatureRequestPublisher"/> never wins over it.
///
/// <para>
/// <b>Never throws for a remote failure.</b> Any non-201 answer, a timeout or a transport error
/// becomes a named <see cref="FeatureRequestPublishResult.Failed"/>: the caller has already stored
/// the request and answers the user "recorded". A 422 (typically an unknown label) is retried
/// once without labels. The log carries the status and GitHub's own <c>message</c>, never the
/// issue body — it is the user's text — and never the token.
/// </para>
/// </summary>
internal sealed class GitHubIssueFeatureRequestPublisher(
    HttpClient client,
    FeedbackHostOptions options,
    ILogger<GitHubIssueFeatureRequestPublisher> logger) : IFeatureRequestPublisher
{
    public const string ApiVersion = "2022-11-28";
    public const string UserAgent = "Raffa.ai-feedback";

    public bool IsConfigured => options.GitHub.Enabled && !string.IsNullOrWhiteSpace(options.GitHub.Token);

    public async Task<FeatureRequestPublishResult> TryPublishAsync(FeatureRequestIssue issue, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(issue);

        if (!IsConfigured)
        {
            return FeatureRequestPublishResult.Failed("the GitHub publisher is not configured.");
        }

        var (title, body) = FeatureRequestIssueText.Compose(issue);
        var labels = FeatureRequestIssueText.Labels(issue, options.GitHub.LabelList);

        var first = await SendAsync(title, body, labels, cancellationToken).ConfigureAwait(false);
        if (first.Published || first.StatusCode != HttpStatusCode.UnprocessableEntity || labels.Count == 0)
        {
            return first.Result;
        }

        logger.LogWarning(
            "GitHub rejected the feature-request issue with 422 (labels {Labels}); retrying without labels",
            string.Join(',', labels));

        var retry = await SendAsync(title, body, [], cancellationToken).ConfigureAwait(false);
        return retry.Result;
    }

    private async Task<(bool Published, HttpStatusCode? StatusCode, FeatureRequestPublishResult Result)> SendAsync(
        string title, string body, IReadOnlyList<string> labels, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"repos/{options.GitHub.Repository}/issues");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.GitHub.Token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, "1.0"));
            request.Content = JsonContent.Create(new { title, body, labels });

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Created)
            {
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;
                if (root.TryGetProperty("number", out var numberElement) && numberElement.TryGetInt32(out var number) &&
                    root.TryGetProperty("html_url", out var urlElement) && urlElement.GetString() is { Length: > 0 } url)
                {
                    logger.LogInformation("Feature request published as GitHub issue #{IssueNumber}", number);
                    return (true, response.StatusCode, FeatureRequestPublishResult.Opened(number, url));
                }

                return (false, response.StatusCode, FeatureRequestPublishResult.Failed("GitHub answered 201 without a number/html_url."));
            }

            var message = ExtractMessage(payload);
            logger.LogWarning(
                "GitHub refused the feature-request issue: {StatusCode} {Message}", (int)response.StatusCode, message);
            return (false, response.StatusCode, FeatureRequestPublishResult.Failed($"GitHub {(int)response.StatusCode}: {message}"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "The feature-request issue could not be published");
            return (false, null, FeatureRequestPublishResult.Failed(ex.GetType().Name + ": " + ex.Message));
        }
    }

    private static string ExtractMessage(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("message", out var message)
                ? message.GetString() ?? "no message"
                : "no message";
        }
        catch (JsonException)
        {
            return "no message";
        }
    }
}
