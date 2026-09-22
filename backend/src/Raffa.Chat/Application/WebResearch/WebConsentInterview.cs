using Raffa.Chat.Application.Interview;
using Raffa.Chat.Domain;

namespace Raffa.Chat.Application.WebResearch;

/// <summary>
/// The consent turn of ADR-030: one interview question with the <see cref="InterviewPresentation.Consent"/>
/// presentation (the SPA renders it as an alert dialog) and exactly two options. <c>allow</c>
/// carries the server-authored <see cref="WebResearchRequest"/> — the only way an authorisation
/// ever reaches the engine — and is single-use (the endpoint stamps it; a replay is a 409).
/// <c>decline</c> re-plans the same words without the "search the web" phrase, so the turn is
/// answered from the tenant's contracts alone. Neither option is ever persisted as a preference:
/// every web request asks again.
/// </summary>
public static class WebConsentInterview
{
    public const string QuestionKey = "web-consent";
    public const string AllowOptionKey = "allow";
    public const string DeclineOptionKey = "decline";

    public static InterviewTurn Build(string question, WebResearchRequest offer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(offer);

        var italian = LanguageHint.IsItalian(question);
        var trimmed = question.Trim();
        var declined = DeclinedRewrite(trimmed);

        var prompt = italian
            ? $"Raffa cercherà sul web pubblico: “{offer.Query}”. Nulla dei tuoi contratti esce da Raffa. I risultati non sono verificati. Consenti?"
            : $"Raffa will search the public web for: “{offer.Query}”. Nothing from your contracts leaves Raffa. The results are not verified. Allow?";

        var allow = new InterviewOption(
            AllowOptionKey,
            italian ? "Sì, cerca sul web" : "Yes, search the web",
            italian ? "Una sola ricerca, solo per questa domanda." : "One search, for this question only.",
            new InterviewResolution(AskIntent.WebResearch, null, null, trimmed, offer));

        var decline = new InterviewOption(
            DeclineOptionKey,
            italian ? "No, resta in Raffa" : "No, stay in Raffa",
            italian ? "Rispondo solo con i tuoi contratti." : "I answer from your contracts only.",
            new InterviewResolution(null, null, null, declined));

        var consent = new InterviewQuestion(QuestionKey, prompt, InterviewPresentation.Consent, AllowFreeText: false, [allow, decline]);

        return new InterviewTurn(prompt, [consent]);
    }

    /// <summary>The words the declined turn is planned on: the same question without the explicit
    /// "search the web" phrase, so the planner's other lexicons decide; the original when nothing
    /// usable remains.</summary>
    public static string DeclinedRewrite(string question)
    {
        var stripped = WebResearchTopicLexicon.StripExplicitRequest(question).Trim().Trim(',', ':', ';', '-', '–', '—', ' ');
        var words = stripped.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= WebQuerySanitizer.MinWords ? string.Join(' ', words) : question.Trim();
    }
}
