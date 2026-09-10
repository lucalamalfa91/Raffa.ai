using System.Text.Json.Serialization;

namespace Raffa.AiEval;

/// <summary>
/// One golden-set case (task E13/F06/US01/T02, ask-golden-set; `inputs/requirements.md` R-EVD-03
/// "a golden set of >= 40 questions x 3 tenants' fixtures with expected kind, expected citation
/// corpora and expected numbers"; spec §15.3). Deserialized from <c>golden/*.json</c> — the cases
/// are data, not code, so extending the set is a JSON edit reviewable by a non-C# reader.
/// </summary>
/// <param name="Id">Stable, unique case id — the xunit test-case display name and the report row
/// key. Convention: <c>{fixture}-{intent}-{topic}-{lang}</c>.</param>
/// <param name="TenantFixture">Which of <c>TenantFixtures.TenantFixtureCatalog</c>'s three
/// workspaces this case runs against: <c>empty</c>, <c>seeded</c> or <c>needs-review</c>.</param>
/// <param name="Intent">The Ask intent / gate label this case is here to cover — one of
/// <c>greeting</c>, <c>off_domain</c>, <c>legal</c>, <c>capability</c>, <c>needs_document</c>,
/// <c>structured_fact</c>, <c>clause</c>, <c>market_compare</c>, <c>renewal_strategy</c>,
/// <c>portfolio_strategy</c>, <c>savings</c>, <c>document_status</c>, <c>quote_route</c>,
/// <c>navigate</c>. Reported per intent so a coverage gap is visible, and asserted against the
/// fixed list so a typo cannot silently create a phantom intent.</param>
/// <param name="Language">"it" or "en" (OQ-askv2-006: the set carries both).</param>
/// <param name="Question">The question, verbatim, as a user would type it.</param>
/// <param name="ExpectedKind">The grounded behaviour this turn must produce: <c>answer</c>,
/// <c>abstain</c>, <c>redirect</c> or <c>refusal</c> (R-ASK-07).</param>
/// <param name="ExpectedCitationCorpora">Every corpus that must appear among the reply's own
/// citations — a subset of <c>tenant</c>/<c>market</c>/<c>raffa</c>/<c>calc</c>. Empty means
/// "this case does not constrain the corpora"; <see cref="ExpectNoCitations"/> is the stronger,
/// explicit "there must be none".</param>
/// <param name="ExpectedNumbers">Dates, amounts and percentages that must appear <b>verbatim</b>
/// in <c>answerMarkdown</c> — the calculator/pack outputs the model is not allowed to restate
/// differently (R-ASK-06 point 2, R-STR-01 AC-1, R-PORT-02 AC-1).</param>
/// <param name="ExpectedMarkdownContains">Copy fragments the reply must carry — the prototype's
/// own wording for a redirect/refusal/abstain (`raffa-v2/app.jsx`'s <c>ask()</c> outcomes).</param>
/// <param name="ExpectedBodyContains">Fragments that must appear somewhere in the whole §6 reply
/// body rather than in the rendered markdown — the citation-card fields a user reads next to the
/// answer but which are never spliced into it: a market record's provenance label and
/// <c>updatedAt</c> (R-MKT-04), a citation's <c>recordId</c> (R-EVD-02), a feature card's
/// <c>href</c> (R-SYS-03).</param>
/// <param name="ForbiddenSubstrings">Case-specific forbidden text, on top of
/// <c>GoldenSetRunner</c>'s own always-applied engineer-chrome list (R-ASK-08).</param>
/// <param name="ExpectedActionHrefs">Every action href the reply must offer. Each one is
/// additionally checked against the capability catalog's own route patterns, so an href can never
/// be model-authored or carry an unresolved <c>{placeholder}</c> (R-SYS-02).</param>
/// <param name="ExpectAtLeastOneAction">R-SYS-02's "every reply carries >= 1 action when a
/// capability applies", asserted for the cases where one certainly does.</param>
/// <param name="ExpectNoCitations">Explicitly assert the reply cites nothing — the redirect and
/// refusal shapes, which offer a CTA rather than evidence (R-ASK-07).</param>
/// <param name="ExpectNoGatewayCall">R-ASK-01/R-ASK-02 "off-domain never retrieves", asserted at
/// its strongest: the recording <c>IAiGateway</c> decorator must see zero calls of any kind.</param>
/// <param name="KnownGap">Set only where the engine's <em>current</em> behaviour provably diverges
/// from the requirement this case encodes. See <see cref="GoldenCaseKnownGap"/>.</param>
/// <param name="Notes">Why this case exists / which requirement or prototype row it pins. Copied
/// into the report so a HITL reader sees the intent without opening the JSON.</param>
internal sealed record GoldenCase(
    string Id,
    string TenantFixture,
    string Intent,
    string Language,
    string Question,
    string ExpectedKind,
    IReadOnlyList<string>? ExpectedCitationCorpora = null,
    IReadOnlyList<string>? ExpectedNumbers = null,
    IReadOnlyList<string>? ExpectedMarkdownContains = null,
    IReadOnlyList<string>? ExpectedBodyContains = null,
    IReadOnlyList<string>? ForbiddenSubstrings = null,
    IReadOnlyList<string>? ExpectedActionHrefs = null,
    bool ExpectAtLeastOneAction = false,
    bool ExpectNoCitations = false,
    bool ExpectNoGatewayCall = false,
    GoldenCaseKnownGap? KnownGap = null,
    string? Notes = null)
{
    [JsonIgnore]
    public IReadOnlyList<string> Corpora => ExpectedCitationCorpora ?? [];

    [JsonIgnore]
    public IReadOnlyList<string> Numbers => ExpectedNumbers ?? [];

    [JsonIgnore]
    public IReadOnlyList<string> MarkdownContains => ExpectedMarkdownContains ?? [];

    [JsonIgnore]
    public IReadOnlyList<string> BodyContains => ExpectedBodyContains ?? [];

    [JsonIgnore]
    public IReadOnlyList<string> Forbidden => ForbiddenSubstrings ?? [];

    [JsonIgnore]
    public IReadOnlyList<string> ActionHrefs => ExpectedActionHrefs ?? [];

    public override string ToString() => Id;
}

