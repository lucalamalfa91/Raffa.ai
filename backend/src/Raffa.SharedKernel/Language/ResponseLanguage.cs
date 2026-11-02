namespace Raffa.SharedKernel.Language;

/// <summary>
/// The language the user chose in the interface (<c>it</c> | <c>en</c>) for the work in progress on this
/// logical call: it decides the language of everything Raffa writes in reply — the model's answer, the
/// deterministic fallback copy, the capability-gap and feedback cards — instead of guessing it from the
/// words of the question. Set once by the entry that received the request (<see cref="Use"/>); read by
/// anything that writes text. Flows through <c>await</c>s like any <see cref="AsyncLocal{T}"/> and never
/// leaks to another request. When nothing set it (<see cref="Current"/> is null), callers keep their
/// previous behaviour: the language of the question.
/// </summary>
public static class ResponseLanguage
{
    private static readonly AsyncLocal<string?> CurrentValue = new();

    /// <summary><c>it</c>, <c>en</c>, or null when no language was chosen for this call.</summary>
    public static string? Current => CurrentValue.Value;

    /// <summary>True for Italian, false for English, null when no language was chosen.</summary>
    public static bool? ChosenItalian => Current switch { "it" => true, "en" => false, _ => null };

    /// <summary>The model's instruction for the chosen language, or null when none was chosen.</summary>
    public static string? Directive => Current switch
    {
        "it" => "RESPONSE LANGUAGE (overrides any rule about the language of the question): write the whole reply in Italian, " +
                "whatever language the question is written in. Keep contract and supplier names, figures and dates as given.",
        "en" => "RESPONSE LANGUAGE (overrides any rule about the language of the question): write the whole reply in English, " +
                "whatever language the question is written in. Keep contract and supplier names, figures and dates as given.",
        _ => null,
    };

    /// <summary>Makes <paramref name="lang"/> the response language until the returned scope is disposed
    /// (anything other than <c>it</c> / <c>en</c> leaves the current choice untouched).</summary>
    public static IDisposable Use(string? lang)
    {
        var normalized = string.Equals(lang, "it", StringComparison.OrdinalIgnoreCase) ? "it"
            : string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase) ? "en"
            : null;
        var previous = CurrentValue.Value;
        if (normalized is not null)
        {
            CurrentValue.Value = normalized;
        }

        return new Restore(previous);
    }

    private sealed class Restore(string? previous) : IDisposable
    {
        public void Dispose() => CurrentValue.Value = previous;
    }
}
