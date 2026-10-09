namespace Raffa.Quotes.Application.Extraction;

/// <summary>
/// Closed set of reasons a quote extraction run ends in <c>Failed</c> (task F6-T02). Stored as a
/// stable prefix of <c>QuoteExtractionJob.ErrorDetail</c> (see <see cref="QuoteExtractionFailure"/>)
/// so an operator or a later reprocess policy can tell "the file could not be read" from "the model
/// endpoint is down" from "the client went away" without parsing free text, and without a schema
/// change (the column already exists and is free text).
/// </summary>
public enum QuoteExtractionFailureKind
{
    /// <summary>The hybrid parser reported a failure, or threw, while reading the file.</summary>
    ParseFailed,

    /// <summary>The file parsed but contained no readable page at all.</summary>
    NoReadableText,

    /// <summary>The AI Gateway <c>extract</c> role reported a failure, or threw.</summary>
    ExtractionFailed,

    /// <summary>The model returned a payload that is not the schema's JSON.</summary>
    MalformedPayload,

    /// <summary>Persisting the extracted lines (or normalizing them) threw.</summary>
    PersistenceFailed,

    /// <summary>The caller's request was cancelled (client disconnect, host shutdown) mid-run.</summary>
    Cancelled,

    /// <summary>Anything else that threw outside the stages above.</summary>
    Unexpected,
}

/// <summary>
/// Formats and parses the typed <c>ErrorDetail</c> of a failed <c>QuoteExtractionJob</c>: the text
/// is <c>[Kind] detail</c>, truncated to the column's 1000-character budget. Typed, not structured
/// JSON, on purpose — no new column, no migration, and still greppable in the database.
/// </summary>
public static class QuoteExtractionFailure
{
    /// <summary>Length budget of <c>QuoteExtractionJob.ErrorDetail</c> (see its configuration).</summary>
    public const int MaxErrorDetailLength = 1000;

    public static string Format(QuoteExtractionFailureKind kind, string? detail)
    {
        var text = string.IsNullOrWhiteSpace(detail) ? kind.ToString() : $"[{kind}] {detail.Trim()}";
        return text.Length <= MaxErrorDetailLength ? text : text[..MaxErrorDetailLength];
    }

    /// <summary>
    /// Reads the kind back from a stored <c>ErrorDetail</c>; <see langword="false"/> for a value
    /// written before this typing existed (or by a different writer).
    /// </summary>
    public static bool TryParseKind(string? errorDetail, out QuoteExtractionFailureKind kind)
    {
        kind = default;
        if (string.IsNullOrEmpty(errorDetail) || errorDetail.Length < 3 || errorDetail[0] != '[' || !char.IsLetter(errorDetail[1]))
        {
            return false;
        }

        var close = errorDetail.IndexOf(']');
        return close > 1
            && Enum.TryParse(errorDetail.AsSpan(1, close - 1), ignoreCase: false, out kind)
            && Enum.IsDefined(kind);
    }
}
