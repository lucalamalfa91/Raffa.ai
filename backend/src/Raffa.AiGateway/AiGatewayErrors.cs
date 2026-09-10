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
}
