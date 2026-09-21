using System.Text;
using System.Text.RegularExpressions;
using Raffa.Chat.Domain;

namespace Raffa.Chat.Application.Gate;

/// <summary>
/// One supplier this tenant already has at least one contract for, as the composition root
/// (<c>Raffa.Api.AskCopilotService</c>) resolves it before calling <see cref="DomainGate.Classify"/>
/// (task E27/F05/US01/T01, NW-80). <see cref="DisplayName"/> is
/// <c>Raffa.Suppliers.Products.Domain.Supplier.Name</c>, via
/// <c>Raffa.SharedKernel.Suppliers.ISupplierNameLookup.GetNamesAsync</c> the host already called for
/// every other reason it needs a name (ADR-024 "names, never guids"). <see cref="NormalizedName"/>
/// is that same name run through <c>Raffa.Suppliers.Products.Application.SupplierNameNormalizer
/// .Normalize</c> -- lower-cased, punctuation-stripped, legal-suffix-stripped ("GmbH"/"SA"/"Ltd"/
/// ...) -- computed once by the host and carried in here rather than called from this type, because
/// <c>Raffa.Chat</c>'s allow-list (<c>[SharedKernel, AiGateway]</c>,
/// <c>Raffa.ArchitectureTests.DependencyDirectionTests</c>) forbids this module from referencing
/// <c>Raffa.Suppliers.Products</c> at all. <see cref="DomainGate"/> stays pure/synchronous either
/// way -- no DB, no LLM -- it only ever compares strings it is handed.
/// </summary>
public readonly record struct KnownSupplierName(string DisplayName, string NormalizedName);

