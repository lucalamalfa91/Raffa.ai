using System.Text.RegularExpressions;

namespace Raffa.AiFlows.Ask.Agentic;

/// <summary>
/// "Avvia il rinnovo", "aprilo in rinnovi", "intervistami per il rinnovo", "invia la mail per il rinnovo",
/// "start the renewal", "open it in renewals", "interview me for the renewal", "send the renewal email": a
/// command to start the renewal of the contract the chat is about, recognised before the model runs so it can
/// never turn into an email written in the chat or a button to another screen. The renewal case then opens
/// with this conversation's context (the levers, the user's own constraints) and runs its interview there.
/// A question about the renewal ("come avvio il rinnovo?", "should we renew?"), a negation ("non avviarlo
/// ancora") or a sentence that only mentions a renewal ("the start date of the renewal") is not a command:
/// the verb must open a clause, in a command form.
/// </summary>
public static partial class RenewalStartCommand
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>Where a command verb may stand: the start of the text or of a clause, or after a request lead
    /// ("ok,", "puoi", "allora", "please", "let's", "can you").</summary>
    private const string Lead =
        @"(?:^|[.!;:\n,]|\b(?:e|ed|ora|adesso|allora|quindi|ok|okay|bene|perfetto|ottimo|puoi|potresti|riesci\s+a|per\s+favore|please|now|then|so|and|great|good|let['’]?s|can\s+you|could\s+you|would\s+you|vorrei|voglio|vogliamo|want\s+to|i['’]?d\s+like\s+to|go\s+ahead\s+and)\b)\s*";

    /// <summary>A renewal noun: rinnovo / rinnovi, renewal(s).</summary>
    private const string Renewal = @"(?:rinnov[oi]|renewals?)";

    /// <summary>A few words between the verb and what it acts on ("avvia la pratica di rinnovo", "start the Workday renewal").</summary>
    private const string Gap = @"(?:[\s,]+[\w'’·.-]+){0,6}?[\s,]+";

    private const string StartVerb =
        @"(?:avvia(?:lo|la|mo|te|re)?|avviamolo|inizia(?:lo|la|mo|te|re)?|comincia(?:mo|re)?|fa(?:i|te)\s+partire|far\s+partire|facciamo\s+partire|lancia(?:lo|la|mo|re)?|procedi(?:amo)?\s+con|parti(?:amo)?\s+con|start|begin|kick\s+off|launch|initiate|proceed\s+with|go\s+ahead\s+with|get\s+started\s+(?:on|with))";

    private const string MoveVerb =
        @"(?:apri(?:lo|la|re|te|amo)?|metti(?:lo|la)?|mettiamo(?:lo|la)?|sposta(?:lo|la|re)?|spostiamo(?:lo|la)?|porta(?:lo|la)?|portiamo(?:lo|la)?|segna(?:lo|la)?|segniamo(?:lo|la)?|imposta(?:lo|la)?|open|move|put|mark|set|take)";

    private const string OpenVerb = @"(?:apri(?:lo|la|re|te|amo)?|crea(?:la|re)?|open|create)";

    private const string SendVerb = @"(?:invia(?:la|mo|re)?|manda(?:la|mo|re)?|spedisci(?:la)?|send|email)";

    // avvia il rinnovo / avvia la pratica di rinnovo / start the renewal / kick off the Workday renewal
    [GeneratedRegex(Lead + StartVerb + @"\b" + Gap + Renewal + @"\b", Options)]
    private static partial Regex StartVerbThenRenewal();

    // "rinnoviamo Workday", "let's renew", "renew it", "rinnovalo"
    [GeneratedRegex(Lead + @"(?:rinnoviamo|rinnoval[oa]|rinnova\s+(?:il|lo|la|questo|questa)|renew\s+(?:it|this|the\s+contract))\b", Options)]
    private static partial Regex RenewVerb();

    // aprilo in rinnovi / mettilo in rinnovo / spostalo nei rinnovi / segnalo come in rinnovo /
    // open it in renewals / move it to renewals / mark it as in renewal
    [GeneratedRegex(
        Lead + MoveVerb + @"\b(?:[\s,]+[\w'’·.-]+){0,5}?[\s,]+(?:in|nei|nel|nella|tra\s+i|fra\s+i|come|a|to|into|as|under)\s+(?:the\s+|i\s+|il\s+)?(?:stato\s+)?(?:in\s+)?(?:rinnov[oi]|renewals?|rinnovi|renewal)\b",
        Options)]
    private static partial Regex MoveToRenewals();

    // apri la pratica di rinnovo / open the renewal case / apri il rinnovo / open the renewal
    [GeneratedRegex(
        Lead + OpenVerb + @"\b" + Gap + @"(?:pratica|case|wizard|flusso|flow)\s+(?:di|del|per\s+il|for\s+the|of\s+the)?\s*" + Renewal + @"\b"
        + @"|" + Lead + OpenVerb + @"\b" + Gap + Renewal + @"\s+(?:case|wizard|flow)\b"
        + @"|" + Lead + @"(?:apri|aprite|apriamo|open)\s+(?:il|lo|the)\s+" + Renewal + @"\b",
        Options)]
    private static partial Regex OpenRenewalCase();

    // intervistami per il rinnovo / fammi le domande del rinnovo / interview me for the renewal
    [GeneratedRegex(
        Lead + @"(?:intervista(?:mi|ci)|intervistare|fammi\s+le\s+domande|facciamo\s+l['’]\s*intervista|interview\s+(?:me|us)|ask\s+me\s+the(?:\s+renewal)?\s+questions|start\s+the\s+interview)\b"
        + @"(?:" + Gap + Renewal + @"\b)?",
        Options)]
    private static partial Regex InterviewForRenewal();

    // invia / manda la mail per il rinnovo, send the renewal email: the case writes it after the interview.
    [GeneratedRegex(
        Lead + SendVerb + @"\b(?:[\s,]+[\w'’·.-]+){0,4}?[\s,]+(?:e-?mail|mail)\b(?:[\s,]+[\w'’·.-]+){0,4}?[\s,]+" + Renewal + @"\b"
        + @"|" + Lead + SendVerb + @"\b(?:[\s,]+[\w'’·.-]+){0,4}?[\s,]+" + Renewal + @"\s+(?:e-?mail|mail)\b",
        Options)]
    private static partial Regex SendRenewalEmail();

    // An interview command must be about the renewal: either the noun is in the text or the chat is already about a contract.
    [GeneratedRegex(@"\b(?:rinnov\w*|renew\w*)\b", Options)]
    private static partial Regex MentionsRenewal();

    private const string QuestionWord =
        @"(?:come|quando|perch[eé]|dovrei|dovremmo|devo|conviene|ha\s+senso|cosa|qual\w*|what|when|why|how|should|shall|whether|is\s+it|does\s+it|do\s+we\s+need)\b";

    [GeneratedRegex(@"^\s*(?:(?:ma|e|allora|so|and|but|ok)[\s,]+)?" + QuestionWord, Options)]
    private static partial Regex StartsAsQuestion();

    [GeneratedRegex(@"(?:^|[\s,.;:])" + QuestionWord, Options)]
    private static partial Regex HasQuestionWord();

    // "non avviare il rinnovo", "don't start the renewal", "senza aprirlo in rinnovi", "not yet": the negation bears on the command itself.
    [GeneratedRegex(
        @"\b(?:non|senza|mai|don['’]?t|do\s+not|never|without)\b(?:[\s,]+[\w'’]+){0,2}?[\s,]+(?:avvi|apri|inizi|lanci|metti|sposta|invi|mand|rinnov|intervist|start|open|begin|launch|send|renew|interview|move|mark)\w*"
        + @"|\b(?:not\s+yet|not\s+now|ancora\s+no|non\s+ancora|non\s+ora|aspetta|wait)\b",
        Options)]
    private static partial Regex Negation();

    // A polite request is still a command: "puoi avviare il rinnovo?", "can you open it in renewals?"
    [GeneratedRegex(@"^\s*(?:puoi|potresti|possiamo|riesci\s+a|mi\s+(?:avvii|apri)|can\s+you|could\s+you|would\s+you|please|per\s+favore)\b", Options)]
    private static partial Regex PoliteRequest();

    /// <summary>True when <paramref name="text"/> asks Raffa to start the renewal now (IT or EN).</summary>
    public static bool IsCommand(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 600)
        {
            return false;
        }

        var commanded = StartVerbThenRenewal().IsMatch(text)
            || RenewVerb().IsMatch(text)
            || MoveToRenewals().IsMatch(text)
            || OpenRenewalCase().IsMatch(text)
            || (InterviewForRenewal().IsMatch(text) && MentionsRenewal().IsMatch(text))
            || SendRenewalEmail().IsMatch(text);
        if (!commanded || Negation().IsMatch(text))
        {
            return false;
        }

        // "come avvio il rinnovo?" asks how; "puoi avviare il rinnovo?" asks Raffa to do it; "avvia il rinnovo come
        // abbiamo detto" is a command that happens to contain "come".
        if (PoliteRequest().IsMatch(text))
        {
            return true;
        }

        return !StartsAsQuestion().IsMatch(text) && !(text.Contains('?') && HasQuestionWord().IsMatch(text));
    }
}
