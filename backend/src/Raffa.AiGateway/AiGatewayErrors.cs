namespace Raffa.AiGateway;

/// <summary>
/// Error-message conventions shared by the gateway and its callers. A <see cref="Raffa.SharedKernel.Result{T}"/>
/// failure is a string, so the one thing a caller can key on without coupling to a provider type is a
/// stable prefix.
/// </summary>
public static class AiGatewayErrors
{
    /// <summary>
    /// Prefix of every failure that means "the provider could not be reached or kept throttling",
    /// as opposed to "this input could not be processed". <c>DocumentAdmissionGate</c> maps it to
    /// the retryable HTTP 503 outcome instead of a 400, the same way it already treats a thrown
    /// credential/network exception.
    /// </summary>
    public const string UnavailablePrefix = "AI provider unavailable:";

    /// <summary>
    /// Prefix of a research failure that happened <em>after</em> the provider ran the search: the
    /// response was <c>incomplete</c>/<c>failed</c>, carried no text, or was not the expected JSON
    /// (F3-T02). The call was made and is billed, so the caller keeps its budget unit spent.
    /// </summary>
    public const string ResearchOutputPrefix = "AI research output unusable:";

    /// <summary>Whether a failure message is the provider's content-filter verdict (Azure code
    /// <c>content_filter</c>): the request reached the model and was judged on its merits.</summary>
    public static bool IsContentFilter(string? error) =>
        !string.IsNullOrEmpty(error)
        && (error.Contains("content_filter", StringComparison.OrdinalIgnoreCase)
            || error.Contains("ResponsibleAIPolicyViolation", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// F3-T03: whether a failed <c>research</c> call should hand its budget unit back. True for
    /// transport and configuration failures (provider unreachable or throttling, no deployment, a
    /// rejected request, an invalid input — no search ran); false for a content-filter verdict and
    /// for output that came back but was unusable (<see cref="ResearchOutputPrefix"/>), where the
    /// provider did the work.
    /// </summary>
    public static bool ResearchFailureReleasesBudget(string? error) =>
        !IsContentFilter(error)
        && !(error?.StartsWith(ResearchOutputPrefix, StringComparison.Ordinal) ?? false);
}