/// <summary>
/// The Ask engine's admission gate (task E13/F06/US01/T01, ask-engine; ADR-024 "engine (R-ASK-01
/// ... 10)"; `inputs/requirements.md` R-ASK-02). Runs before the planner, before any context pack
/// is assembled and before any retrieval — <see cref="GateLabel.OffDomain"/>/
/// <see cref="GateLabel.Greeting"/>/<see cref="GateLabel.Legal"/>/<see cref="GateLabel.Capability"/>/
/// <see cref="GateLabel.NeedsDocument"/> are all answered directly from this result (redirect/
/// refusal/deterministic capability copy) without a single retrieval call or model call — only
/// <see cref="GateLabel.InDomain"/> ever reaches the planner (R-ASK-02: "Off-domain never
/// retrieves").
///
/// <para>
/// <b>Deterministic first (R-ASK-02: "keyword/pattern first")</b>: five fixed lexicons/rules are
/// checked, in order, before anything else — greeting, off-domain topic, legal, capability
/// (R-SYS-01/02's own vocabulary), then supplier-name resolution. A question naming a specific
/// supplier this tenant has never uploaded is <see cref="GateLabel.NeedsDocument"/> (R-ASK-03)
/// regardless of anything else about its phrasing — checked last among the deterministic rules so
/// a legal/capability/off-domain question that happens to also name a supplier is still classified
/// by its real topic, not misrouted to an upload prompt.
/// </para>
///
/// <para>
/// <b>Ambiguity fallback — a documented gap, not a silent guess</b>: ADR-024 says an ambiguous turn
/// should fall through to <c>IAiGateway.ClassifyAsync</c> with a fixed label set. That role's
/// concrete contract (<c>Raffa.AiGateway.Contracts.AiClassificationRequest</c>/
/// <c>AiClassificationResult</c>) is pinned to <c>AiDocumentType</c> (the document-admission
/// taxonomy: Msa, OrderForm, Invoice, ...) by the foundry-gateway feature that landed this contract
/// — there is no generic, caller-supplied label-set overload, and this task's own file scope
/// forbids touching any <c>Raffa.AiGateway</c> file other than
/// <c>Fixtures.FixtureAiGateway.AnswerAsync</c> ("this is the only AiGateway edit this phase" —
/// this task's own Files table). Calling <c>ClassifyAsync</c> here would therefore either fail to
/// compile against the wrong contract or, worse, silently reinterpret an <c>AiDocumentType</c>
/// verdict as a gate label it was never trained to produce — a fabricated signal, exactly what
/// Appendix C rule 10 forbids. Until a follow-up task gives the gateway a generic fixed-label-set
/// classify overload, an ambiguous turn (no deterministic rule fired) defaults to
/// <see cref="GateLabel.InDomain"/>: the safest default, because the planner + context pack +
/// guards downstream already abstain rather than fabricate when there is truly no evidence
/// (Appendix C rule 10) — an incorrectly-admitted off-domain question still cannot receive a
/// fabricated answer, it can only ever receive an honest "cannot determine".
/// </para>
///
/// <para>
/// <b>Recorded as a deviation, not a silent absorb</b>: this is a real, locked-ADR deviation
/// (ADR-024/R-ASK-02 say "then IAiGateway.ClassifyAsync with the fixed label set on ambiguity"),
/// so it belongs in <c>reports/open-questions.md</c> as its own <c>OQ-askv2-01x</c> entry — the
/// canonical place this repo's KB contract says an absorbed assumption must be recorded ("An
/// assumption you record is a result; an assumption you absorb silently is a defect"). This
/// implementer's own harness instructions forbid editing or committing that specific file directly
/// (multiple same-phase tasks writing it in parallel is what broke a prior phase barrier —
/// `PhaseBarrierMergeConflict` on `reports/open-questions.md`), so the entry itself is deferred to
/// whichever task/role is authorized to touch it; this doc comment is that task's pointer back to
/// the code.
/// </para>
/// </summary>
public sealed class DomainGate
{
    // R-ASK-02 greeting lexicon, it/en. Word-boundary matched so "hi" does not fire inside
    // "history" and "ciao" does not fire inside a longer word.
    private static readonly Regex GreetingPattern = new(
        @"\b(ciao|salve|buongiorno|buonasera|buond[iì]|hello|hi|hey|good\s+(morning|afternoon|evening))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Off-domain small-talk/topic lexicon (recipes, personal photos, weather, sport, jokes) — the
    // prototype's own oracle ("ricetta della carbonara", "foto di mia nonna") plus the obvious
    // English/Italian siblings a savings copilot must still decline warmly (R-ASK-02).
    private static readonly Regex OffDomainPattern = new(
        @"\b(ricetta|carbonara|cucin\w*|pasta|pizza|torta|dolce|recipe|cook\w*|" +
        @"foto|photo|picture|selfie|nonna|nonno|grandma|grandpa|" +
        @"meteo|weather|calcio|football|partita|" +
        @"barzelletta|joke|come\s+stai|how\s+are\s+you)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Task text, verbatim: "legal lexicon 'sue / causa / tribunale / valid under law /
    // enforceable'" — plus the obvious close synonyms so a real deployment does not miss the
    // first paraphrase it sees.
    private static readonly Regex LegalPattern = new(
        @"\b(sue|causa|tribunale|enforceable|valid\s+under\s+law|lawsuit|avvocat\w*|lawyer|" +
        @"legal\s+advice|azione\s+legale|denuncia|citare\s+in\s+giudizio)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // R-SYS-01/02's own vocabulary and the prototype's capabilities branch
    // ("what can you"/"cosa puoi"/"cosa sai fare"/"help"/"aiuto"/"how do i"/"come faccio").
    private static readonly Regex CapabilityPattern = new(
        @"\b(what\s+can\s+you|cosa\s+puoi|cosa\s+sai\s+fare|come\s+faccio|how\s+do\s+i|" +
        @"aiuto|help)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Same heuristic Raffa.Chat.Application.DeterministicQueryPlanner.CapitalizedSupplierNamePattern
    // already uses (see that field's own doc comment for why a capitalized run not at position 0
    // is, for the phrasings this gate sees, a proper noun worth checking against the tenant's
    // known suppliers): "AWS", "Salesforce", "Databricks" all match; a lower-cased mention does
    // not — this is a heuristic gate, not a name extractor.
    private static readonly Regex CapitalizedNamePattern = new(
        @"(?<!^)[A-Z][A-Za-z0-9&'-]*(?:\s+[A-Z][A-Za-z0-9&'-]*)*",
        RegexOptions.Compiled);

    /// <summary>
    /// Capitalized runs that are never a supplier name. The English first-person pronoun is the
    /// one that matters: it is capitalized mid-sentence by the rules of the language, so the
    /// pattern above matched it before anything else and R-STR-01's own worked question — "How
    /// should I approach the Salesforce renewal?" — was answered with "No I contract has been
    /// uploaded and validated", never even considering Salesforce. Found by the golden set
    /// (task E13/F06/US01/T02, GAP-ASK-PRONOUN-AS-SUPPLIER).
    /// </summary>
    private static readonly HashSet<string> NeverSupplierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "I", "I'm", "I've", "I'd", "I'll", "A", "An", "The", "OK", "Ok",
        "Raffa", "Ask", "EUR", "USD", "CHF", "GBP", "Q1", "Q2", "Q3", "Q4", "FY", "H1", "H2",
    };

    // Money shorthand and currency codes are never supplier names: "40K", "20 k", "1.5M", "€ 20k",
    // "EUR 20000" — the capitalized "K"/"M" after a number used to be extracted as a supplier
    // candidate ("No K contract has been uploaded"). Blanked before name extraction runs.
    private static readonly Regex MoneyShorthandPattern = new(
        @"(?:€|\$|£|\b(?:EUR|USD|CHF|GBP)\b)?\s?\b\d+(?:[.,]\d+)*\s?[kKmM]\b(?:€|\$|£)?|\b(?:EUR|USD|CHF|GBP)\b\s?\d[\d.,]*",
        RegexOptions.Compiled);

    // A one-letter or letter+digit token ("K", "M", "Q2") is never a proper noun worth a
    // needs-document redirect.
    private static readonly Regex NeverACandidatePattern = new(@"^[A-Z]\d?$", RegexOptions.Compiled);

    /// <summary>
    /// Classifies <paramref name="question"/>. Pure and synchronous: no I/O, no LLM call, always
    /// the same label for the same inputs (Appendix C rule 6) — the same determinism convention
    /// <c>AskRaffaQueryRouter</c>/<c>DeterministicQueryPlanner</c> already establish.
    /// </summary>
    /// <param name="question">The caller's raw question text.</param>
    /// <param name="knownSuppliers">Every supplier this tenant already has at least one contract
    /// for (resolved by the composition root — R-ASK-03; see <see cref="KnownSupplierName"/>'s own
    /// doc comment for how its two fields are computed). <see cref="ExtractSupplierCandidate"/>
    /// tries an exact, case-insensitive match against a capitalized candidate first, then falls
    /// back to a normalized/contains match against the whole question (task E27/F05/US01/T01,
    /// NW-80). An empty collection is valid input (a brand-new tenant with no contracts at all):
    /// every named supplier then resolves to <see cref="GateLabel.NeedsDocument"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="question"/> is null/blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="knownSuppliers"/> is null.</exception>
    public DomainGateResult Classify(string question, IReadOnlyCollection<KnownSupplierName> knownSuppliers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(knownSuppliers);

        var trimmed = question.Trim();

        if (GreetingPattern.IsMatch(trimmed))
        {
            return new DomainGateResult(GateLabel.Greeting, "matched the greeting lexicon (it/en).");
        }

        if (OffDomainPattern.IsMatch(trimmed))
        {
            return new DomainGateResult(
                GateLabel.OffDomain, "matched the off-domain topic lexicon (not a procurement question).");
        }

        if (LegalPattern.IsMatch(trimmed))
        {
            return new DomainGateResult(GateLabel.Legal, "matched the legal-advice lexicon.");
        }

        if (CapabilityPattern.IsMatch(trimmed))
        {
            return new DomainGateResult(GateLabel.Capability, "matched the capability/how-to lexicon.");
        }

        var (candidate, resolvedCandidate) = ExtractSupplierCandidate(trimmed, knownSuppliers);
        if (candidate is not null && resolvedCandidate is null)
        {
            return new DomainGateResult(
                GateLabel.NeedsDocument,
                $"named supplier '{candidate}' does not match any of this tenant's " +
                $"{knownSuppliers.Count} known supplier(s) (R-ASK-03).",
                candidate);
        }

        candidate = resolvedCandidate ?? candidate;

        // No deterministic rule matched off-domain content, and any named supplier (if one was
        // named at all) is already known to this tenant. See this type's own doc comment
        // ("Ambiguity fallback") for why this defaults to InDomain rather than a live classify
        // call.
        return new DomainGateResult(
            GateLabel.InDomain,
            "no deterministic off-domain/legal/capability rule matched" +
            (candidate is null ? "; no supplier named." : $"; named supplier '{candidate}' is known."),
            candidate);
    }

    /// <summary>
    /// Picks the capitalized run in <paramref name="question"/> most likely to be a supplier name,
    /// and says whether this tenant already knows it. Two tiers, tried in order (task
    /// E27/F05/US01/T01, NW-80 — "exact, then normalized/contains"):
    ///
    /// <para>
    /// <b>Tier 1 — exact, capitalized-run match</b> (unchanged from task E13/F06/US01/T02's own
    /// golden-set fix), two rules:
    /// <list type="number">
    /// <item><b>Every</b> capitalized run is considered, not just the first. "How should I approach
    /// the Salesforce renewal?" names one supplier, and it is not the first capitalized token.</item>
    /// <item>A run this tenant already has contracts for wins over one it does not, so a question
    /// that mentions a known supplier is never sent to "upload a document first" because some other
    /// capitalized word appeared earlier in the sentence.</item>
    /// </list>
    /// Runs in <see cref="NeverSupplierNames"/> are skipped outright.
    /// </para>
    ///
    /// <para>
    /// <b>Tier 2 — normalized/contains, against the whole question</b>: Tier 1 can only ever see a
    /// capitalized run, so a mention missing the supplier's own legal suffix ("AsterCloud" for
    /// "AsterCloud GmbH") or typed with no capital letter at all ("su salesforce") never produces a
    /// Tier-1 match to begin with — not a mismatch, a candidate that was never extracted. This tier
    /// normalizes <paramref name="question"/> the same way <see cref="KnownSupplierName.NormalizedName"/>
    /// was already normalized (case-folded, punctuation-stripped — see
    /// <see cref="NormalizeForContainsMatch"/>) and checks whether each known supplier's normalized
    /// name occurs as one or more whole words anywhere in it. Tried only when Tier 1 resolved
    /// nothing, so an exact capitalized match is never second-guessed by a looser one.
    /// </para>
    ///
    /// <para>
    /// When nothing resolves in either tier, the first capitalized run Tier 1 saw (if any) is
    /// returned as the unknown candidate — the honest R-ASK-03 answer is still "no contract for
    /// that supplier".
    /// </para>
    /// </summary>
    /// <returns>The candidate (or <see langword="null"/> when the question names none), and the
    /// known supplier's display name that matched (or <see langword="null"/> when none did).</returns>
    private static (string? Candidate, string? Resolved) ExtractSupplierCandidate(
        string question, IReadOnlyCollection<KnownSupplierName> knownSuppliers)
    {
        string? first = null;

        // Same length, so a match at position 0 is still "at position 0" for CapitalizedNamePattern.
        var scrubbed = MoneyShorthandPattern.Replace(question, m => new string(' ', m.Length));

        foreach (Match match in CapitalizedNamePattern.Matches(scrubbed))
        {
            var value = match.Value.Trim();
            if (value.Length == 0 || NeverSupplierNames.Contains(value) || NeverACandidatePattern.IsMatch(value))
            {
                continue;
            }

            foreach (var known in knownSuppliers)
            {
                if (string.Equals(known.DisplayName, value, StringComparison.OrdinalIgnoreCase))
                {
                    return (value, known.DisplayName);
                }
            }

            first ??= value;
        }

        var normalizedQuestion = NormalizeForContainsMatch(question);
        foreach (var known in knownSuppliers)
        {
            if (ContainsWholeWord(normalizedQuestion, known.NormalizedName))
            {
                return (first ?? known.DisplayName, known.DisplayName);
            }
        }

        return (first, null);
    }

    /// <summary>
    /// Lower-cases <paramref name="text"/> and drops every character that is neither a letter/digit
    /// nor whitespace, collapsing whitespace runs to a single space — the same punctuation-handling
    /// rule <c>Raffa.Suppliers.Products.Application.SupplierNameNormalizer.Normalize</c>'s own char
    /// loop applies (duplicated rather than referenced; see <see cref="KnownSupplierName"/>'s own doc
    /// comment for why <c>Raffa.Chat</c> cannot call that type directly), minus that method's
    /// legal-suffix stripping — stripping a *trailing* word only makes sense applied to a single
    /// supplier name, never to an arbitrary sentence. Never applied to a supplier name itself: the
    /// host is the one place both sides of Tier 2's comparison are normalized by the real
    /// <c>SupplierNameNormalizer</c>, so they agree on legal-suffix handling even though this method
    /// does not attempt it.
    /// </summary>
    private static string NormalizeForContainsMatch(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (char.IsWhiteSpace(c))
            {
                builder.Append(' ');
            }
            // every other character (punctuation) is dropped, exactly like SupplierNameNormalizer.
        }

        var words = builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words);
    }

    /// <summary>
    /// Whether <paramref name="needle"/> occurs in <paramref name="normalizedHaystack"/> as one or
    /// more whole, space-bounded words — never a same-word substring hit ("co" inside "coach").
    /// Both arguments are expected already normalized (single-spaced, lower-case, alphanumeric-only —
    /// <see cref="NormalizeForContainsMatch"/> for the question, <see cref="KnownSupplierName.NormalizedName"/>
    /// for the supplier), so padding each with one boundary space is enough to guarantee a match only
    /// ever lands on real word boundaries.
    /// </summary>
    private static bool ContainsWholeWord(string normalizedHaystack, string needle) =>
        needle.Length > 0 &&
        $" {normalizedHaystack} ".Contains($" {needle} ", StringComparison.Ordinal);
}
