using System.Text.Json;

namespace Contigo.AiEval;

/// <summary>
/// Loads the checked-in golden cases from <c>golden/*.json</c> (task E13/F06/US01/T02,
/// ask-golden-set). Prefers the source-tree copy so an edit to a case file is picked up without a
/// rebuild of anything but the JSON; falls back to the copy the csproj places next to the compiled
/// assembly when the source tree is not available.
/// </summary>
internal static class GoldenSet
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Lazy<IReadOnlyList<GoldenCase>> LazyCases = new(Load);

    /// <summary>Every case, ordered by file then by declaration order inside the file — the order
    /// the report lists them in, and the order they are evaluated in.</summary>
    public static IReadOnlyList<GoldenCase> Cases => LazyCases.Value;

    /// <summary>The fixed intent vocabulary a case may name — the nine
    /// <c>Contigo.Chat.Domain.AskIntent</c> members (R-ASK-03) plus the five gate labels that never
    /// reach the planner (R-ASK-02). Asserted by <c>GoldenSetTests</c>, so a typo in a JSON file
    /// surfaces as a failing test rather than as a silently uncounted intent in the coverage
    /// table.</summary>
    public static IReadOnlySet<string> KnownIntents { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "greeting", "off_domain", "legal", "capability", "needs_document",
        "structured_fact", "clause", "market_compare", "renewal_strategy",
        "portfolio_strategy", "savings", "document_status", "quote_route", "navigate",
    };

    public static IReadOnlySet<string> KnownKinds { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "answer", "abstain", "redirect", "refusal",
    };

    private static IReadOnlyList<GoldenCase> Load()
    {
        var directory = ResolveDirectory();

        var files = Directory
            .EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToList();

        if (files.Count == 0)
        {
            throw new InvalidOperationException(
                $"No golden-set case files found in '{directory}' — the AI evaluation set cannot run.");
        }

        var cases = new List<GoldenCase>();
        foreach (var file in files)
        {
            var parsed = JsonSerializer.Deserialize<List<GoldenCase>>(File.ReadAllText(file), JsonOptions)
                ?? throw new InvalidOperationException($"Golden-set file '{file}' deserialized to null.");

            cases.AddRange(parsed);
        }

        var duplicate = cases
            .GroupBy(c => c.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Golden-set case id '{duplicate.Key}' is declared more than once — ids are the " +
                "report row key and the xunit test-case name, so they must be unique.");
        }

        return cases;
    }

    private static string ResolveDirectory()
    {
        if (Directory.Exists(AiEvalOptions.GoldenDirectory))
        {
            return AiEvalOptions.GoldenDirectory;
        }

        if (Directory.Exists(AiEvalOptions.GoldenOutputDirectory))
        {
            return AiEvalOptions.GoldenOutputDirectory;
        }

        throw new DirectoryNotFoundException(
            $"Neither '{AiEvalOptions.GoldenDirectory}' nor '{AiEvalOptions.GoldenOutputDirectory}' " +
            "exists — the golden-set case files are missing.");
    }
}

/// <summary>
/// Every deviation between a requirement and the engine's current behaviour that the golden set is
/// allowed to tolerate, each one reported on every run. A <c>golden/*.json</c> case may only name
/// an id listed here (<c>GoldenSetTests.Every_known_gap_is_a_documented_gap</c> enforces it), so a
/// future case cannot quietly widen what the set forgives.
///
/// <para>
/// Task E13/F06/US01/T02 owns <c>backend/tests/Contigo.AiEval/**</c> and must not change the
/// engine, so each of these is reported rather than fixed here — see the "Engine gaps this run
/// exposed" section of <c>reports/last-run.md</c> and <c>backend/README.md</c> § "Ask V2 AI
/// evaluation set".
/// </para>
/// </summary>
internal static class GoldenSetKnownGaps
{
    /// <summary>
    /// <c>Contigo.Chat.Application.Gate.DomainGate.ExtractSupplierCandidate</c> takes the first
    /// capitalized run after position 0 as a supplier name. In English that catches the pronoun
    /// "I", so R-STR-01's own worked question ("How should I approach the Salesforce renewal?")
    /// resolves the candidate "I", finds no such supplier, and returns
    /// <c>needs_document</c> ("No I contract has been uploaded and validated…") instead of the
    /// renewal-strategy answer the requirement specifies.
    ///
    /// <para><b>Fixed.</b> The gate now considers every capitalized run, skips the ones that are
    /// never names (the English pronoun among them) and prefers a run this tenant already has
    /// contracts for. No case declares this gap any more; the id stays so the report of an older
    /// run still reads, and so a regression has a name.</para>
    /// </summary>
    public const string PronounReadAsSupplier = "GAP-ASK-PRONOUN-AS-SUPPLIER";

