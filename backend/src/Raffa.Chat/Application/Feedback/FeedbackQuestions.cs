namespace Raffa.Chat.Application.Feedback;

/// <summary>The fixed vocabulary of the three interview questions — the keys the card submits and
/// the server validates (<see cref="FeedbackService"/>), so a client cannot invent a fourth
/// question or a fifth choice.</summary>
public static class FeedbackQuestions
{
    public const string TextKind = "text";
    public const string ChoiceKind = "choice";

    public const string WhatKey = "what";
    public const string FrequencyKey = "frequency";
    public const string ImportanceKey = "importance";

    public const string FrequencyEveryRenewal = "every-renewal";
    public const string FrequencyWeekly = "weekly";
    public const string FrequencySometimes = "sometimes";

    public const string ImportanceBlocking = "blocking";
    public const string ImportanceVeryUseful = "very-useful";
    public const string ImportanceNiceToHave = "nice-to-have";

    /// <summary>The free-text answer is capped so a public issue body stays short and a pasted
    /// contract cannot travel through it by accident.</summary>
    public const int WhatMaxLength = 500;

    public static IReadOnlySet<string> FrequencyKeys { get; } =
        new HashSet<string>(StringComparer.Ordinal) { FrequencyEveryRenewal, FrequencyWeekly, FrequencySometimes };

    public static IReadOnlySet<string> ImportanceKeys { get; } =
        new HashSet<string>(StringComparer.Ordinal) { ImportanceBlocking, ImportanceVeryUseful, ImportanceNiceToHave };

    /// <summary>English labels for the issue body (the devs' language), keyed by choice key.</summary>
    public static string LabelFor(string choiceKey) => choiceKey switch
    {
        FrequencyEveryRenewal => "at every renewal",
        FrequencyWeekly => "every week",
        FrequencySometimes => "now and then",
        ImportanceBlocking => "blocking",
        ImportanceVeryUseful => "very useful",
        ImportanceNiceToHave => "nice to have",
        _ => choiceKey,
    };
}
