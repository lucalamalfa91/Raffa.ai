using System.Text.RegularExpressions;

namespace Raffa.AiFlows.CapabilityGaps.Drafting;

/// <summary>The kind of communication a user asked to have drafted (plan F4: three types).</summary>
public enum DraftType
{
    /// <summary>Renegotiate the terms before renewing — what every draft has been until now, and
    /// the default for any request that names no other type.</summary>
    Renegotiation = 0,

    /// <summary>Give notice that the contract will not be renewed, or terminate it ("disdetta",
    /// "non rinnovo", "cancel", "terminate").</summary>
    CancellationNonRenewal = 1,

    /// <summary>Answer a supplier's proposal or offer with a counter-proposal.</summary>
    CounterProposal = 2,
}

/// <summary>
/// The deterministic draft-type classifier (no model call): a lexicon over the user's question in
/// the five languages, cancellation first (the most specific and the costliest to get wrong), then
/// counter-proposal, otherwise <see cref="DraftType.Renegotiation"/>. It labels the draft
/// (<see cref="DraftOutcome.Type"/>, the chip the reply can show); it does not change what is
/// written — the prompts and the template are versioned (draft-v1, draft-template-v1) and stay
/// byte-identical, so a cancellation request is still drafted by the same writer until a
/// per-type template exists (LANG-07).
/// </summary>
public static class DraftTypeClassifier
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex Cancellation = new(
        @"\b(disdett\w*|recesso|recedere|non\s+(voglio\s+|vogliamo\s+|intendo\s+|intendiamo\s+)?rinnovar\w*|non\s+rinnovo|mancato\s+rinnovo|" +
        @"cessazione\s+del\s+contratto|risoluzione\s+del\s+contratto|" +
        @"cancel\w*|terminat\w*|termination|not\s+(to\s+)?renew\w*|non-?renewal|opt\s+out|" +
        @"résili\w*|ne\s+pas\s+renouveler|non-?renouvellement|" +
        @"rescind\w*|rescisi[oó]n|no\s+renovar|cancelaci[oó]n|dar\s+de\s+baja|" +
        @"k[uü]ndig\w*|nicht\s+verl[aä]ngern|vertragsende)\b", Options);

    private static readonly Regex Counter = new(
        @"\b(contro-?propost\w*|contro-?offert\w*|rispond\w*\s+(alla|all'|ad\s+una)\s+(propost|offert)\w*|risposta\s+(alla|all')\s*(propost|offert)\w*|" +
        @"counter-?(proposal|offer)s?|respond\w*\s+to\s+(their|the|his|her)\s+(proposal|offer|quote)|reply\s+to\s+(their|the)\s+(proposal|offer)|" +
        @"contre-?proposition|contre-?offre|contrapropuesta|contraoferta|gegenvorschlag|gegenangebot)\b", Options);

    /// <summary>The type of communication <paramref name="question"/> asks for. Pure.</summary>
    public static DraftType Classify(string? question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return DraftType.Renegotiation;
        }

        if (Cancellation.IsMatch(question))
        {
            return DraftType.CancellationNonRenewal;
        }

        return Counter.IsMatch(question) ? DraftType.CounterProposal : DraftType.Renegotiation;
    }
}
