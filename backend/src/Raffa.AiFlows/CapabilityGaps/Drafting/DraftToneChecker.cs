using System.Text.RegularExpressions;

namespace Raffa.AiFlows.CapabilityGaps.Drafting;

/// <summary>
/// The formal / legal-tone check of a drafted email (plan 3.4, F4 "controllore legale-formale"),
/// deterministic and without a model call. The draft is a copyable text a buyer sends to a supplier
/// under their own name (decision D4), so it must read as a formal business letter and must not bind
/// the customer or leak the platform's own notes. Findings are stable codes:
/// <list type="bullet">
/// <item><c>informal-greeting</c> — opens with "ciao", "hey", "hi"...</item>
/// <item><c>no-greeting</c> — no formal salutation in the opening line.</item>
/// <item><c>exclamation</c> — "!!" or more than one "!" in the body.</item>
/// <item><c>emoji</c> — any pictographic character.</item>
/// <item><c>commitment</c> — an acceptance, admission or guarantee the customer did not make
/// ("we accept", "ci impegniamo", "we admit liability"...): a draft proposes, it never commits.</item>
/// <item><c>internal-note</c> — a platform/internal marker left in the text ("TODO", "internal note").</item>
/// <item><c>no-signature-placeholder</c> — no bracketed signature placeholder for the sender.</item>
/// </list>
/// By default the findings are only reported on the draft (<see cref="DraftOutcome.ToneFindings"/>);
/// <see cref="DraftingOptions.EnforceTone"/> additionally sends the writer back once.
/// </summary>
public static class DraftToneChecker
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex InformalGreeting = new(
        @"^\s*(ciao|ehi|hey|hi|hello there|yo|salve a tutti|bonjour à tous|hola a todos|hallo zusammen)\b", Options);

    private static readonly Regex Greeting = new(
        @"\b(gentil\w*|egregi\w*|spettabil\w*|buongiorno|buonasera|dear|good\s+(morning|afternoon|evening)|" +
        @"madame|monsieur|cher|chère|estimad\w*|querid\w*|sehr\s+geehrte\w*|liebe\w*)\b", Options);

    private static readonly Regex Emoji = new(@"[\p{So}\p{Cs}]", RegexOptions.CultureInvariant);

    // An acceptance, admission or guarantee: wording that makes the sender a party to something.
    private static readonly Regex Commitment = new(
        @"\b(we\s+(hereby\s+)?(accept|agree\s+to|admit|acknowledge\s+(fault|liability)|guarantee|will\s+sign|commit\s+to)|" +
        @"i\s+(hereby\s+)?(accept|agree\s+to|admit|guarantee)|" +
        @"accettiamo|accetto|ci\s+impegniamo|mi\s+impegno|riconosciamo\s+(la\s+nostra|di\s+aver)|ammettiamo|garantiamo|" +
        @"confermiamo\s+(il\s+rinnovo|l'ordine|la\s+firma)|firmeremo|" +
        @"nous\s+(acceptons|nous\s+engageons|garantissons)|aceptamos|nos\s+comprometemos|garantizamos|" +
        @"wir\s+(akzeptieren|verpflichten\s+uns|garantieren))\b", Options);

    private static readonly Regex InternalNote = new(
        @"(\bTODO\b|\bFIXME\b|\binternal\s+note\b|\bnote\s+intern[ae]\b|\bnota\s+interna\b|\blorem\s+ipsum\b|\bbozza\s+generata\b|<[^>]+>)", Options);

    private static readonly Regex SignaturePlaceholder = new(@"\[[^\]\r\n]{2,}\]", RegexOptions.CultureInvariant);

    /// <summary>The findings for a draft, empty when it reads as a formal, non-binding business
    /// letter. Pure.</summary>
    public static IReadOnlyList<string> Check(string? subject, string? body)
    {
        var text = (body ?? string.Empty).Trim();
        var findings = new List<string>();
        if (text.Length == 0)
        {
            return findings;
        }

        var firstLine = text.Split('\n', 2)[0];
        if (InformalGreeting.IsMatch(firstLine))
        {
            findings.Add("informal-greeting");
        }
        else if (!Greeting.IsMatch(firstLine))
        {
            findings.Add("no-greeting");
        }

        var all = (subject ?? string.Empty) + "\n" + text;
        if (text.Contains("!!", StringComparison.Ordinal) || text.Count(c => c == '!') > 1)
        {
            findings.Add("exclamation");
        }

        if (Emoji.IsMatch(all))
        {
            findings.Add("emoji");
        }

        if (Commitment.IsMatch(all))
        {
            findings.Add("commitment");
        }

        if (InternalNote.IsMatch(all))
        {
            findings.Add("internal-note");
        }

        if (!SignaturePlaceholder.IsMatch(text))
        {
            findings.Add("no-signature-placeholder");
        }

        return findings;
    }

    /// <summary>The violation text naming the findings, for the writer's retry.</summary>
    public static string Describe(IReadOnlyList<string> findings) =>
        "the draft is not a formal, non-binding business letter (" + string.Join(", ", findings) + ").";

    /// <summary>The retry addendum for a tone rejection — grounding rules are unchanged.</summary>
    public static string BuildRetryInstruction(string violation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(violation);

        return
            "Your previous draft was rejected by an automated tone check: " + violation +
            " Write it again as a formal, courteous business letter: a formal salutation, no exclamation " +
            "marks or emoji, no acceptance, admission or guarantee on the customer's behalf (the email only " +
            "proposes and asks), no internal notes, and a signature placeholder in square brackets. " +
            "Keep using ONLY the numbers, dates and citation keys already present in the input.";
    }
}
