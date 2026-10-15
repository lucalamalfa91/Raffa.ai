namespace Raffa.AiFlows.CapabilityGaps.Catalog;

/// <summary>
/// The fixed catalog of operations Ask Raffa cannot perform yet (ADR-030 D1) — five entries,
/// matched deterministically (regex only, it/en) by <c>Gate.DomainGate.Classify</c> right after the
/// legal lexicon and before the capability/how-to lexicon, so "can you help me send an email to
/// the supplier?" is recognised as a request for an operation, never as the feature tour
/// ("help") and never as a savings turn ("leverage"). First match wins, in catalog order:
/// <see cref="SendSupplierKey"/> is checked before <see cref="EmailDraftKey"/> so a question that
/// both writes and sends is recorded as the <em>send</em> gap (what is actually missing), while
/// both resolve to the same drafted-email alternative.
///
/// <para>Deliberately conservative: every pattern needs an operation verb and its object (or an
/// unmistakable noun such as "promemoria"), a supplier name alone is never enough, and the
/// send gap is vetoed by a timing question ("when must we send the notice?" is a notice-deadline
/// fact, R-ASK-03/NW-91). A missed gap costs the user one honest abstain; a false positive would
/// hijack a real question, so the lexicon errs on the side of missing.</para>
///
/// <para>INV-07 (F4-T08): the entries — texts, alternative and the include/exclude lexicons — are
/// versioned data in <c>gap-catalogue.json</c> (an embedded resource with a changelog), loaded once
/// by <see cref="GapCatalogueData"/>; this class keeps the stable keys and the matching API.</para>
/// </summary>
public static class CapabilityGapCatalog
{
    public const string SendSupplierKey = "send-supplier";
    public const string EmailDraftKey = "email-draft";
    public const string ReminderKey = "reminder";
    public const string ExportFileKey = "export-file";
    public const string PurchaseOrderKey = "purchase-order";

    internal const string ResourceName = "Raffa.AiFlows.CapabilityGaps.gap-catalogue.json";

    private static readonly Lazy<GapCatalogueData> Data = new(GapCatalogueData.LoadEmbedded);

    /// <summary>The <c>version</c> of the loaded catalogue file.</summary>
    public static string Version => Data.Value.Version;

    public static IReadOnlyList<CapabilityGap> All => Data.Value.Entries;

    /// <summary>The first catalog entry whose lexicon matches <paramref name="question"/>, or
    /// <see langword="null"/> when the question asks for nothing Raffa cannot do. Pure.</summary>
    public static CapabilityGap? Match(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        var trimmed = question.Trim();
        return All.FirstOrDefault(gap => gap.Matches(trimmed));
    }

    public static CapabilityGap? Find(string key) =>
        All.FirstOrDefault(gap => string.Equals(gap.Key, key, StringComparison.Ordinal));
}
