namespace Raffa.AiFlows.DocumentExtraction.Verification;

/// <summary>Outcome of checking one fact's evidence span against the page-marked document text.</summary>
public enum SpanVerdict
{
    /// <summary>Found (exact, or fuzzy at or above the threshold) on the page the model named, or anywhere
    /// when the model named no usable page.</summary>
    Found,

    /// <summary>Not on the page the model named, but found on another page.</summary>
    FoundOnOtherPage,

    /// <summary>Not found in the document.</summary>
    NotFound,

    /// <summary>The fact carries no span: nothing to verify.</summary>
    NoSpan,
}