/// <summary>
/// A recorded, deliberate divergence between what a requirement says a case should produce
/// (<see cref="GoldenCase.ExpectedKind"/> and friends) and what the engine <em>currently</em>
/// produces. This task owns <c>backend/tests/Raffa.AiEval/**</c> only and is explicitly
/// forbidden from editing the engine, so a divergence it finds is recorded here and surfaced in
/// the report rather than silently written into the expectation (which would bake the defect in as
/// if it were the requirement) or left to fail the suite (which would make the golden set unusable
/// as a regression gate for everything else).
///
/// <para>
/// <b>Verdict semantics</b>: a case with a known gap passes when the reply matches
/// <em>either</em> the requirement's expectation (the gap has since been fixed — the report then
/// stops listing it, with no JSON edit needed) <em>or</em> exactly this recorded observation. Any
/// third behaviour is a genuine failure. So the gap never hides a regression: it only tolerates
/// the one, named, already-reported deviation.
/// </para>
/// </summary>
/// <param name="Id">Stable gap id, e.g. <c>GAP-ASK-PRONOUN-SUPPLIER</c>. Must appear in
/// <c>GoldenSetKnownGaps.All</c> — <c>GoldenSetTests</c> asserts it, so nobody can quietly invent
/// a new tolerated deviation inside a JSON file.</param>
/// <param name="ObservedKind">The reply kind the engine actually returns today.</param>
/// <param name="ObservedMarkdownContains">Copy fragments the engine's current reply carries —
/// how the observation is pinned precisely enough to be a real assertion, not a wildcard.</param>
/// <param name="Note">One sentence naming the requirement the current behaviour misses.</param>
internal sealed record GoldenCaseKnownGap(
    string Id,
    string ObservedKind,
    IReadOnlyList<string>? ObservedMarkdownContains = null,
    string? Note = null)
{
    [JsonIgnore]
    public IReadOnlyList<string> ObservedContains => ObservedMarkdownContains ?? [];
}
