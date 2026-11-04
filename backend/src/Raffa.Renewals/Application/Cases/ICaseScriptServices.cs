using Raffa.SharedKernel.Text;

namespace Raffa.Renewals.Application.Cases;

/// <summary>
/// The service boundary of <see cref="RenewalCaseEngine"/>: the parts the design prototype scripted
/// for its demo scenario. The engine never calls an LLM, a mailbox or a document store itself — it
/// calls this interface and stays pure and deterministic. <see cref="FixtureCaseScriptServices"/>
/// reproduces the prototype exactly; a production implementation must honour the contract written on
/// each member (summarised here):
/// <list type="bullet">
/// <item><see cref="SampleReply"/> — demo only; production returns null-equivalent behaviour by never
/// offering the "use example reply" shortcut (return an empty string).</item>
/// <item><see cref="AnalyzeReply"/> — an LLM reading of the pasted supplier reply (prompt-versioned,
/// input hash stored, BACKEND-SPEC §2.6 <c>parsed_json</c>): one row per term with what the supplier
/// said, what the plan asked, a state (proposed | unclear | rejected | conflict) and any policy warning,
/// plus the term updates it implies. It must never mark a term "agreed" — only the owner does.</item>
/// <item><see cref="VerifyDraft"/> — agreed-vs-draft comparison of a received contract draft (extraction
/// pipeline + diff): one row per agreed point with the draft value, result (ok | diff | unread) and the
/// page/section it was read from. Unreadable pages are reported as <c>unread</c>, never guessed.</item>
/// <item><see cref="CheckSigned"/> — compares the signed file with the last verified draft.</item>
/// <item><see cref="StartExtraction"/> — enqueues the signed contract into the standard document
/// pipeline and returns the fields below the confidence threshold for the user to decide.</item>
/// <item><see cref="ExampleVendors"/>, <see cref="VendorOffer"/> — demo shortcuts of the alternatives
/// branch; production parses the offers the user pastes or uploads (quote extraction).</item>
/// <item><see cref="ExampleAddress"/> — demo only. Raffa never invents a recipient: production returns
/// null and the "Use example address" button disappears.</item>
/// <item><see cref="CounterPrice"/> — the unit price the supplier's position implies for a term
/// (from the analysed reply / the case target), used for the counter-proposal and the agreed terms.</item>
/// <item><see cref="Features"/> — the functions the contract covers, without product names (an LLM
/// task over the validated contract).</item>
/// </list>
/// </summary>
public interface ICaseScriptServices
{
    /// <summary>True only for the prototype's demo script (<see cref="FixtureCaseScriptServices"/>): the engine
    /// then writes the prototype's own texts and figures, word for word (prototype parity). Every other
    /// implementation gets texts and figures built from the case's readings and the user's decisions only.</summary>
    bool Scripted => false;

    /// <summary>Text "Use example reply" puts in the reply box for <paramref name="round"/> (1-based).</summary>
    string SampleReply(RenewalCase c, CaseContext ctx, int round);

    /// <summary>Reads reply <paramref name="rid"/> (<c>c.Reps[rid]</c>) against the case's plan and terms.</summary>
    CaseReplyAnalysis AnalyzeReply(RenewalCase c, CaseContext ctx, int rid);

    /// <summary>Compares contract draft <paramref name="version"/> (1-based) with <c>c.Agreed</c>.</summary>
    IReadOnlyList<CaseDraftRow> VerifyDraft(RenewalCase c, CaseContext ctx, int version);

    /// <summary>Compares the signed file with the last verified draft.</summary>
    CaseSignedCheck CheckSigned(RenewalCase c, CaseContext ctx);

    /// <summary>Starts extraction of the signed contract (stage 0, weak fields to decide).</summary>
    CasePipe StartExtraction(RenewalCase c, CaseContext ctx);

    /// <summary>Vendor names offered by "Add two examples".</summary>
    IReadOnlyList<string> ExampleVendors();

    /// <summary>The offer received from <paramref name="vendor"/>.</summary>
    CaseAltOffer VendorOffer(RenewalCase c, CaseContext ctx, string vendor, string by);

    /// <summary>Demo recipient for an email; null when not available (never in production).</summary>
    string? ExampleAddress(CaseEmail email, CaseContext ctx);

    /// <summary>Unit price the supplier would settle on for <paramref name="termMonths"/> (12 or 24).</summary>
    double CounterPrice(CaseContext ctx, int termMonths);

    /// <summary>Functions the new supplier must cover (category is null when unknown).</summary>
    CaseFeatureSet Features(CaseContext ctx);
}

/// <summary>Rows of the analysis card and the term updates the reading implies.</summary>
public sealed record CaseReplyAnalysis(
    IReadOnlyList<CaseReplyRow> Rows,
    IReadOnlyList<CaseTermUpdate> Terms,
    IReadOnlyDictionary<int, double>? Prices = null,
    CaseReplyBenchmark? Benchmark = null);

/// <summary>The commercial terms a supplier's reply states, as numbers: what the shared market benchmark learns from it.
/// Only what the reply says; null for anything it does not.</summary>
public sealed record CaseReplyBenchmark(double? DiscountPct, double? UpliftCapPct, int? NoticeDays, string? PaymentTerms, int? TermMonths);

/// <summary>Sets term <paramref name="Key"/> (created with <paramref name="Label"/> if missing).</summary>
public sealed record CaseTermUpdate(string Key, CaseText Label, string State, string? Val, string Ev);

/// <summary>Result of the signed-vs-last-draft comparison.</summary>
public sealed record CaseSignedCheck(int Points, int Differences);

/// <summary>
/// The functions of the current solution. <paramref name="CategoryEn"/> is the English category used
/// inside the RFQ email body ("electronic signature"), null when unknown.
/// </summary>
public sealed record CaseFeatureSet(string? CategoryEn, IReadOnlyList<CaseText> Items);
