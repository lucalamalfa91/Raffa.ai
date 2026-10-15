namespace Raffa.AiFlows.QuoteExtraction.Orchestration;

/// <summary>
/// Stage labels of the quote-extraction flow. The AI Gateway's `extract` role identifies the call
/// by a caller-owned string, and the fixture gateway and the live prompt both key on it, so the
/// value is a contract: never rename it.
/// </summary>
public static class QuoteExtractionStages
{
    /// <summary>Caller-owned stage label for the AI Gateway's `extract` role (see
    /// <c>IAiGateway.ExtractAsync</c>'s own doc comment: "mirrors, but does not reference,
    /// ExtractionStage; the gateway must not depend on a domain module's enum").</summary>
    public const string StageName = "QuoteLineItems";

    /// <summary>Task F6-T07: caller-owned stage label of the header call (supplier, quote date,
    /// currency, geography), with its own schema (<c>QuoteHeaderJsonSchema</c>). A new label, not a
    /// rename of <see cref="StageName"/>; the extract prompt is the gateway's generic one, keyed on
    /// this label only for telemetry and the fixture gateway.</summary>
    public const string HeaderStageName = "QuoteHeader";
}
