namespace Raffa.Chat.Application.Interview;

/// <summary>
/// <c>Chat:Interview</c> — the kill switch and bounds of the interview (ADR-030). With
/// <see cref="Enabled"/> false every path behaves exactly as before the interview existed.
/// </summary>
public sealed class InterviewOptions
{
    public const string SectionName = "Chat:Interview";

    public bool Enabled { get; set; } = true;

    /// <summary>Questions per interview turn (1–3).</summary>
    public int MaxQuestions { get; set; } = 2;

    /// <summary>Options per question, the web-research offer included.</summary>
    public int MaxOptionsPerQuestion { get; set; } = 4;

    /// <summary>Stage 2: ask the analyst role for interpretations when the deterministic stage is
    /// unsure. Off by default — the deterministic signals decide alone until the golden set says
    /// they are not enough.</summary>
    public bool UseModelStage { get; set; }

    /// <summary>A question with at most this many words, no supplier and no scope counts as
    /// "short and unscoped" (an <em>unsure</em> signal, never an ambiguous one on its own).</summary>
    public int ShortQuestionMaxWords { get; set; } = 6;

    /// <summary>Ask "which contract?" when a named supplier has several instead of silently
    /// picking the soonest deadline (NW-80's fallback stays when this is off).</summary>
    public bool AskWhichContract { get; set; } = true;

    /// <summary>Ask "which contract?" for a notice question with no contract in scope instead of
    /// abstaining to Portfolio (supersedes NW-94 case 5 for the unscoped case only).</summary>
    public bool AskOnUnscopedNotice { get; set; } = true;

    /// <summary>Stage 3: turn a model abstain whose reason says "ambiguous" into the
    /// interpretation interview instead of the abstain block.</summary>
    public bool ConvertAmbiguousAbstain { get; set; } = true;
}