    /// <summary>
    /// <c>Contigo.Chat.Application.AskContigoQueryRouter</c>'s structured/semantic keyword lists
    /// are English-only ("renew", "expir", "spend", "next N days"), so an Italian dates/spend
    /// question — which the prototype's own intent table lists in the same row as its English twin
    /// ("expire / scad / end", "120 / days / giorni / renew / rinnov / scadono") — falls through to
    /// the semantic branch, retrieves nothing and abstains. The abstain is honest; the question was
    /// answerable.
    /// </summary>
    public const string ItalianStructuredBlind = "GAP-ASK-ITALIAN-STRUCTURED-BLIND";

    /// <summary>
    /// <c>Contigo.Api.AskCopilotService.BuildRoutingOnlyReply</c> hard-codes the Documents
    /// capability for every <c>navigate</c> turn, so "take me to renewals" offers <c>/documents</c>
    /// rather than the destination R-SYS-02's own routing table names.
    /// </summary>
    public const string NavigateAlwaysDocuments = "GAP-ASK-NAVIGATE-ALWAYS-DOCUMENTS";

    /// <summary>
    /// R-ASK-02 AC-3 requires a legal refusal to offer an action to Contract 360 for the contract
    /// the question named. <c>DomainGate</c> returns the <c>legal</c> label without a
    /// <c>NamedSupplier</c> (only the <c>in_domain</c> and <c>needs_document</c> branches carry
    /// one), so <c>BuildLegalReply</c> can never resolve that contract and always falls back to the
    /// Documents action.
    /// </summary>
    public const string LegalRefusalHasNoContractAction = "GAP-ASK-LEGAL-NO-CONTRACT-ACTION";

    /// <summary>
    /// <c>Contigo.Api.AskCopilotService.BuildContractFactItem</c> emits the annual-spend
    /// <c>PackValue</c> with <c>Currency = "n/a"</c> even though <c>Contract.Currency</c> is known,
    /// so a spend figure reaches <c>answerMarkdown</c> as "n/a 640000" instead of "CHF 640000".
    /// Not a grounding failure (the numeric guard is currency-aware and simply finds no currency
    /// token to check) but it is engineer-facing text in a user-visible reply (R-ASK-08) and it
    /// drops the currency R-PORT-02 requires on a total.
    /// </summary>
    public const string SpendPackValueLosesCurrency = "GAP-ASK-SPEND-CURRENCY-NA";

    /// <summary>
    /// An empty workspace still receives a calculator-backed <c>answer</c> for a renewal-window
    /// question ("0 contract(s) … auto-renew inside the window you asked about") rather than the
    /// upload invite R-PORT-02 AC-2 / R-ASK-10 describe for a portfolio with nothing validated.
    /// </summary>
    public const string EmptyPortfolioStillAnswers = "GAP-ASK-EMPTY-PORTFOLIO-ANSWERS";

    /// <summary>
    /// A <c>needs_review</c> contract is not distinguished from a validated one when a pack is
    /// built: R-CMP-03 requires Ask to name the weak facts that block the comparison, but the
    /// engine composes the same contract-fact item and answers from whatever fields happen to be
    /// present.
    /// </summary>
    public const string NeedsReviewNotFlagged = "GAP-ASK-NEEDS-REVIEW-NOT-FLAGGED";

