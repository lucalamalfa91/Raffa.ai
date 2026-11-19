using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Raffa.Api.Infrastructure;

/// <summary>GitHub refused or could not do something the export needed; <see cref="Message"/> says what, never a token.</summary>
internal sealed class GitHubPublishException(string message) : Exception(message);

internal sealed record GitHubFile(string Content, string Sha);

/// <summary>
/// Opens a draft pull request on the repository for a playground proposal, through the REST API and the same connection the
/// feedback publisher already uses (<see cref="FeedbackHostOptions.GitHub"/>: the repository and the fine-grained token from Key
/// Vault, off in exposed environments). A host adapter like <see cref="GitHubIssueFeatureRequestPublisher"/>: plain
/// <see cref="HttpClient"/>, no SDK. It needs the token to hold <b>Contents</b> and <b>Pull requests</b> read and write on the
/// repository, which an issues-only token does not; a refusal says so. It never merges and never writes to the default branch:
/// the proposal goes to a new branch and a draft pull request that a person reviews.
/// </summary>
internal sealed class GitHubPullRequestPublisher(HttpClient client, FeedbackHostOptions options)
{
    public string Repository => options.GitHub.Repository;

    public bool IsConfigured => options.GitHubPublishingActive && !string.IsNullOrWhiteSpace(options.GitHub.Token);

    public async Task<string> DefaultBranchAsync(CancellationToken ct)
    {
        using var document = await SendAsync(HttpMethod.Get, $"repos/{Repository}", null, "read the repository", ct).ConfigureAwait(false);
        return document!.RootElement.GetProperty("default_branch").GetString() ?? "main";
    }

    public async Task<string> BranchShaAsync(string branch, CancellationToken ct)
    {
        using var document = await SendAsync(HttpMethod.Get, $"repos/{Repository}/git/ref/heads/{Uri.EscapeDataString(branch)}", null, $"find the branch '{branch}'", ct).ConfigureAwait(false);
        return document!.RootElement.GetProperty("object").GetProperty("sha").GetString()
            ?? throw new GitHubPublishException("GitHub returned no commit for the branch.");
    }

    /// <summary>A file of the repository at <paramref name="branch"/>, or <see langword="null"/> when it does not exist there.</summary>
    public async Task<GitHubFile?> GetFileAsync(string path, string branch, CancellationToken ct)
    {
        using var document = await SendAsync(
            HttpMethod.Get, $"repos/{Repository}/contents/{EscapePath(path)}?ref={Uri.EscapeDataString(branch)}", null, $"read {path}", ct, allowNotFound: true).ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        var root = document.RootElement;
        var base64 = (root.GetProperty("content").GetString() ?? string.Empty).Replace("\n", string.Empty, StringComparison.Ordinal);
        return new GitHubFile(Encoding.UTF8.GetString(Convert.FromBase64String(base64)), root.GetProperty("sha").GetString() ?? string.Empty);
    }

    public async Task CreateBranchAsync(string name, string fromSha, CancellationToken ct)
    {
        using var _ = await SendAsync(
            HttpMethod.Post, $"repos/{Repository}/git/refs", new { @ref = "refs/heads/" + name, sha = fromSha }, $"create the branch '{name}'", ct).ConfigureAwait(false);
    }

    public async Task PutFileAsync(string path, string content, string message, string branch, string? existingSha, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["message"] = message,
            ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(content)),
            ["branch"] = branch,
        };
        if (existingSha is not null)
        {
            body["sha"] = existingSha;
        }

        using var _ = await SendAsync(HttpMethod.Put, $"repos/{Repository}/contents/{EscapePath(path)}", body, $"write {path}", ct).ConfigureAwait(false);
    }

    public async Task<(int Number, string Url)> CreatePullRequestAsync(string title, string head, string baseBranch, string body, CancellationToken ct)
    {
        using var document = await SendAsync(
            HttpMethod.Post, $"repos/{Repository}/pulls", new { title, head, @base = baseBranch, body, draft = true }, "open the pull request", ct).ConfigureAwait(false);
        var root = document!.RootElement;
        return (root.GetProperty("number").GetInt32(), root.GetProperty("html_url").GetString() ?? string.Empty);
    }

    private static string EscapePath(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private async Task<JsonDocument?> SendAsync(HttpMethod method, string path, object? body, string what, CancellationToken ct, bool allowNotFound = false)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.GitHub.Token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", GitHubIssueFeatureRequestPublisher.ApiVersion);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Raffa.ai-playground", "1.0"));
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return JsonDocument.Parse(payload);
            }

            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            throw new GitHubPublishException(Explain(response.StatusCode, payload, what));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new GitHubPublishException($"Could not {what}: {ex.GetType().Name}.");
        }
    }

    private string Explain(HttpStatusCode status, string payload, string what)
    {
        var detail = "no message";
        try
        {
            using var document = JsonDocument.Parse(payload);
            detail = document.RootElement.TryGetProperty("message", out var message) ? message.GetString() ?? detail : detail;
        }
        catch (JsonException)
        {
        }

        return status switch
        {
            HttpStatusCode.Unauthorized => $"GitHub rejected the token while trying to {what}.",
            HttpStatusCode.Forbidden or HttpStatusCode.NotFound =>
                $"GitHub answered {(int)status} while trying to {what} on {Repository} ({detail}). The token needs Contents and Pull requests read and write on that repository; the feedback token may hold only Issues.",
            _ => $"GitHub answered {(int)status} while trying to {what}: {detail}",
        };
    }
}
