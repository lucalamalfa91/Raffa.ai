namespace Raffa.AiFlows.QuoteExtraction;

/// <summary>
/// Settings of the quote-extraction flow, bound from the <c>QuoteExtraction</c> configuration section
/// (<c>QuoteExtraction__HeaderStageEnabled</c> in an environment).
/// </summary>
public sealed class QuoteExtractionOptions
{
    public const string SectionName = "QuoteExtraction";

    /// <summary>
    /// Task F6-T07: run the header stage (supplier, quote date, currency, geography) after the line
    /// extraction, for a quote whose header the upload left incomplete. <b>Off by default</b>: it is a
    /// second model call per such quote (the document text is sent again, with its own schema), so
    /// turning it on is a cost decision for whoever has measured it on real quotes. Off, the flow is
    /// exactly what it was.
    /// </summary>
    public bool HeaderStageEnabled { get; set; }

    /// <summary>
    /// A header value is recorded only when the model's own confidence for it is at least this; below
    /// it the field stays blank for a person to fill in (a wrong supplier or currency silently
    /// mis-matches every line of the quote, a blank one is asked about).
    /// </summary>
    public double HeaderMinConfidence { get; set; } = 0.7;
}
