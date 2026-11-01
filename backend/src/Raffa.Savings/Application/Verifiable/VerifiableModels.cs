using Raffa.SharedKernel.Text;

namespace Raffa.Savings.Application.Verifiable;

/// <summary>
/// A quote (offer) to check — the prototype's <c>SQ.QUOTES[i]</c> / <c>SQ.fromCase</c> shape. A line
/// carries <see cref="QuoteLine.Existing"/> only when the customer's own current contract states a
/// price for the same thing. There is no market benchmark anywhere in this model.
/// </summary>
public sealed record Quote
{
    public string Id { get; init; } = string.Empty;

    public string Ref { get; init; } = string.Empty;

    public string Supplier { get; init; } = string.Empty;

    /// <summary>Proposal file / source ("Databricks_Proposal_Q-88213.pdf", "DocuSign reply · 08 Sep 2026").</summary>
    public string File { get; init; } = string.Empty;

    /// <summary>new | renewal.</summary>
    public string Kind { get; init; } = "new";

    public int? Cid { get; init; }

    /// <summary>Date the quote was received / checked.</summary>
    public DateOnly? Date { get; init; }

    public string? By { get; init; }

    public string Currency { get; init; } = "CHF";

    /// <summary>"24 months".</summary>
    public string? Term { get; init; }

    /// <summary>Offer valid until.</summary>
    public DateOnly? Valid { get; init; }

    /// <summary>checked | negotiating | signing | signed | declined.</summary>
    public string Status { get; init; } = "checked";

    public IReadOnlyList<QuoteLine> Lines { get; init; } = [];

    /// <summary>What the proposal states (null = not stated).</summary>
    public QuoteStated? Stated { get; init; }

    /// <summary>What the proposal omits.</summary>
    public IReadOnlyList<CaseText> Gaps { get; init; } = [];

    /// <summary>The target already recorded for this quote (used when the options give none).</summary>
    public QuoteTarget? Target { get; init; }
}

public sealed record QuoteLine
{
    public string Name { get; init; } = string.Empty;

    public string Sku { get; init; } = string.Empty;

    public double Qty { get; init; }

    /// <summary>"seat / yr", "user / mo" (a unit containing "/ mo" is multiplied by 12).</summary>
    public string Unit { get; init; } = string.Empty;

    public double Price { get; init; }

    /// <summary>The customer's own current contract price for the same line, when known.</summary>
    public double? Existing { get; init; }

    /// <summary>Where <see cref="Existing"/> comes from ("Your current price", "2024 contract price").</summary>
    public string? ExistingLabel { get; init; }
}

public sealed record QuoteStated(string? Payment, string? Uplift, string? Renewal, string? Liability, string? Sla);

/// <summary>A user target: none | p5 | p10 | budget | custom.</summary>
public sealed record QuoteTarget(string Kind, double? Budget = null, string? Label = null);

/// <summary>Another offer actually received for the same need.</summary>
public sealed record OtherOffer(string Name, double Total, int Term);

public sealed record QuotePolicy(int LiabMin);

/// <summary>
/// <c>SQ.assess(q, o)</c> options: the user's target, the renewal plan's term, other offers received,
/// the contract's notice date and the company policy.
/// </summary>
public sealed record AssessOptions
{
    public QuoteTarget? Target { get; init; }

    /// <summary>"12" | "24" | "36".</summary>
    public string? PlanTerm { get; init; }

    public IReadOnlyList<OtherOffer> Others { get; init; } = [];

    public DateOnly? NoticeDate { get; init; }

    public QuotePolicy? Policy { get; init; }
}

public static class QuoteVerdicts
{
    public const string NoCompare = "nocompare";
    public const string MissesTarget = "misses_target";
    public const string MeetsTarget = "meets_target";
    public const string BelowCurrent = "below_current";
    public const string AboveCurrent = "above_current";
    public const string Same = "same";
}

public sealed record AssessedLine(QuoteLine Line, double Annual, double? CurrentAnnual, double? Delta, string Pos);

/// <summary>An evidence point: kind price | target | term | policy | gap | validity | other; tone ok | alert | neutral.</summary>
public sealed record EvidencePoint(string Kind, string Tone, CaseText Text, CaseText Src);

/// <summary>One line compared with the customer's own contract price (never a market price).</summary>
public sealed record LineComparison(string Name, double Quoted, double Current, double DeltaPct, string Unit, CaseText Source);

public sealed record QuoteAssessment
{
    public required IReadOnlyList<AssessedLine> Lines { get; init; }

    public double Total { get; init; }

    public double? CurTotal { get; init; }

    /// <summary>Only some lines have a current contract price.</summary>
    public bool Partial { get; init; }

    /// <summary>Total vs current contract, in %.</summary>
    public double? Own { get; init; }

    public QuoteTarget? Target { get; init; }

    public double? TargetTotal { get; init; }

    public bool? TargetMet { get; init; }

    /// <summary>What is missing for a complete comparison.</summary>
    public required IReadOnlyList<CaseText> Missing { get; init; }

    /// <summary>current | others | none.</summary>
    public required string Basis { get; init; }

    /// <summary>See <see cref="QuoteVerdicts"/>.</summary>
    public required string Verdict { get; init; }

    /// <summary>At most five evidence points, each with its source.</summary>
    public required IReadOnlyList<EvidencePoint> Points { get; init; }

    public required IReadOnlyList<CaseText> Gaps { get; init; }

    public required IReadOnlyList<OtherOffer> Others { get; init; }

    /// <summary>What can still be checked when nothing is comparable.</summary>
    public required IReadOnlyList<CaseText> Evaluable { get; init; }

    public required IReadOnlyList<LineComparison> Comparisons { get; init; }
}
