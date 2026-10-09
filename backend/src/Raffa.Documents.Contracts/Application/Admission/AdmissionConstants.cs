namespace Raffa.Documents.Contracts.Application.Admission;

/// <summary>
/// Admission-related values that code outside the admission gate (the extraction handler, the hung
/// processing recovery) needs to recognise.
/// </summary>
public static class AdmissionConstants
{
    /// <summary>Prefix that marks a <see cref="AdmissionOutcome.Failed"/> error as "the provider is
    /// unavailable" rather than "this document could not be read".</summary>
    public const string GatewayUnavailablePrefix = "The document could not be assessed:";
}
