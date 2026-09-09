using System.Text.RegularExpressions;
using Contigo.Chat.Domain;

namespace Contigo.Chat.Application.Gate;

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
/// concrete contract (<c>Contigo.AiGateway.Contracts.AiClassificationRequest</c>/
/// <c>AiClassificationResult</c>) is pinned to <c>AiDocumentType</c> (the document-admission
/// taxonomy: Msa, OrderForm, Invoice, ...) by the foundry-gateway feature that landed this contract
/// — there is no generic, caller-supplied label-set overload, and this task's own file scope
/// forbids touching any <c>Contigo.AiGateway</c> file other than
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

    // Same heuristic Contigo.Chat.Application.DeterministicQueryPlanner.CapitalizedSupplierNamePattern
    // already uses (see that field's own doc comment for why a capitalized run not at position 0
    // is, for the phrasings this gate sees, a proper noun worth checking against the tenant's
    // known suppliers): "AWS", "Salesforce", "Databricks" all match; a lower-cased mention does
    // not — this is a heuristic gate, not a name extractor.
    private static readonly Regex CapitalizedNamePattern = new(
        @"(?<!^)[A-Z][A-Za-z0-9&'-]*(?:\s+[A-Z][A-Za-z0-9&'-]*)*",
        RegexOptions.Compiled);

    /// <summary>
    /// Classifies <paramref name="question"/>. Pure and synchronous: no I/O, no LLM call, always
    /// the same label for the same inputs (Appendix C rule 6) — the same determinism convention
    /// <c>AskContigoQueryRouter</c>/<c>DeterministicQueryPlanner</c> already establish.
    /// </summary>
    /// <param name="question">The caller's raw question text.</param>
    /// <param name="knownSupplierNames">Every supplier name this tenant already has at least one
    /// contract for (resolved by the composition root via
    /// <c>Contigo.SharedKernel.Suppliers.ISupplierNameLookup</c> — R-ASK-03) — compared
    /// case-insensitively against a capitalized candidate extracted from
    /// <paramref name="question"/>. An empty collection is valid input (a brand-new tenant with no
    /// contracts at all): every named supplier then resolves to <see cref="GateLabel.NeedsDocument"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="question"/> is null/blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="knownSupplierNames"/> is null.</exception>
    public DomainGateResult Classify(string question, IReadOnlyCollection<string> knownSupplierNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(knownSupplierNames);

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

        var candidate = ExtractSupplierCandidate(trimmed);
        if (candidate is not null)
        {
            var resolved = knownSupplierNames.Any(
                name => string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase));

            if (!resolved)
            {
                return new DomainGateResult(
                    GateLabel.NeedsDocument,
                    $"named supplier '{candidate}' does not match any of this tenant's " +
                    $"{knownSupplierNames.Count} known supplier(s) (R-ASK-03).",
                    candidate);
            }
        }

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

    private static string? ExtractSupplierCandidate(string question)
    {
        var match = CapitalizedNamePattern.Match(question);
        return match.Success ? match.Value : null;
    }
}