    /// <summary>
    /// <c>Contigo.Api.InsightsEndpointExtensions.ComputeRenewal</c> builds its
    /// <c>ContractRenewalTerms</c> with <c>CancellationNoticeDays: null</c>, and
    /// <c>Contract</c> carries no notice-days column at all — only an already-computed
    /// <c>CancellationDeadline</c>, which that mapping never reads. So <c>RenewalEngine</c> reports
    /// "CancellationDeadline could not be determined", and the renewal-strategy pack's "When you
    /// must move" block carries the renewal date but never the notice deadline R-STR-01's own
    /// output structure names ("dates" → when you must move) and AC-2 requires ("a passed deadline
    /// is stated as passed, not hidden"). The deadline is known to the tenant — it is on the
    /// contract row and every structured-fact answer quotes it — it is simply dropped on the way
    /// into the strategy calculator.
    ///
    /// <para><b>Fixed.</b> <c>InsightsEndpointExtensions.ToStrategyInputs</c> falls back to the
    /// contract's own extracted <c>CancellationDeadline</c> when the engine derives none, and
    /// <c>AskCopilotService</c> now composes its strategy inputs through that same mapping instead
    /// of a private copy, so Ask and <c>GET /api/contracts/{id}/strategy</c> cannot drift.</para>
    /// </summary>
    public const string StrategyPackHasNoNoticeDeadline = "GAP-ASK-STRATEGY-NO-NOTICE-DEADLINE";

    /// <summary>
    /// The prototype's own intent table ends with "anything else → abstain: 'Nothing in the N
    /// validated contracts supports a reliable answer…'". <c>AskCopilotService
    /// .BuildStructuredFactPackAsync</c> instead falls back to a portfolio snapshot (the five
    /// soonest-ending contracts) for any question the deterministic planner cannot handle, and to a
    /// company-wide annual-spend total for any question merely containing the word "spend". So an
    /// off-topic question ("how much did we pay in legal fees last year?") receives a confident,
    /// cited answer about unrelated contracts rather than the honest decline.
    /// </summary>
    public const string UnanswerableStillAnswered = "GAP-ASK-UNANSWERABLE-STILL-ANSWERED";

    /// <summary>
    /// R-SUP-04: "Everywhere a supplier is shown, the name is used… never a bare SupplierId guid."
    /// <c>AskCopilotService</c> resolves the supplier name only where exactly one contract was
    /// named. Every multi-contract path titles its pack items with <c>item.Type.ToString()</c>
    /// instead — the deterministic renewal-window/annual-spend result, the portfolio snapshot, and
    /// <c>BuildDocumentStatusPack</c> — so a renewal list reads "OrderForm · OrderForm" rather than
    /// "DocuSign · OrderForm", and a document-status reply says "Msa — not yet askable" without
    /// saying whose. No guid is rendered, so nothing unsafe reaches the user; the supplier identity
    /// the requirement asks for is simply missing exactly where a list needs it most.
    /// </summary>
    public const string StructuredPackDropsSupplierName = "GAP-ASK-STRUCTURED-PACK-DROPS-SUPPLIER-NAME";

    /// <summary>
    /// <c>AskCopilotService.BuildPortfolioStrategyPackAsync</c> puts the criticality score into the
    /// pack twice in two different shapes: rounded to one decimal in the item title ("criticality
    /// 20.2/100") and at full <see cref="decimal"/> precision in its structured
    /// <c>PackValue</c> ("20.167985109353187529083294560"). Any consumer that renders the value
    /// rather than the title — the fixture gateway's own echo does exactly that — shows a
    /// 27-digit number to a user, which is the "fabricated precision" Appendix C rule 10 and
    /// R-ASK-08 both rule out. It does not trip the numeric guard, because that guard checks only
    /// amounts, percentages and dates, never a bare <c>Number</c> value.
    /// </summary>
    public const string UnroundedCalculatorValue = "GAP-ASK-UNROUNDED-CALC-VALUE";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        PronounReadAsSupplier,
        ItalianStructuredBlind,
        NavigateAlwaysDocuments,
        LegalRefusalHasNoContractAction,
        SpendPackValueLosesCurrency,
        EmptyPortfolioStillAnswers,
        NeedsReviewNotFlagged,
        StrategyPackHasNoNoticeDeadline,
        UnanswerableStillAnswered,
        StructuredPackDropsSupplierName,
        UnroundedCalculatorValue,
    };
}
